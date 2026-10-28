using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    }

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
