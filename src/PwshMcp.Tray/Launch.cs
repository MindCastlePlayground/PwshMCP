using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

public enum Mode { Tray, Approve, Resume, Export, SelfTest, Render }

public sealed record Args(Mode Mode, bool Show, string? Id, string? BackendDir, string? Dir, Lang? Lang)
{
    public static Args Parse(string[] a)
    {
        string? Next(int i) => i + 1 < a.Length ? a[i + 1] : null;
        // Optional value: the next token unless it is another flag.
        string? Opt(ref int i) => Next(i) is { } v && !v.StartsWith(Cli.Flag) ? a[++i] : null;
        Mode mode = Mode.Tray; bool show = false; string? id = null, backend = null, dir = null; Lang? lang = null;
        for (int i = 0; i < a.Length; i++)
        {
            switch (a[i].ToLowerInvariant())
            {
                case Cli.Show: show = true; break;
                case Cli.Approve: mode = Mode.Approve; id = Next(i++); break;
                case Cli.Resume: mode = Mode.Resume; id = Next(i++); break;
                case Cli.BackendDir: backend = Next(i++); break;
                case Cli.ExportIcons: mode = Mode.Export; dir = Next(i++); break;
                case Cli.Language: lang = L.Parse(Next(i++)); break;
                case Cli.SelfTest: mode = Mode.SelfTest; dir = Opt(ref i); break;
                case Cli.RenderCheck: mode = Mode.Render; dir = Opt(ref i); break;
            }
        }
        return new(mode, show, id, backend, dir, lang);
    }
}

public enum Uac { Started, Cancelled, Failed }

/// <summary>Process launching: elevated self, hidden broker, hidden worker.</summary>
public static class Launch
{
    const int ErrorCancelled = 1223;

    public static string HostDir(string? backend) => string.IsNullOrWhiteSpace(backend) ? AppContext.BaseDirectory : Path.GetFullPath(backend);
    public static string HostPath(string? backend) => Path.Combine(HostDir(backend), Names.HostExe);

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Runs this exe elevated. Only call from a user click.</summary>
    public static Task<(Uac, string)> Elevated(string flag, string id, string? backend) => Task.Run(() =>
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Names.TrayExe)) { UseShellExecute = true, Verb = Cli.RunAs };
        psi.ArgumentList.Add(flag); psi.ArgumentList.Add(id);
        // The elevated window may run as another admin account whose saved language differs; keep the one the user is reading.
        psi.ArgumentList.Add(Cli.Language); psi.ArgumentList.Add(L.Tag(L.Current));
        if (!string.IsNullOrWhiteSpace(backend)) { psi.ArgumentList.Add(Cli.BackendDir); psi.ArgumentList.Add(HostDir(backend)); }
        try { using var p = Process.Start(psi); return (Uac.Started, ""); }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled) { return (Uac.Cancelled, ""); }
        catch (Exception e) { return (Uac.Failed, e.Message); }
    });

    public static Process Broker(string? backend)
    {
        var psi = new ProcessStartInfo(HostPath(backend)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = HostDir(backend) };
        psi.ArgumentList.Add(Cli.Broker);
        return Process.Start(psi) ?? throw new IOException(Copy.Err.NoResponse);
    }

    /// <summary>Starts the elevated worker detached from this window. Returns null on success or the failure text.</summary>
    public static async Task<string?> Worker(string grant, string? backend)
    {
        var path = HostPath(backend);
        if (!File.Exists(path)) return string.Format(Copy.Conn.HostMissing, HostDir(backend));
        try
        {
            var psi = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = HostDir(backend) };
            psi.ArgumentList.Add(Cli.Worker); psi.ArgumentList.Add(Cli.Grant); psi.ArgumentList.Add(grant);
            using var p = Process.Start(psi);
            if (p is null) return Copy.Err.NoResponse;
            using var cts = new CancellationTokenSource(Timing.WorkerGrace);
            try { await p.WaitForExitAsync(cts.Token); return string.Format(Copy.Approve.WorkerExited, p.ExitCode); }
            catch (OperationCanceledException) { return null; }
        }
        catch (Exception e) { return e.Message; }
    }

    /// <summary>--backend-dir for this process; set once at startup so every client verifies the same Host build (A9).</summary>
    public static string? Backend { get; set; }

    public static async Task<Response> Send(Request r, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        return await new BrokerClient { ExpectedHostPath = HostPath(Backend) }.SendAsync(r, cts.Token);
    }
}
