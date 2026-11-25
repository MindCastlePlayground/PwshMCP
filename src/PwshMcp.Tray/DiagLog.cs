using System.IO;
using System.Text;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

/// <summary>Lifecycle and error diagnostics only: never log command code, descriptions, task names, credentials or output.</summary>
public static class TrayLog
{
    static readonly object Gate = new();
    static readonly HashSet<string> Once = [];

    internal static string LogDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AdminNames.Folder, Names.LogDir);
    internal static int MaxBytes { get; set; } = Limits.TrayLogBytes;
    public static string LogPath => Path.Combine(LogDirectory, Names.TrayLog);
    public static bool Enabled { get; set; } = true;

    public static void Write(string stage, string? detail = null, Exception? error = null)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaxBytes)
                    File.Move(LogPath, Path.Combine(LogDirectory, Names.TrayLogPrev), true);
                File.AppendAllText(LogPath, Format(stage, detail, error), Encoding.UTF8);
            }
        }
        catch { /* Diagnostic logging must never stop or crash the tray. */ }
    }

    public static void WriteOnce(string key, string stage, string? detail = null, Exception? error = null)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                if (!Once.Add(key)) return;
                Write(stage, detail, error);
            }
        }
        catch { }
    }

    internal static void ResetForTest()
    {
        lock (Gate)
        {
            Once.Clear();
            MaxBytes = Limits.TrayLogBytes;
            Enabled = true;
        }
    }

    static string Format(string stage, string? detail, Exception? error)
    {
        var sb = new StringBuilder();
        sb.Append(DateTimeOffset.UtcNow.ToString("o"))
          .Append(" pid=").Append(Environment.ProcessId)
          .Append(" ver=").Append(Copy.Version)
          .Append(' ').Append(stage);
        if (!string.IsNullOrWhiteSpace(detail))
            sb.Append(' ').Append(SingleLine(detail));
        sb.AppendLine();

        string? stack = null;
        var cur = error;
        for (int depth = 0; cur is not null && depth < 5; depth++, cur = cur.InnerException)
        {
            sb.Append("  ex=").Append(cur.GetType().FullName)
              .Append(" hr=0x").Append(cur.HResult.ToString("X8"))
              .Append(" msg=").AppendLine(SingleLine(cur.Message));
            if (!string.IsNullOrWhiteSpace(cur.StackTrace)) stack = cur.StackTrace;
        }
        if (!string.IsNullOrWhiteSpace(stack))
        {
            if (stack.Length > Limits.TrayLogStackChars) stack = stack[..Limits.TrayLogStackChars] + "...";
            sb.AppendLine(stack);
        }
        return sb.ToString();
    }

    static string SingleLine(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
