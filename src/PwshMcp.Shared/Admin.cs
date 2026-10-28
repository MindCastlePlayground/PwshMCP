using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PwshMcp.Shared;

public static class AdminNames
{
    public const string Mode = "admin", Host = "PwshMcp.Host.exe", Tray = "PwshMcp.Tray.exe", Launcher = "PwshMcp.Admin.exe";
    public const string Folder = "PwshMCP", Prefix = "PwshMCP-Admin-";
    public const string Offline = "admin_unavailable", Forbidden = "admin_forbidden", Invalid = "admin_invalid_request", Protected = "admin_requires_protected_install", Worker = "admin_worker_failed";
    public const int MaxMessage = 4096;
    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Folder);
    public static string Pipe => Protocol.Pipe + ".admin." + WindowsIdentity.GetCurrent().User!.Value + "." + Process.GetCurrentProcess().SessionId;
}
public enum AdminOp { Status, Approve, Resume }
public sealed record AdminRequest
{
    public AdminOp Op { get; init; }
    public string Id { get; init; } = "";
    public DateTimeOffset? ExpiresAt { get; init; }
}
public sealed record AdminResponse
{
    public bool Ok { get; init; } = true;
    public string Error { get; init; } = "";
    public bool Ready { get; init; }
    public AccessInfo? Grant { get; init; }
}
public static class ProcessPath
{
    public static string Read(int pid)
    {
        var handle = OpenProcess(0x1000, false, (uint)pid);
        if (handle == IntPtr.Zero) throw new UnauthorizedAccessException();
        try
        {
            var value = new StringBuilder(32768); var length = value.Capacity;
            if (!QueryFullProcessImageName(handle, 0, value, ref length)) throw new UnauthorizedAccessException();
            return Path.GetFullPath(value.ToString());
        }
        finally { CloseHandle(handle); }
    }
    public static bool Is(int pid, string expected) => string.Equals(Read(pid), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int length);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
public sealed class AdminClient
{
    public string? ExpectedHostPath { get; set; }
    public async Task<AdminResponse> SendAsync(AdminRequest request, CancellationToken ct = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(request.Op == AdminOp.Status ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(8));
        using var pipe = new NamedPipeClientStream(".", AdminNames.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(limit.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) ||
            !ProcessPath.Is((int)pid, ExpectedHostPath ?? Path.Combine(AdminNames.Directory, AdminNames.Host))) throw new UnauthorizedAccessException();
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Protocol.Json).AsMemory(), limit.Token);
        var line = await reader.ReadLineAsync(limit.Token) ?? throw new IOException(AdminNames.Offline);
        return JsonSerializer.Deserialize<AdminResponse>(line, Protocol.Json) ?? throw new IOException(AdminNames.Invalid);
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
}
