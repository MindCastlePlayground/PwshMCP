using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PwshMcp.Shared;

public static class Protocol
{
    public const string Pipe = "PwshMcp.Broker.v1";
    public const string Product = "PwshMCP";
    public const string Version = "0.1.0";
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
}
public static class Ops
{
    public const string Snapshot="snapshot", Submit="submit", Status="status", Output="output", Cancel="cancel", RequestAccess="request_access", Approve="approve", Deny="deny", Revoke="revoke", WorkerNext="worker_next", WorkerResult="worker_result", SessionReset="session_reset";
}
public enum JobState { NeedsApproval, NeedsSession, Queued, Running, Succeeded, Failed, Cancelled, Rejected, Expired, OutcomeUnknown }
public enum AccessState { Pending, Active, Denied, Revoked, Expired }
public enum Privilege { Normal, Elevated }
public sealed record RunSpec
{
    public string Code {get;init;}="";
    public string Cwd {get;init;}="";
    public Privilege Privilege {get;init;}=Privilege.Elevated;
    public Dictionary<string,string?> Env {get;init;}=new(StringComparer.OrdinalIgnoreCase);
    public string? Session {get;init;}
    public int TimeoutSeconds {get;init;}=600;
    public bool Profile {get;init;}
}
public sealed record Request
{
    public string Op {get;init;}="";
    public string Id {get;init;}="";
    public string Client {get;init;}="agent";
    public string Label {get;init;}="";
    public RunSpec? Run {get;init;}
    public DateTimeOffset? ExpiresAt {get;init;}
    public int Cursor {get;init;}
    public ExecutionResult? Result {get;init;}
}
public sealed record AccessInfo
{
    public string Id {get;init;}="";
    public string Client {get;init;}="";
    public string Label {get;init;}="";
    public string User {get;init;}="";
    public string Sid {get;init;}="";
    public int SessionId {get;init;}
    public AccessState State {get;set;}
    public DateTimeOffset CreatedAt {get;init;}=DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt {get;set;}
    public DateTimeOffset? HeartbeatAt {get;set;}
}
public sealed record JobInfo
{
    public string Id {get;init;}="";
    public string Client {get;init;}="";
    public string OwnerSid {get;init;}="";
    public int SessionId {get;init;}
    public string? GrantId {get;set;}
    public RunSpec Run {get;init;}=new();
    public JobState State {get;set;}
    public DateTimeOffset CreatedAt {get;init;}=DateTimeOffset.UtcNow;
    public ExecutionResult? Result {get;set;}
}
public sealed record ExecutionResult
{
    public int ExitCode {get;init;}
    public string Output {get;init;}="";
    public string Error {get;init;}="";
    public bool Cancelled {get;init;}
    public string Engine {get;init;}="";
    public string Identity {get;init;}="";
    public DateTimeOffset EnvironmentAt {get;init;}=DateTimeOffset.UtcNow;
    public string EnvironmentId {get;init;}=Guid.NewGuid().ToString("N");
}
public sealed record Snapshot
{
    public List<AccessInfo> Grants {get;init;}=[];
    public List<JobInfo> Jobs {get;init;}=[];
    public bool ElevatedBroker {get;init;}
    public string Version {get;init;}=Protocol.Version;
}
public sealed record Response
{
    public bool Ok {get;init;}=true;
    public string Error {get;init;}="";
    public string? Id {get;init;}
    public Snapshot? Snapshot {get;init;}
    public JobInfo? Job {get;init;}
    public AccessInfo? Grant {get;init;}
    public string? Output {get;init;}
    public int Cursor {get;init;}
    public bool Cancel {get;init;}
}
public sealed class BrokerClient
{
    public async Task<Response> SendAsync(Request request, CancellationToken ct=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var pipe=new NamedPipeClientStream(".",Protocol.Pipe,PipeDirection.InOut,PipeOptions.Asynchronous,System.Security.Principal.TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(timeout.Token);
        using var writer=new StreamWriter(pipe,new UTF8Encoding(false),leaveOpen:true){AutoFlush=true};
        using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request,Protocol.Json).AsMemory(),timeout.Token);
        var line=await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Broker disconnected");
        return JsonSerializer.Deserialize<Response>(line,Protocol.Json) ?? throw new IOException("Invalid broker response");
    }
}
