using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PwshMcp.Shared;

internal static class Program
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static class K
    {
        public const string Host="PwshMcp.Host", Broker="PwshMcp.Host.Broker", Peer="PwshMcp.Host.Peer";
        public const string Store="store", Path="path", Jobs="jobs", Grants="grants", Archive="archived", Hashes="fingerprints", Cancelled="cancelled";
        public const string Dispatch="Dispatch", Sweep="Sweep", Compact="ArchiveOverflow", Restore="Restore", Save="Save";
        public const string Sid="history-test-owner", User="test", Client="history-test", Description="History test", Code="'archive-output'";
        public const string Output="archive-output", Error="archive-error", Env="PRIVATE_TEST_VALUE", Secret="not-persisted";
        public const string State="state.json", History="history", JobDir="jobs", Ext=".bin", Changed="different", Other="other-owner";
    }
    static readonly Assembly Host=Assembly.Load(K.Host);
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static object Field(object obj,string name)=>obj.GetType().GetField(name,Private)!.GetValue(obj)!;
    static object? Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,Private|BindingFlags.Public)!.Invoke(obj,args);
    static object Broker(string path)
    {
        var obj=Activator.CreateInstance(Host.GetType(K.Broker)!,true)!;
        var store=Field(obj,K.Store);
        store.GetType().GetField(K.Path,Private)!.SetValue(store,path);
        return obj;
    }
    static object Peer(string sid=K.Sid)=>Activator.CreateInstance(Host.GetType(K.Peer)!,sid,K.User,42,"",false,Environment.ProcessId)!;
    static Response Send(object broker,Request request,object? peer=null)=>(Response)Call(broker,K.Dispatch,request,peer??Peer())!;
    static string Hash(RunSpec run)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(run,Protocol.Json))));
    static string Id(string request)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{K.Sid}|42|{K.Client}|{request}")))[..32].ToLowerInvariant();
    static RunSpec Run(string cwd)=>new(){Description=K.Description,Code=K.Code,Cwd=cwd,Privilege=Privilege.Normal,Env=new(){[K.Env]=K.Secret}};
    static void Save(object broker)
    {
        var jobs=(Dictionary<string,JobInfo>)Field(broker,K.Jobs);
        var grants=(Dictionary<string,AccessInfo>)Field(broker,K.Grants);
        Call(Field(broker,K.Store),K.Save,grants.Values,jobs.Values,Field(broker,K.Hashes));
    }
    static void Cases(string root)
    {
        var path=System.IO.Path.Combine(root,K.State);
        var broker=Broker(path);
        var jobs=(Dictionary<string,JobInfo>)Field(broker,K.Jobs);
        var hashes=(Dictionary<string,string>)Field(broker,K.Hashes);
        var spec=Run(root);
        for(var i=0;i<125;i++)
        {
            var req=i.ToString();var id=Id(req);
            jobs[id]=new(){Id=id,Client=K.Client,OwnerSid=K.Sid,SessionId=42,Run=spec,State=i==1?JobState.Rejected:JobState.Succeeded,
                CreatedAt=DateTimeOffset.UtcNow.AddMinutes(-200+i),Result=i==1?null:new(){Output=K.Output,Error=K.Error}};
            hashes[$"{K.Sid}|42|{K.Client}|{req}"]=Hash(spec);
        }
        var live=Id("live");jobs[live]=new(){Id=live,OwnerSid=K.Sid,SessionId=42,State=JobState.NeedsApproval,Run=spec,CreatedAt=DateTimeOffset.UtcNow.AddHours(-5)};
        Call(broker,K.Compact);
        Check(jobs.Count==101&&jobs.ContainsKey(live),"100 completed plus every live job retained");
        Check(((HashSet<string>)Field(broker,K.Archive)).Count==100&&hashes.Count==0,"bounded tracking and released fingerprints");
        Check(jobs.Values.Where(j=>j.Id!=live).All(j=>j.Run.Code.Length==0&&j.Run.Env.Count==0&&j.Result?.Output==""),"hot summaries release command/env/output");
        var files=Directory.GetFiles(System.IO.Path.Combine(root,K.History,K.JobDir));
        Check(files.Length==125,"all finished jobs archived including null-result rejection");
        var encrypted=File.ReadAllBytes(files[0]);
        Check(!Encoding.UTF8.GetString(encrypted).Contains(K.Output),"output absent from plaintext archive");
        var stamp=File.GetLastWriteTimeUtc(files[0]);Call(broker,K.Compact);
        Check(File.GetLastWriteTimeUtc(files[0])==stamp,"poll does not rewrite archive");
        var snapshot=Send(broker,new(){Op=Ops.Snapshot}).Snapshot!;
        Check(snapshot.Jobs.Count==101&&snapshot.Jobs.Any(j=>j.Id==live),"snapshot includes oldest live job");
        foreach(var id in new[]{Id("0"),Id("124")})
        {
            var status=Send(broker,new(){Op=Ops.Status,Id=id});
            Check(status.Ok&&status.Job!.Run.Code==K.Code&&status.Job.Result!.Error==K.Error&&status.Job.Run.Env.Count==0,"hot or evicted detail restored without env");
            Check(Send(broker,new(){Op=Ops.Output,Id=id,Cursor=8}).Output==K.Output[8..],"output cursor survives archive");
            Check(!Send(broker,new(){Op=Ops.Status,Id=id},Peer(K.Other)).Ok,"archive ownership checked");
        }
        Check(!Send(broker,new(){Op=Ops.Status,Id="../state.json"}).Ok,"invalid path rejected");
        var request=new Request{Op=Ops.Submit,Id="0",Client=K.Client,Run=spec};
        Check(Send(broker,request).Job!.Id==Id("0")&&jobs.Count==101,"evicted submit remains idempotent");
        Check(!Send(broker,request with{Run=spec with{Code=K.Changed}}).Ok,"archived conflict rejected");
        Send(broker,new(){Op=Ops.Cancel,Id=Id("0")});
        Check(((HashSet<string>)Field(broker,K.Cancelled)).Count==0,"cancel completed does not grow cache");
        Save(broker);
        var restored=Broker(path);Call(restored,K.Restore);Call(restored,K.Compact);
        Check(Send(restored,new(){Op=Ops.Output,Id=Id("0")}).Output==K.Output,"evicted output after restart");
        Check(Send(restored,request).Job!.Id==Id("0"),"idempotency after restart");

        var grants=(Dictionary<string,AccessInfo>)Field(broker,K.Grants);
        for(var i=0;i<110;i++){var id=Id("grant"+i);grants[id]=new(){Id=id,State=AccessState.Revoked};}
        var active=Id("active");grants[active]=new(){Id=active,State=AccessState.Active};
        Call(broker,K.Compact);
        Check(grants.Count==101&&grants.ContainsKey(active),"bounded ended grants and live preserved");

        var failureRoot=System.IO.Path.Combine(root,"failure");Directory.CreateDirectory(failureRoot);
        File.WriteAllText(System.IO.Path.Combine(failureRoot,K.History),"blocked directory");
        var failed=Broker(System.IO.Path.Combine(failureRoot,K.State));
        var failedJobs=(Dictionary<string,JobInfo>)Field(failed,K.Jobs);
        var failedId=Id("failed-write");failedJobs[failedId]=new(){Id=failedId,State=JobState.Succeeded,Run=spec,Result=new(){Output=K.Output}};
        Call(failed,K.Compact);
        Check(failedJobs[failedId].Result!.Output==K.Output&&failedJobs[failedId].Run.Code==K.Code,"write failure preserves original payload");
        File.Delete(System.IO.Path.Combine(failureRoot,K.History));Call(failed,K.Compact);
        Check(failedJobs[failedId].Result!.Output=="","write retry releases payload after success");

        var corrupt=System.IO.Path.Combine(root,K.History,K.JobDir,Id("0")+K.Ext);
        File.WriteAllBytes(corrupt,[0,1,2]);
        try{Send(broker,new(){Op=Ops.Output,Id=Id("0")});throw new Exception("corrupt history returned success");}
        catch(TargetInvocationException ex){Check(ex.InnerException is System.ComponentModel.Win32Exception,"corrupt archive is reported, not empty success");}
    }
    static int Main()
    {
        var root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"PwshMcp-history-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try{Cases(root);Console.WriteLine($"PASS: {checks} history checks");return 0;}
        finally{Directory.Delete(root,true);}
    }
}
