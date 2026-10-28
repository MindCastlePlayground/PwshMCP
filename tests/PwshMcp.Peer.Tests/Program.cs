using System.IO.Pipes;
using System.Security.Principal;
using PwshMcp.Host;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var legacy = args.Contains("--legacy", StringComparer.Ordinal);
            await PeerTests.Run(legacy);
            Console.WriteLine(legacy
                ? "PASS: --legacy reproduced the impersonation leak after awaits."
                : "PASS: Peer.Read returns client identity without leaking impersonation after awaits.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {error.Message}");
            return 1;
        }
    }
}

internal static class PeerTests
{
    private static class Text
    {
        public const string Pipe = "PwshMcp.Peer.Tests.";
        public const string InitialToken = "The test must start without an impersonation token.";
        public const string LeakedToken = "Peer.Read leaked the client impersonation token into the caller.";
        public const string PeerMismatch = "Peer identity did not match the connected client.";
        public const string PrincipalMismatch = "Elevated status did not match the current principal.";
        public const string LegacyNoLeak = "The --legacy reproduction did not leak the client impersonation token.";
    }

    public static async Task Run(bool legacy)
    {
        EnsureWindows();
        Check(WindowsIdentity.GetCurrent(true) is null, Text.InitialToken);

        using var current = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(current);
        var expected = new Peer(
            current.User?.Value ?? throw new UnauthorizedAccessException(),
            current.Name,
            System.Diagnostics.Process.GetCurrentProcess().SessionId,
            string.Empty,
            principal.IsInRole(WindowsBuiltInRole.Administrator),
            Environment.ProcessId);

        var name = Text.Pipe + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await wait.WaitAsync(timeout.Token);
        var buffer = new byte[1];
        var read = server.ReadAsync(buffer, timeout.Token).AsTask();
        await client.WriteAsync(new byte[] { 1 }, timeout.Token);
        await read.WaitAsync(timeout.Token);
        Check(buffer[0] == 1, "The client handshake byte was not received.");

        var peer = legacy ? Legacy.Read(server) : Peer.Read(server);
        Check(peer.Sid == expected.Sid && peer.User == expected.User && peer.Session == expected.Session && peer.Pid == expected.Pid,
            Text.PeerMismatch);
        Check(peer.Elevated == expected.Elevated, Text.PrincipalMismatch);

        var leaked = false;
        for (var i = 0; i < 12; i++)
        {
            await Task.Yield();
            await Task.Delay(10);
            using var token = WindowsIdentity.GetCurrent(true);
            leaked |= token is not null;
        }
        Check(legacy ? leaked : !leaked, legacy ? Text.LegacyNoLeak : Text.LeakedToken);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This regression test requires Windows named pipe impersonation.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class Legacy
{
    public static Peer Read(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid))
            throw new UnauthorizedAccessException();
        var session = System.Diagnostics.Process.GetProcessById((int)pid).SessionId;
        Peer? peer = null;
        pipe.RunAsClient(() =>
        {
            using var id = WindowsIdentity.GetCurrent(true) ?? throw new UnauthorizedAccessException();
            var principal = new WindowsPrincipal(id);
            peer = new Peer(id.User!.Value, id.Name, session, string.Empty,
                principal.IsInRole(WindowsBuiltInRole.Administrator), (int)pid);
        });
        return peer ?? throw new UnauthorizedAccessException();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);
}
