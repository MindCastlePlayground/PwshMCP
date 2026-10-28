using PwshMcp.Shared;

namespace PwshMcp.Host;

/// <summary>Lifecycle diagnostics only: never log requests, commands, credentials or output.</summary>
internal static class RuntimeLog
{
    internal const string Started = "started", Stopped = "stopped", Fatal = "fatal", Client = "client_disconnected", Session = "session_ended";
    const string Folder = "logs", FileName = "runtime.log", Previous = "runtime.previous.log";
    const int Limit = 524288;
    static readonly object Gate = new();
    public static void Write(string mode, string stage, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AdminNames.Folder, Folder);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, FileName);
                if (System.IO.File.Exists(path) && new FileInfo(path).Length > Limit)
                    System.IO.File.Move(path, Path.Combine(dir, Previous), true);
                System.IO.File.AppendAllText(path, $"{DateTimeOffset.UtcNow:o} pid={Environment.ProcessId} {mode} {stage} {error?.GetType().Name} {error?.HResult:X8}{Environment.NewLine}");
            }
        }
        catch { /* A diagnostic write must never stop the runtime. */ }
    }
}

internal static class PipeFault
{
    const int Busy = 231;
    public static bool IsBusy(IOException error) => (error.HResult & 0xffff) == Busy;
}
