using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PwshMcp.Tray;

public partial class MainWindow : Window
{
    readonly MainVm vm;

    public MainWindow(MainVm vm)
    {
        this.vm = vm;
        Language = L.Xml;
        InitializeComponent();
        DataContext = vm;
        Group(vm.Access);
        vm.PropertyChanged += OnVm;
        vm.Fix.Asked += Repair;
        Pop = v => life.IsOpen = v;
        ShowFix = w => w.Show();
        IsVisibleChanged += (_, _) => { vm.Viewing = IsVisible && WindowState != WindowState.Minimized; if (!IsVisible) Shut(false); };
        StateChanged += (_, _) => { vm.Viewing = IsVisible && WindowState != WindowState.Minimized; Shut(false); };
        // A popup left floating over a moved, resized or backgrounded window would point at the wrong place.
        Deactivated += (_, _) => Shut(false);
        LocationChanged += (_, _) => Shut(false);
        SizeChanged += (_, _) => Shut(false);
        InputBindings.Add(new KeyBinding(new Relay(() => vm.Tab = Tab.Requests), Key.D1, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => vm.Tab = Tab.Access), Key.D2, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => vm.Tab = Tab.Tasks), Key.D3, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(Hide), Key.Escape, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.Retry, Key.F5, ModifierKeys.None));
    }

    /// <summary>
    /// Access splits into live (false) then ended (true), in that fixed order; source order is kept inside each group.
    /// Live grouping moves a grant under the Ended heading the moment it expires or is revoked.
    /// </summary>
    static void Group(System.Collections.IEnumerable src)
    {
        if (CollectionViewSource.GetDefaultView(src) is not ListCollectionView v || v.GroupDescriptions.Count > 0) return;
        var by = new PropertyGroupDescription(nameof(GrantVm.IsEnded));
        by.GroupNames.Add(false); by.GroupNames.Add(true);
        v.GroupDescriptions.Add(by);
        v.LiveGroupingProperties.Add(nameof(GrantVm.IsEnded));
        v.IsLiveGrouping = true;
    }

    object? shown;

    void OnVm(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainVm.Selected) or "" or null)) return;
        if (bound is not null && !ReferenceEquals(bound, vm.Selected)) Shut(false);
        if (ReferenceEquals(shown, vm.Selected)) return;
        shown = vm.Selected;
        if (!Theme.Motion || shown is null) return;
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        var d = TimeSpan.FromMilliseconds(180);
        detail.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, d) { EasingFunction = ease });
        rise.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(4, 0, d) { EasingFunction = ease });
    }

    /// <summary>Brings the window forward. Only called for explicit user intent (menu, double-click, --show, second launch).</summary>
    public void Present(Tab? tab = null)
    {
        if (tab is { } t) vm.Tab = t;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        FocusList();
        if (vm.Fix.Reopen) { vm.Fix.Reopen = false; Repair(vm.Fix.Focus); }
    }

    // ---- repair window: one per main window, opened only by a click (header chip, Fix executor, tray menu) ----------

    RecoveryWindow? fix;
    /// <summary>Show seam. The offline check swaps it so the repair window is built and laid out but never shown.</summary>
    internal Action<Window> ShowFix;
    internal RecoveryWindow? Fix => fix;

    void OnFixConn(object sender, RoutedEventArgs e) => Repair(Part.Conn);
    void OnFixExec(object sender, RoutedEventArgs e) => Repair(Part.Exec);

    /// <summary>Opens the repair window pointed at <paramref name="p"/>, or re-aims the open one. Opening it repairs nothing by itself.</summary>
    internal void Repair(Part p)
    {
        vm.Fix.Focus = p;
        if (fix is null)
        {
            fix = new RecoveryWindow(vm.Fix);
            // Owned only once this window has a handle; otherwise it centres on screen (tray menu before first show).
            if (IsVisible) fix.Owner = this; else fix.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var mine = fix;
            fix.Closed += (_, _) => { if (ReferenceEquals(fix, mine)) fix = null; };
            ShowFix(fix);
        }
        else if (fix.IsVisible) fix.Activate();
        fix.Aim(p);
    }

    void FocusList()
    {
        var list = vm.Tab switch { Tab.Requests => reqList, Tab.Access => accList, _ => taskList };
        Dispatcher.BeginInvoke(() =>
        {
            if (list.SelectedItem is { } item && list.ItemContainerGenerator.ContainerFromItem(item) is UIElement c) c.Focus();
            else list.Focus();
        });
    }

    // ---- lifetime popup: opened from the footer link, bound to one pending request while open ----------------------

    GrantVm? bound;
    Button? anchor, last;
    long shutAt;

    /// <summary>Popup visibility seam. The offline check swaps it so no popup window is ever created.</summary>
    internal Action<bool> Pop;
    internal GrantVm? Bound => bound;

    void OnPicked(object sender, RoutedEventArgs e)
    {
        // The outside click that just closed the popup also lands on its own anchor; that click must not reopen it.
        if (ReferenceEquals(sender, last) && Environment.TickCount64 - shutAt < 300) return;
        if (sender is Button b) Open(b);
    }

    /// <summary>Opens the popup for the request behind <paramref name="b"/>; refused unless that request is selected, pending and editable.</summary>
    internal bool Open(Button b)
    {
        if (b.DataContext is not GrantVm g || !ReferenceEquals(g, vm.Selected) || !g.ShowPick || !g.CanPick) return false;
        Shut(false);
        g.Touch(); // previews count from now, not from when the request was selected
        bound = g; anchor = b;
        lifeCard.DataContext = g;
        g.PropertyChanged += OnBound;
        life.PlacementTarget = b;
        life.PopupAnimation = Theme.Motion ? PopupAnimation.Fade : PopupAnimation.None;
        Turn(b, 180);
        Pop(true);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (bound == g) Checked()?.Focus(); });
        return true;
    }

    /// <summary>Closes and unbinds. <paramref name="focus"/> returns focus to the anchor (Esc, a choice); outside clicks leave it where the user clicked.</summary>
    internal void Shut(bool focus)
    {
        if (bound is not { } g) return;
        g.PropertyChanged -= OnBound;
        bound = null;
        var b = anchor; anchor = null;
        Pop(false);
        lifeCard.DataContext = null;
        if (b is null) return;
        Turn(b, 0);
        if (focus && b.IsVisible) b.Focus();
    }

    internal RadioButton? Checked() => Find<RadioButton>(lifeCard, r => r.IsChecked == true);

    /// <summary>Picks a lifetime for the bound request and closes; ignored once the request can't be edited.</summary>
    internal void Choose(Life l)
    {
        if (bound is not { CanPick: true } g) return;
        g.Pick(l);
        Shut(true);
    }

    /// <summary>Esc closes, Enter picks the focused choice. Handled keys never reach the window's Esc-to-hide binding.</summary>
    internal bool Press(Key k, object? focused)
    {
        if (bound is null) return false;
        if (k == Key.Escape) { Shut(true); return true; }
        if (k == Key.Enter && focused is RadioButton { Tag: Life l }) { Choose(l); return true; }
        return false;
    }

    void OnLife(object sender, RoutedEventArgs e) { if (sender is RadioButton { Tag: Life l }) Choose(l); }
    void OnLifeKey(object sender, KeyEventArgs e) => e.Handled = Press(e.Key, Keyboard.FocusedElement);

    void OnLifeClosed(object? sender, EventArgs e)
    {
        if (bound is null) return;
        last = anchor; shutAt = Environment.TickCount64;
        Shut(false);
    }

    /// <summary>A request that stops being selected, pending or editable takes its popup down with it.</summary>
    void OnBound(object? s, PropertyChangedEventArgs e)
    {
        if (bound is { } g && (!g.ShowPick || !g.CanPick || !ReferenceEquals(g, vm.Selected))) Shut(false);
    }

    static void Turn(Button b, double angle)
    {
        if (Find<System.Windows.Shapes.Path>(b, _ => true) is { } p) p.RenderTransform = new RotateTransform(angle);
    }

    static T? Find<T>(DependencyObject d, Func<T, bool> ok) where T : DependencyObject
    {
        if (d is T t && ok(t)) return t;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (Find(VisualTreeHelper.GetChild(d, i), ok) is { } hit) return hit;
        return null;
    }

    bool retired;

    /// <summary>Adopts the placement of the window this one replaces after a language switch.</summary>
    public void Take(MainWindow old)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        var r = old.WindowState == WindowState.Normal ? new Rect(old.Left, old.Top, old.Width, old.Height) : old.RestoreBounds;
        if (!r.IsEmpty) { Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height; }
        if (old.WindowState == WindowState.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>Closes for real (replaced, not hidden). The view model lives on in the new window.</summary>
    public void Retire()
    {
        Release();
        Close();
    }

    /// <summary>Unhooks from the view model and takes the lifetime popup down; the offline check stops here, short of a real Close.</summary>
    internal void Release()
    {
        retired = true;
        Shut(false);
        vm.PropertyChanged -= OnVm;
        vm.Fix.Asked -= Repair;
        // A language switch rebuilds this window; an open repair window is rebuilt with it (an attempt in flight keeps running in the model).
        if (fix is { } f) { fix = null; vm.Fix.Reopen = f.IsVisible; if (f.IsVisible) f.Close(); }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window keeps the tray running; Exit lives in the tray menu.
        if (!App.Quitting && !retired) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
