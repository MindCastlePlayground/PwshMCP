using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace PwshMcp.Shared;

public static class Protocol
{
#if PWMC_TEST_CHANNEL
    public const string Pipe = "PwshMcp.Broker.Tests.v1";
    public const string Product = "PwshMCP.Tests";
#else
    public const string Pipe = "PwshMcp.Broker.v1";
    public const string Product = "PwshMCP";
#endif
    public const string Version = "0.4.0";
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
}
public static class Ops
{
    public const string Snapshot="snapshot", Submit="submit", Status="status", Output="output", Cancel="cancel", RequestAccess="request_access", Approve="approve", Deny="deny", Revoke="revoke", WorkerNext="worker_next", WorkerResult="worker_result", SessionReset="session_reset";
}
public enum JobState { NeedsApproval, NeedsSession, Queued, Running, Succeeded, Failed, Cancelled, Rejected, Expired, OutcomeUnknown }
public enum AccessState { Pending, Active, Denied, Revoked, Expired }
public enum AccessEnd { Unknown, RequestTimeout, Duration, Session, Restart }
public enum Privilege { Normal, Elevated }
public sealed record RunSpec
{
    public string Description {get;init;}="";
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
    public string TaskName {get;init;}="";
    public string? ClientName {get;init;}
    public string Op {get;init;}="";
    public string Id {get;init;}="";
    public string Client {get;init;}="agent";
    public string Label {get;init;}="";
    public string? TaskId {get;init;}
    public RunSpec? Run {get;init;}
    public DateTimeOffset? ExpiresAt {get;init;}
    public int Cursor {get;init;}
    public ExecutionResult? Result {get;init;}
}
public sealed record AccessInfo
{
    public string TaskName {get;init;}="";
    public string ClientName {get;set;}="";
    public string Id {get;init;}="";
    public string Client {get;init;}="";
    public string Label {get;init;}="";
    public string User {get;init;}="";
    public string Sid {get;init;}="";
    public int SessionId {get;init;}
    public AccessState State {get;set;}
    public AccessEnd EndReason {get;set;}
    public DateTimeOffset CreatedAt {get;init;}=DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt {get;set;}
    public DateTimeOffset? HeartbeatAt {get;set;}
}
public sealed record JobInfo
{
    public string Id {get;init;}="";
    public string Client {get;init;}="";
    public string ClientName {get;init;}="";
    public string TaskName {get;init;}="";
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
    public string? ExpectedHostPath {get;set;}
    public async Task<Response> SendAsync(Request request, CancellationToken ct=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var pipe=new NamedPipeClientStream(".",Protocol.Pipe,PipeDirection.InOut,PipeOptions.Asynchronous,System.Security.Principal.TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(timeout.Token);
        VerifyServer(pipe);
        using var writer=new StreamWriter(pipe,new UTF8Encoding(false),leaveOpen:true){AutoFlush=true};
        using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request,Protocol.Json).AsMemory(),timeout.Token);
        var line=await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Broker disconnected");
        return JsonSerializer.Deserialize<Response>(line,Protocol.Json) ?? throw new IOException("Invalid broker response");
    }
    private void VerifyServer(NamedPipeClientStream pipe)
    {
        if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out var pid))throw new IOException("Cannot identify broker");
        using var process=Process.GetProcessById((int)pid);
        var handle=OpenProcess(0x1000,false,pid);
        if(handle==IntPtr.Zero)throw new IOException("Cannot verify broker executable");
        try
        {
            var buffer=new StringBuilder(32768);var length=buffer.Capacity;
            if(!QueryFullProcessImageName(handle,0,buffer,ref length))throw new IOException("Cannot verify broker path");
            var expected=ExpectedHostPath??Path.Combine(AppContext.BaseDirectory,"PwshMcp.Host.exe");
            if(!string.Equals(Path.GetFullPath(expected),Path.GetFullPath(buffer.ToString()),StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected broker executable");
        }
        finally{CloseHandle(handle);}
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref int length);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
}
