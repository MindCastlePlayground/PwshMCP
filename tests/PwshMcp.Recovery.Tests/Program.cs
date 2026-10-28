using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PwshMcp.Shared;

internal static class Program
{
    private static class Text
    {
        public const string Pipe = "PwshMcp.Recovery.Tests.";
        public const string Windows = "This regression test requires Windows named pipes.";
        public const string Serve = "AdminHost.Serve was not found.";
        public const string Busy = "An occupied single-instance pipe did not report a busy error.";
        public const string Fault = "The busy pipe error was not classified as recoverable.";
        public const string Connection = "The isolated client process failed to send its request.";
    }

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(Text.Windows);
            if (args.Length == 2 && args[0] == "--send-status") return await SendStatus(args[1]);
            await BusyPipeIsRecoverable();
            await ClosedStreamIsContained();
            await ExitedPeerDoesNotBreakServe();
            Console.WriteLine("PASS: an exited status client does not fault its serving call; the next status client is served. Occupied-pipe errors are classified as recoverable.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {error.Message}");
            return 1;
        }
    }

    private static async Task ClosedStreamIsContained()
    {
        var type = HostType();
        var host = Activator.CreateInstance(type, nonPublic: true)!;
        var serve = type.GetMethod("Serve", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var exchange = type.GetMethod("Exchange", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pipe = new NamedPipeServerStream(Text.Pipe + Guid.NewGuid().ToString("N"));
        pipe.Dispose();
        var failed = false;
        try { await (Task)exchange.Invoke(host, [pipe, CancellationToken.None])!; }
        catch (Exception) { failed = true; }
        Check(failed, "Closed-stream fixture must fault the unguarded exchange.");
        await (Task)serve.Invoke(host, [pipe, CancellationToken.None])!;
    }

    private static async Task BusyPipeIsRecoverable()
    {
        var name = Text.Pipe + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var wait = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await wait.WaitAsync(timeout.Token);

        IOException? busy = null;
        try { using var next = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous); }
        catch (IOException error) { busy = error; }

        Check(busy is not null, Text.Busy);
        var hostType = HostType();
        var classifier = hostType.Assembly.GetType("PwshMcp.Host.PipeFault")?.GetMethod("IsBusy", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Check(classifier is not null, Text.Fault);
        Check((bool)classifier!.Invoke(null, [busy])!, Text.Fault);
    }

    private static async Task ExitedPeerDoesNotBreakServe()
    {
        var hostType = HostType();
        var serve = hostType.GetMethod("Serve", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(serve is not null, Text.Serve);
        var host = Activator.CreateInstance(hostType, nonPublic: true)!;
        var name = Text.Pipe + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var accepted = server.WaitForConnectionAsync(timeout.Token);
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["--send-status", name]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }) ?? throw new InvalidOperationException(Text.Connection);
        await accepted.WaitAsync(timeout.Token);
        var failedClient = serve!.Invoke(host, [server, timeout.Token]);
        Check(failedClient is Task, Text.Connection);
        await child.WaitForExitAsync(timeout.Token);
        var childError = await child.StandardError.ReadToEndAsync();
        Check(child.ExitCode == 0, $"{Text.Connection} Exit code: {child.ExitCode}; {childError}");
        await ((Task)failedClient!).WaitAsync(timeout.Token);

        var nextName = Text.Pipe + Guid.NewGuid().ToString("N");
        using var nextServer = new NamedPipeServerStream(nextName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var nextClient = new NamedPipeClientStream(".", nextName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var nextAccepted = nextServer.WaitForConnectionAsync(timeout.Token);
        await nextClient.ConnectAsync(timeout.Token);
        await nextAccepted.WaitAsync(timeout.Token);
        var nextServe = (Task)serve.Invoke(host, [nextServer, timeout.Token])!;
        using var writer = new StreamWriter(nextClient, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(nextClient, Encoding.UTF8, leaveOpen: true);
        var request = JsonSerializer.Serialize(new AdminRequest { Op = AdminOp.Status }, Protocol.Json);
        await writer.WriteLineAsync(request);
        var reply = await reader.ReadLineAsync(timeout.Token);
        await nextServe.WaitAsync(timeout.Token);
        var response = JsonSerializer.Deserialize<AdminResponse>(reply!, Protocol.Json);
        Check(response?.Ready == true, "AdminHost did not serve a valid status request after the exited client.");
    }

    private static async Task<int> SendStatus(string name)
    {
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.ConnectAsync(timeout.Token);
        var request = JsonSerializer.Serialize(new AdminRequest { Op = AdminOp.Status }, Protocol.Json);
        var bytes = Encoding.UTF8.GetBytes(new string(' ', AdminNames.MaxMessage - 64) + request + "\n");
        await client.WriteAsync(bytes, timeout.Token);
        return 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static Type HostType() => Assembly.Load("PwshMcp.Host").GetType("PwshMcp.Host.AdminHost") ?? throw new InvalidOperationException(Text.Serve);
}
