using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using PwshMcp.Shared;

namespace PwshMcp.Host;

internal static class McpNames
{
    public const string Context="context";
    public const string Description="description",TaskName="taskName";
    public const string AuditGuide="Every run, session_reset and session_close must include a concise human-readable description of the action and purpose. Supply taskName when first requesting administrator access (including the first elevated run). Do not put credentials or secrets in audit descriptions or task names. These fields are retained in audit metadata. Task names do not narrow client-wide authorization.";
    public const string JsonRpc="jsonrpc",Id="id",Method="method",Params="params",Name="name",Arguments="arguments",Code="code",Cwd="cwd",Privilege="privilege",Environment="env",Session="session",Timeout="timeoutSeconds",RequestId="requestId",Cursor="cursor",Reason="reason",Profile="profile";
    public const string Initialize="initialize",List="tools/list",Call="tools/call",Ping="ping";
    public const string Run="run",Status="status",Output="output",Cancel="cancel",Grants="grants",RequestAccess="request_access",SessionReset="session_reset",SessionClose="session_close",ContextSet="context_set",ContextClear="context_clear",EnvInfo="env_info";
}
internal sealed class Mcp
{
    private readonly BrokerClient broker=new();
    private readonly string client;
    public Mcp(string name){client=name;}
    private readonly Dictionary<string,Dictionary<string,string?>> contexts=[];
    public async Task RunAsync(CancellationToken ct)
    {
        var worker=StartWorker();
        try
        {
            while(!ct.IsCancellationRequested)
            {
                var line=await Console.In.ReadLineAsync(ct);if(line is null)break;
                if(worker.HasExited){worker.Dispose();worker=StartWorker();}
                JsonNode? id=null;
                try
                {
                    if(line.Length>Names.MaxMessage)throw new InvalidDataException();
                    var msg=JsonNode.Parse(line)??throw new JsonException();id=msg[McpNames.Id]?.DeepClone();
                    if(id is null)continue;
                    var method=msg[McpNames.Method]?.GetValue<string>();
                    object result=method switch
                    {
                        McpNames.Initialize=>new{protocolVersion="2025-06-18",capabilities=new{tools=new{listChanged=false}},serverInfo=new{name=Protocol.Product,version=Protocol.Version},instructions=McpNames.AuditGuide},
                        McpNames.Ping=>new{},
                        McpNames.List=>new{tools=Tools()},
                        McpNames.Call=>await Call(msg[McpNames.Params],ct),
                        _=>throw new NotSupportedException()
                    };
                    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id,result},Protocol.Json));
                }
                catch(Exception ex)
                {
                    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id,error=new{code=ex is JsonException?-32700:ex is NotSupportedException?-32601:-32602,message=ex.GetType().Name}},Protocol.Json));
                }
            }
        }
        finally{try{if(!worker.HasExited)worker.Kill(true);}catch(InvalidOperationException){}worker.Dispose();}
    }
    private Process StartWorker()
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(Names.Worker);start.ArgumentList.Add(Names.Client);start.ArgumentList.Add(client);start.ArgumentList.Add(Names.Parent);start.ArgumentList.Add(Environment.ProcessId.ToString());
        return Process.Start(start)??throw new IOException();
    }
    private async Task<object> Call(JsonNode? p,CancellationToken ct)
    {
        try
        {
            var name=p?[McpNames.Name]?.GetValue<string>()??throw new InvalidDataException();
            var a=p[McpNames.Arguments]??new JsonObject();
            var session=a[McpNames.Session]?.GetValue<string>();
            if(name==McpNames.ContextSet)
            {
                var key=session??throw new InvalidDataException();
                contexts[key]=a[McpNames.Environment]?.Deserialize<Dictionary<string,string?>>(Protocol.Json)??[];
                return Text(new{context=key,variables=contexts[key].Keys});
            }
            if(name==McpNames.ContextClear){contexts.Remove(session??throw new InvalidDataException());return Text(new{cleared=true});}
            if(name==McpNames.EnvInfo)return Text(new{mode="fresh",source="execution_identity_current_machine_and_user",overrides=contexts.ToDictionary(x=>x.Key,x=>x.Value.Keys.ToArray()),values="hidden"});
            Request request;
            if(name is McpNames.Run or McpNames.SessionReset or McpNames.SessionClose)
            {
                var description=a[McpNames.Description]?.GetValue<string>();
                if(string.IsNullOrWhiteSpace(description))return Text(new{error=Errors.Description},true);
                var env=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
                var context=a[McpNames.Context]?.GetValue<string>()??session;
                if(context is not null&&contexts.TryGetValue(context,out var baseEnv))foreach(var item in baseEnv)env[item.Key]=item.Value;
                var patch=a[McpNames.Environment]?.Deserialize<Dictionary<string,string?>>(Protocol.Json);
                if(patch is not null)foreach(var item in patch)env[item.Key]=item.Value;
                var code=name==McpNames.SessionReset?SessionCommands.Reset:name==McpNames.SessionClose?SessionCommands.Close:a[McpNames.Code]?.GetValue<string>()??throw new InvalidDataException();
                var privilege=a[McpNames.Privilege]?.GetValue<string>()??"normal";
                if(privilege is not ("normal" or "elevated"))throw new InvalidDataException();
                request=new(){Op=Ops.Submit,Id=a[McpNames.RequestId]?.GetValue<string>()??Guid.NewGuid().ToString("N"),Client=client,Label=a[McpNames.Reason]?.GetValue<string>()??"",TaskName=a[McpNames.TaskName]?.GetValue<string>()??"",Run=new(){Description=description,Code=code,Cwd=a[McpNames.Cwd]?.GetValue<string>()??Environment.CurrentDirectory,Privilege=privilege=="normal"?Privilege.Normal:Privilege.Elevated,Session=session,Env=env,TimeoutSeconds=a[McpNames.Timeout]?.GetValue<int>()??600,Profile=a[McpNames.Profile]?.GetValue<bool>()??false}};
            }
            else request=new(){Op=name switch{McpNames.Status=>Ops.Status,McpNames.Output=>Ops.Output,McpNames.Cancel=>Ops.Cancel,McpNames.Grants=>Ops.Snapshot,McpNames.RequestAccess=>Ops.RequestAccess,_=>throw new NotSupportedException()},Id=a[McpNames.Id]?.GetValue<string>()??"",Client=client,Label=a[McpNames.Reason]?.GetValue<string>()??"",TaskName=a[McpNames.TaskName]?.GetValue<string>()??"",Cursor=a[McpNames.Cursor]?.GetValue<int>()??0};
            var response=await broker.SendAsync(request,ct);
            return Text(response,!response.Ok);
        }
        catch(Exception ex) when(ex is IOException or TimeoutException or OperationCanceledException){return Text(new{error="broker_unavailable",detail=ex.GetType().Name},true);}
    }
    private static object Text(object value,bool error=false)=>new{content=new[]{new{type="text",text=JsonSerializer.Serialize(value,Protocol.Json)}},isError=error};
    private static object[] Tools()
    {
        object S(string type)=>new{type};
        object Tool(string name,string description,Dictionary<string,object> properties,string[] required)=>new{name,description,inputSchema=new{type="object",properties,required,additionalProperties=false}};
        var run=new Dictionary<string,object>{{McpNames.Code,S("string")},{McpNames.Cwd,S("string")},{McpNames.Privilege,new{type="string",@enum=new[]{"normal","elevated"}}},{McpNames.Environment,new{type="object",additionalProperties=new{type=new[]{"string","null"}}}},{McpNames.Session,S("string")},{McpNames.Timeout,S("integer")},{McpNames.RequestId,S("string")},{McpNames.Reason,S("string")},{McpNames.Profile,S("boolean")}};
        run[McpNames.Context]=S("string");
        var descriptionSchema=new{type="string",minLength=1,maxLength=1000,description="Audit description: what this command changes or checks, and why; do not include secrets."};
        var taskSchema=new{type="string",minLength=1,maxLength=200,description="Task title for the first administrator permission request. Required when no active or pending grant exists."};
        run[McpNames.Description]=descriptionSchema;run[McpNames.TaskName]=taskSchema;
        var session=new Dictionary<string,object>{{McpNames.Session,S("string")},{McpNames.Privilege,S("string")},{McpNames.RequestId,S("string")},{McpNames.Description,descriptionSchema},{McpNames.TaskName,taskSchema}};
        return [Tool(McpNames.Run,"Run PowerShell in a fresh environment, or an explicit persistent session. Returns task id immediately; elevated requests require tray authorization.",run,[McpNames.Code,McpNames.Description]),Tool(McpNames.Status,"Get task status. outcomeUnknown requires checking actual effects before retry.",new(){{McpNames.Id,S("string")}},[McpNames.Id]),Tool(McpNames.Output,"Read completed output using a character cursor.",new(){{McpNames.Id,S("string")},{McpNames.Cursor,S("integer")}},[McpNames.Id]),Tool(McpNames.Cancel,"Request cancellation; does not roll back changes.",new(){{McpNames.Id,S("string")}},[McpNames.Id]),Tool(McpNames.Grants,"Read authorization and task state.",[],[]),Tool(McpNames.RequestAccess,"Request a trusted administrator session via the tray without opening UAC.",new(){{McpNames.Reason,S("string")},{McpNames.TaskName,taskSchema}},[McpNames.TaskName]),Tool(McpNames.SessionReset,"Recreate a persistent shell with a fresh environment; loses shell state.",session,[McpNames.Session,McpNames.Description]),Tool(McpNames.SessionClose,"Close persistent shell.",session,[McpNames.Session,McpNames.Description]),Tool(McpNames.ContextSet,"Set explicit environment overrides for a named context/session; values are not logged.",new(){{McpNames.Session,S("string")},{McpNames.Environment,new{type="object",additionalProperties=new{type=new[]{"string","null"}}}}},[McpNames.Session,McpNames.Environment]),Tool(McpNames.ContextClear,"Clear explicit environment overrides.",new(){{McpNames.Session,S("string")}},[McpNames.Session]),Tool(McpNames.EnvInfo,"Describe environment sources and override names; never return secret values.",[],[])];
    }
}
