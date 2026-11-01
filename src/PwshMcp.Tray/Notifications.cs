using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PwshMcp.Tray;

/// <summary>Presentation preferences only. Never grants access.</summary>
public sealed class NoticePrefs
{
    public bool Manual { get; set; }
    public bool Auto { get; set; } = true;
    /// <summary>Chime on a new request. Off unless the user turns it on.</summary>
    public bool Sound { get; set; }

    public static NoticePrefs Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Names.PrefsKey);
            return new() { Manual = key?.GetValue(Names.QuietValue) is int m && m != 0,
                Auto = key?.GetValue(Names.AutoQuietValue) is not int a || a != 0,
                Sound = key?.GetValue(Names.SoundValue) is int c && c != 0 };
        }
        catch { return new(); }
    }

    public void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey(Names.PrefsKey);
        key.SetValue(Names.QuietValue, Manual ? 1 : 0);
        key.SetValue(Names.AutoQuietValue, Auto ? 1 : 0);
        key.SetValue(Names.SoundValue, Sound ? 1 : 0);
    }
}

/// <summary>Deduplicates arrivals; quiet time defers them without losing pending requests.</summary>
public sealed class NoticeQueue
{
    readonly HashSet<string> shown = [];
    public string? Next(IEnumerable<string> pending, bool muted)
    {
        var ids = pending.ToArray();
        shown.IntersectWith(ids);
        if (muted) return null;
        return ids.FirstOrDefault(id => !shown.Contains(id));
    }
    public void Seen(string id) => shown.Add(id);
    public void Defer(string id) => shown.Remove(id);
    public void Dismiss(IEnumerable<string> ids) => shown.UnionWith(ids);
}

/// <summary>Why arrivals are held back. Full screen is never a preference: it always waits, and says nothing about it.</summary>
public enum Hush { None, Manual, Auto, Full }

public static class Hushes
{
    public static Hush Of(bool manual, bool auto, bool full, bool quiet) =>
        full ? Hush.Full : manual ? Hush.Manual : auto && quiet ? Hush.Auto : Hush.None;
}

/// <summary>Read-only Windows notification state. Full screen (including borderless apps) is reported apart from other quiet states.</summary>
public static class QuietProbe
{
    enum UserState { NotPresent = 1, Busy, FullScreen, Presentation, Accepts, QuietTime, App }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    const uint Nearest = 2;
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out UserState state);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern nint GetShellWindow();
    [DllImport("user32.dll")] static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    public static (bool Full, bool Quiet) Read()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return (Covered(), false);
        if (state is UserState.Busy or UserState.FullScreen) return (true, false);
        return (Covered(), state is UserState.NotPresent or UserState.Presentation or UserState.QuietTime);
    }

    /// <summary>The foreground window covers its whole monitor (borderless full screen).</summary>
    static bool Covered()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetWindowRect(hwnd, out var rect) && GetMonitorInfo(MonitorFromWindow(hwnd, Nearest), ref info)
            && rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top
            && rect.Right >= info.Monitor.Right && rect.Bottom >= info.Monitor.Bottom;
    }
}

/// <summary>A fixed batch supplied by a user click, never an ongoing auto-approve rule.</summary>
public static class QuickApproval
{
    public static async Task<(int Ok, int Failed)> Run(MainVm main, IReadOnlyList<string> ids, Life life)
    {
        var ok = 0;
        var batch = ids.Distinct().ToArray();
        foreach (var id in batch)
        {
            var grant = main.Requests.FirstOrDefault(g => g.Id == id);
            if (grant is null || !grant.CanApprove) continue;
            grant.Pick(life);
            await grant.Submit();
            if (grant.Accepted) ok++;
        }
        return (ok, batch.Length - ok);
    }
}
