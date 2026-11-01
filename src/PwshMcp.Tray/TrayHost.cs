using System.Runtime.InteropServices;
using System.Windows.Interop;
using PwshMcp.Shared;
using Forms = System.Windows.Forms;

namespace PwshMcp.Tray;

/// <summary>The notification-area icon and its menu. Never takes focus on its own.</summary>
public sealed class TrayHost : IDisposable
{
    const int WmSettingChange = 0x001A, WmDpiChanged = 0x02E0, WmDisplayChange = 0x007E;
    readonly Forms.NotifyIcon icon = new();
    readonly Forms.ContextMenuStrip menu = new();
    readonly Forms.ToolStripMenuItem status, open, review, repair, revoke, exit, lang, zh, en;
    readonly Dictionary<(Glyph, bool, bool), System.Drawing.Icon> cache = [];
    readonly HwndSource sink;
    readonly uint taskbarCreated;
    readonly MainVm vm;
    readonly Action<Tab?> show;
    readonly Action quit, warm;
    readonly NoticePrefs prefs = NoticePrefs.Load();
    readonly NoticeQueue notices = new();
    readonly Forms.ToolStripMenuItem approve = new(), approveAll = new(), duration = new(), quiet = new(), manual = new(), automatic = new();
    readonly Forms.ToolStripMenuItem sound = new(), soundOn = new(), soundTry = new();
    readonly System.Drawing.Font bold;
    NoticeWindow? popup;
    string? popupId;
    Hush hush;
    bool approving, disposed;
    bool Muted => hush != Hush.None;
    Life quickLife = Life.SignOut;
    (Glyph, bool, bool) current = (Glyph.App, false, false);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);

    /// <summary>A notice is open or a quick approval is running; the main window must not be torn down meanwhile.</summary>
    public bool Busy => approving || popup is not null;

    public TrayHost(MainVm vm, Action<Tab?> show, Action quit, Action warm)
    {
        this.vm = vm; this.show = show; this.quit = quit; this.warm = warm;
        status = new(Copy.Conn.Connecting) { Enabled = false };
        bold = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
        open = new(Copy.Tray.Open, null, (_, _) => show(null)) { Font = bold };
        review = new(string.Format(Copy.Tray.Review, 0), null, (_, _) => show(Tab.Requests)) { Visible = false };
        // Opens the repair window only; nothing is started until the user clicks inside it.
        repair = new(Copy.Fix.Menu, null, (_, _) => { show(null); vm.Fix.Ask(vm.Fix.Worst); });
        revoke = new(Copy.Tray.RevokeAll, null, async (_, _) => await RevokeAll()) { Enabled = false };
        exit = new(Copy.Tray.Exit, null, (_, _) => quit());
        // Each language is named in itself so it can be found from the other one.
        zh = new(Copy.Lang.Zh, null, (_, _) => MainVm.Pick(Lang.Zh));
        en = new(Copy.Lang.En, null, (_, _) => MainVm.Pick(Lang.En));
        lang = new(Copy.Tray.Language);
        lang.DropDownItems.AddRange([zh, en]);
        manual.CheckOnClick = true; automatic.CheckOnClick = true;
        manual.Click += (_, _) => SetQuiet(manual.Checked, prefs.Auto);
        automatic.Click += (_, _) => SetQuiet(prefs.Manual, automatic.Checked);
        quiet.DropDownItems.AddRange([manual, automatic]);
        soundOn.CheckOnClick = true;
        soundOn.Click += (_, _) => { prefs.Sound = soundOn.Checked; Save(); };
        // Plays only on this click. It changes no setting and no access.
        soundTry.Click += (_, _) => Ring(true);
        sound.DropDownItems.AddRange([soundOn, soundTry]);
        menu.Opening += (_, _) => Menu();
        approveAll.Click += async (_, _) => { if (approveAll.Tag is string[] ids) await Approve(ids, quickLife); };
        menu.Items.AddRange([status, new Forms.ToolStripSeparator(), open, review, approve, approveAll, duration, quiet, sound, repair, new Forms.ToolStripSeparator(), revoke, lang, new Forms.ToolStripSeparator(), exit]);
        icon.ContextMenuStrip = menu;
        icon.Text = Copy.Tray.Offline;
        icon.DoubleClick += (_, _) => show(null);
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) show(vm.PendingCount > 0 ? Tab.Requests : null); };

        // Hidden top-level window: receives TaskbarCreated (Explorer restart) and theme broadcasts. Never shown.
        taskbarCreated = RegisterWindowMessage(Names.TaskbarCreated);
        sink = new HwndSource(new HwndSourceParameters(Copy.Product) { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) });
        sink.AddHook(Hook);

        Render(force: true);
        icon.Visible = true;
    }

    nint Hook(nint hwnd, int msg, nint w, nint l, ref bool handled)
    {
        if (taskbarCreated != 0 && msg == taskbarCreated)
        {
            icon.Visible = false; Render(force: true); icon.Visible = true;
        }
        else if (msg is WmSettingChange or WmDpiChanged or WmDisplayChange)
        {
            Theme.Apply(System.Windows.Application.Current.Resources);
            Render(force: true);
        }
        return 0;
    }

    public void Render(bool force = false)
    {
        if (disposed) return;
        var (full, quiet) = QuietProbe.Read();
        hush = Hushes.Of(prefs.Manual, prefs.Auto, full, quiet);
        var s = vm.Status;
        var glyph = s.Conn != Conn.Online ? Glyph.Offline : vm.ActiveCount > 0 ? Glyph.Active : Glyph.Idle;
        var pending = vm.PendingCount > 0;
        var key = (glyph, Theme.BarLight, pending);
        if (force || key != current)
        {
            // Old icons are disposed only after the new one is in place.
            var stale = force ? cache.Values.ToList() : [];
            if (force) cache.Clear();
            if (!cache.TryGetValue(key, out var ic)) cache[key] = ic = Glyphs.TrayIcon(glyph, key.Item2, pending);
            icon.Icon = ic; current = key;
            foreach (var i in stale) i.Dispose();
        }
        // Full screen holds notices back silently; only a chosen or Windows quiet mode is named.
        icon.Text = Clip(Tip(glyph, pending) + (hush is Hush.Manual or Hush.Auto ? " · " + Copy.Notify.Quiet : ""));
        status.Text = s.Conn == Conn.Online ? vm.ConnDetail : $"{vm.ConnTitle} · {s.Detail}";
        review.Text = string.Format(Copy.Tray.Review, vm.PendingCount);
        review.Visible = pending;
        revoke.Enabled = s.Conn == Conn.Online && vm.ActiveCount > 0;
        zh.Checked = L.Current == Lang.Zh; en.Checked = L.Current == Lang.En;
        Notify();
    }

    string[] Pending() => vm.Requests.Where(g => g.IsPending && !g.Accepted).Select(g => g.Id).ToArray();

    void SetQuiet(bool manual, bool auto)
    {
        prefs.Manual = manual; prefs.Auto = auto;
        Save();
    }

    void Save()
    {
        try { prefs.Save(); }
        catch (Exception e) { vm.Fault(new InvalidOperationException(string.Format(Copy.Notify.SavedFailed, e.Message))); }
        Render();
    }

    // Freeze both individual entries and the bulk target list while the context menu is open.
    void Menu()
    {
        var ids = Pending();
        Clear(approve); Clear(duration);
        approve.Text = Copy.Notify.Select;
        approve.Enabled = vm.Ready && ids.Length > 0 && !approving;
        foreach (var id in ids)
        {
            var g = vm.Requests.First(r => r.Id == id);
            var item = new Forms.ToolStripMenuItem(Clip($"{g.TaskLine} · {g.Client}").Replace("&", "&&"));
            item.ToolTipText = $"{g.TaskName}\n{g.Client}\n{g.Key}\n{g.User} · {g.Session}";
            item.Click += async (_, _) => await Approve([id], quickLife);
            approve.DropDownItems.Add(item);
        }
        approveAll.Text = string.Format(Copy.Notify.All, ids.Length);
        approveAll.Enabled = approve.Enabled;
        approveAll.Tag = ids;
        // Handler is registered once, not on every opening.
        duration.Text = $"{Copy.Notify.Duration} · {NoticeWindow.Label(quickLife)}";
        foreach (var life in Lives.All)
        {
            var item = new Forms.ToolStripMenuItem(NoticeWindow.Label(life)) { Checked = life == quickLife };
            item.Click += (_, _) => quickLife = life;
            duration.DropDownItems.Add(item);
        }
        quiet.Text = hush switch { Hush.Manual => Copy.Notify.Muted, Hush.Auto => Copy.Notify.AutoMuted, _ => Copy.Notify.Quiet };
        manual.Text = Copy.Notify.Manual; manual.Checked = prefs.Manual;
        automatic.Text = Copy.Notify.Auto; automatic.Checked = prefs.Auto;
        sound.Text = Copy.Notify.Sound;
        soundOn.Text = Copy.Notify.SoundOn; soundOn.Checked = prefs.Sound;
        soundTry.Text = Copy.Notify.SoundTry;
    }

    static void Clear(Forms.ToolStripMenuItem item)
    {
        while (item.DropDownItems.Count > 0) { var child = item.DropDownItems[0]; item.DropDownItems.RemoveAt(0); child.Dispose(); }
    }

    async Task Approve(string[] ids, Life life)
    {
        if (approving || !vm.Ready) return;
        approving = true;
        try
        {
            var result = await QuickApproval.Run(vm, ids, life);
            Balloon(string.Format(Copy.Notify.Result, result.Ok, result.Failed), result.Failed > 0);
        }
        finally { approving = false; Render(); }
    }

    void Notify()
    {
        var ids = Pending();
        var next = notices.Next(ids, Muted || !vm.Online);
        // Entering full screen or quiet time takes an open notice down; its request comes back when it ends.
        if (popup is not null && (Muted || !vm.Online || !ids.Contains(popupId)))
        {
            if (popupId is not null && ids.Contains(popupId)) notices.Defer(popupId);
            ClosePopup();
            return;
        }
        if (popup is not null) { popup.Refresh(ids.Length); return; }
        if (next is null) return;
        var grant = vm.Requests.First(g => g.Id == next);
        grant.Pick(quickLife);
        notices.Seen(next);
        popupId = next;
        // Only a newly presented notice builds windows: a dismissed or already shown id never reaches here again.
        warm();
        var window = new NoticeWindow(grant, ids.Length,
            () => { vm.SelReq = grant; show(Tab.Requests); },
            () => notices.Dismiss(Pending()),
            () => SetQuiet(true, prefs.Auto));
        popup = window;
        window.Closed += (_, _) => { if (popup == window) { popup = null; popupId = null; } };
        window.Warm();
        window.Show();
        if (prefs.Sound) Ring(false);
    }

    /// <summary>A failed sample says so in a balloon; a failed request chime lands in the window's error line instead of interrupting again.</summary>
    async void Ring(bool sample)
    {
        try { await Chime.Play(); }
        catch (Exception e)
        {
            var text = string.Format(Copy.Notify.SoundFailed, e.Message);
            if (sample) Balloon(text, true); else vm.Fault(new InvalidOperationException(text));
        }
    }

    void ClosePopup()
    {
        var old = popup; popup = null; popupId = null;
        old?.Retire();
    }

    /// <summary>Re-reads the fixed menu labels after a language switch. The icon, broker and grants are left alone.</summary>
    public void Relabel()
    {
        if (popupId is not null) notices.Defer(popupId);
        ClosePopup();
        open.Text = Copy.Tray.Open; sound.Text = Copy.Notify.Sound; repair.Text = Copy.Fix.Menu; revoke.Text = Copy.Tray.RevokeAll; exit.Text = Copy.Tray.Exit; lang.Text = Copy.Tray.Language;
        Render();
    }

    string Tip(Glyph g, bool pending)
    {
        if (pending) return vm.PendingCount == 1 ? Copy.Tray.PendingOne : string.Format(Copy.Tray.PendingMany, vm.PendingCount);
        return g switch
        {
            Glyph.Offline => Copy.Tray.Offline,
            Glyph.Active => vm.ActiveCount == 1 ? Copy.Tray.ActiveOne : string.Format(Copy.Tray.ActiveMany, vm.ActiveCount),
            _ => Copy.Tray.Idle
        };
    }

    // NotifyIcon.Text is limited to 127 characters.
    static string Clip(string s) => s.Length <= 127 ? s : s[..126] + "…";

    async Task RevokeAll()
    {
        var (ok, error) = await vm.RevokeActive();
        var text = error is null ? string.Format(Copy.Tray.RevokedAll, ok) : $"{Copy.Tray.RevokeFailed}: {error}";
        Balloon(text, error is not null);
    }

    /// <summary>Result of a user action, shown as a quiet notification that takes no focus. Never shown over a full-screen app.</summary>
    void Balloon(string text, bool warn)
    {
        if (disposed || QuietProbe.Read().Full) return;
        icon.ShowBalloonTip(Timing.Balloon, Copy.Product, text, warn ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        disposed = true;
        ClosePopup();
        icon.Visible = false;
        icon.Dispose(); menu.Dispose(); sink.Dispose(); bold.Dispose();
        foreach (var i in cache.Values) i.Dispose();
    }
}

/// <summary>Exit confirmation as a native task dialog.</summary>
public static class ExitPrompt
{
    public static bool Confirm(bool owned, int live)
    {
        var stop = new Forms.TaskDialogButton(owned ? Copy.Exit.Stop : Copy.Exit.Close);
        var keep = new Forms.TaskDialogButton(Copy.Exit.Keep);
        var body = owned ? Copy.Exit.OwnedBody + (live > 0 ? "\n\n" + string.Format(Copy.Exit.OwnedTasks, live) : "") : Copy.Exit.ServiceBody;
        var page = new Forms.TaskDialogPage
        {
            Caption = Copy.Exit.Title, Heading = owned ? Copy.Exit.OwnedHead : Copy.Exit.ServiceHead, Text = body,
            Icon = owned && live > 0 ? Forms.TaskDialogIcon.Warning : Forms.TaskDialogIcon.Information,
            Buttons = { keep, stop }, DefaultButton = keep, AllowCancel = true
        };
        return Forms.TaskDialog.ShowDialog(page, Forms.TaskDialogStartupLocation.CenterScreen) == stop;
    }
}
