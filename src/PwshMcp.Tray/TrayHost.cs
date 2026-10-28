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
    readonly Action quit;
    (Glyph, bool, bool) current = (Glyph.App, false, false);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);

    public TrayHost(MainVm vm, Action<Tab?> show, Action quit)
    {
        this.vm = vm; this.show = show; this.quit = quit;
        status = new(Copy.Conn.Connecting) { Enabled = false };
        open = new(Copy.Tray.Open, null, (_, _) => show(null)) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) };
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
        menu.Items.AddRange([status, new Forms.ToolStripSeparator(), open, review, repair, new Forms.ToolStripSeparator(), revoke, lang, new Forms.ToolStripSeparator(), exit]);
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
        var s = vm.Status;
        var glyph = s.Conn != Conn.Online ? Glyph.Offline : vm.ActiveCount > 0 ? Glyph.Active : Glyph.Idle;
        var pending = vm.PendingCount > 0;
        var key = (glyph, Theme.BarLight, pending);
        if (force || key != current)
        {
            if (force) { foreach (var i in cache.Values) i.Dispose(); cache.Clear(); }
            if (!cache.TryGetValue(key, out var ic)) cache[key] = ic = Glyphs.TrayIcon(glyph, key.Item2, pending);
            icon.Icon = ic; current = key;
        }
        icon.Text = Clip(Tip(glyph, pending));
        status.Text = s.Conn == Conn.Online ? vm.ConnDetail : $"{vm.ConnTitle} · {s.Detail}";
        review.Text = string.Format(Copy.Tray.Review, vm.PendingCount);
        review.Visible = pending;
        revoke.Enabled = s.Conn == Conn.Online && vm.ActiveCount > 0;
        zh.Checked = L.Current == Lang.Zh; en.Checked = L.Current == Lang.En;
    }

    /// <summary>Re-reads the fixed menu labels after a language switch. The icon, broker and grants are left alone.</summary>
    public void Relabel()
    {
        open.Text = Copy.Tray.Open; repair.Text = Copy.Fix.Menu; revoke.Text = Copy.Tray.RevokeAll; exit.Text = Copy.Tray.Exit; lang.Text = Copy.Tray.Language;
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
        // Result of a user action, shown as a quiet notification; it does not take focus.
        icon.ShowBalloonTip(4000, Copy.Product, text, error is null ? Forms.ToolTipIcon.None : Forms.ToolTipIcon.Warning);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose(); menu.Dispose(); sink.Dispose();
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
