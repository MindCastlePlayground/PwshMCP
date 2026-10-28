using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

public enum RepairState { Started, Ready, Failed, Cancelled, NeedsElevation, NotOwned }
public sealed record RepairResult(RepairState State, string Detail = "");

/// <summary>Explicit user actions only. Starting the registered task does not approve any grant.</summary>
public static class Recovery
{
    const string Scheduler = "schtasks.exe", Run = "/Run", TaskName = "/TN", RunAs = "runas";
    const int Cancelled = 1223;

    public static async Task<RepairResult> ExecutorAsync(bool elevate = false)
    {
        if (await Ready()) return new(RepairState.Ready);
        var launcher = Path.Combine(AdminNames.Directory, AdminNames.Launcher);
        if (!File.Exists(launcher)) return new(RepairState.Failed, AdminNames.Protected);
        try
        {
            if (elevate)
            {
                // Only the explicitly labelled UI action reaches this branch; no automatic UAC fallback.
                using var p = Process.Start(new ProcessStartInfo(launcher)
                { UseShellExecute = true, Verb = RunAs, WorkingDirectory = AdminNames.Directory, WindowStyle = ProcessWindowStyle.Hidden });
                if (p is null) return new(RepairState.Failed, AdminNames.Offline);
            }
            else
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), Scheduler))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                start.ArgumentList.Add(Run); start.ArgumentList.Add(TaskName);
                start.ArgumentList.Add(AdminNames.Prefix + WindowsIdentity.GetCurrent().User!.Value);
                using var p = Process.Start(start);
                if (p is null) return new(RepairState.NeedsElevation, AdminNames.Offline);
                var output = p.StandardOutput.ReadToEndAsync();
                var error = p.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await p.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { try { p.Kill(); } catch (InvalidOperationException) { } return new(RepairState.Failed, AdminNames.Offline); }
                await output;
                var detail = (await error).Trim();
                if (p.ExitCode != 0) return new(RepairState.NeedsElevation, detail);
            }
            var until = DateTimeOffset.UtcNow.AddSeconds(8);
            do
            {
                if (await Ready()) return new(RepairState.Ready);
                await Task.Delay(300);
            } while (DateTimeOffset.UtcNow < until);
            return new(RepairState.Failed, AdminNames.Offline);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == Cancelled) { return new(RepairState.Cancelled); }
        catch (Exception e) { return new(RepairState.Failed, e.GetType().Name + ": " + e.Message); }
    }

    static async Task<bool> Ready()
    {
        try { var r = await new AdminClient().SendAsync(new() { Op = AdminOp.Status }); return r.Ok && r.Ready; }
        catch { return false; }
    }
}
