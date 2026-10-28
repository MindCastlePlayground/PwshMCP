using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PwshMcp.Tray;

/// <summary>Compact, modeless repair window. Survives the Windows prompt taking focus, unlike a light-dismiss popup.</summary>
public partial class RecoveryWindow : Window
{
    readonly FixVm vm;

    public RecoveryWindow(FixVm vm)
    {
        this.vm = vm;
        Language = L.Xml;
        InitializeComponent();
        DataContext = vm;
        // SizeToContent grows downward; expanded diagnostics must not push Close past the bottom of the screen.
        SourceInitialized += (_, _) => Keep();
        SizeChanged += (_, _) => Keep();
        LocationChanged += (_, _) => Keep();
        DpiChanged += (_, _) => Keep();
    }

    /// <summary>Work area (DIPs) of the display this window is on; before it is placed, the owner's display.</summary>
    Rect? Area()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource src || src.CompositionTarget is null) return null;
        var at = !IsVisible && Owner is { } o ? new WindowInteropHelper(o).Handle : src.Handle;
        var r = System.Windows.Forms.Screen.FromHandle(at == IntPtr.Zero ? src.Handle : at).WorkingArea;
        var m = src.CompositionTarget.TransformFromDevice;
        return new Rect(m.Transform(new Point(r.Left, r.Top)), m.Transform(new Point(r.Right, r.Bottom)));
    }

    void Keep()
    {
        if (Area() is not { } a) return;
        var (max, top) = Bound(a, Top, ActualHeight);
        if (Math.Abs(MaxHeight - max) > 0.5) MaxHeight = max;
        if (IsVisible && !double.IsNaN(Top) && Math.Abs(Top - top) > 0.5) Top = top;
    }

    /// <summary>The tallest the window may be, and a top that keeps its whole height (so Close) inside the work area.</summary>
    internal static (double Max, double Top) Bound(Rect area, double top, double h) =>
        (area.Height, Math.Max(area.Top, Math.Min(top, area.Bottom - Math.Min(h, area.Height))));

    /// <summary>The part last pointed at; the offline check reads it instead of watching focus.</summary>
    internal Part Cued { get; private set; }
    internal Border Box(Part p) => p == Part.Conn ? connPart : execPart;

    /// <summary>Points at one part: focus on its first available action, and a short accent wash so the eye lands there.</summary>
    public void Aim(Part p)
    {
        Cued = p;
        var box = Box(p);
        if (Theme.Motion && TryFindResource(K.AccentSoft) is SolidColorBrush soft)
        {
            var wash = new SolidColorBrush(soft.Color);
            box.Background = wash;
            wash.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation(1, 0, Timing.Cue) { EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 3 } });
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => First(p)?.Focus());
    }

    internal Button? First(Part p) =>
        (p == Part.Conn ? new[] { reconnect, restart } : new[] { start, elevate }).FirstOrDefault(b => b.IsVisible && b.IsEnabled);

    void OnClose(object sender, RoutedEventArgs e) => Close();
}
