using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace PwshMcp.Tray;

/// <summary>A non-modal, non-activating arrival window. No default approval button or global shortcuts.</summary>
public partial class NoticeWindow : Window
{
    readonly GrantVm grant;
    readonly Action details, later, mute;
    bool completed;
    public NoticeWindow(GrantVm grant, int pending, Action details, Action later, Action mute)
    {
        this.grant = grant; this.details = details; this.later = later; this.mute = mute;
        InitializeComponent();
        Language = L.Xml;
        DataContext = grant;
        Refresh(pending);
        SourceInitialized += (_, _) => Place();
        Closed += (_, _) => { if (!completed) later(); };
    }

    public static string Label(Life life) => life switch
    {
        Life.OneHour => Copy.Notify.Hour,
        Life.FourHours => Copy.Notify.Four,
        Life.Today => Copy.Notify.Today,
        _ => Copy.Notify.SignOut
    };

    void Place()
    {
        var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        var source = (HwndSource?)PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not { } target) return;
        var a = target.TransformFromDevice.Transform(new Point(area.Left, area.Top));
        var b = target.TransformFromDevice.Transform(new Point(area.Right, area.Bottom));
        MinWidth = Math.Min(MinWidth, b.X - a.X);
        MinHeight = Math.Min(MinHeight, b.Y - a.Y);
        Width = Math.Min(Width, b.X - a.X - 24);
        Height = Math.Min(Height, b.Y - a.Y - 24);
        Left = Math.Max(a.X, b.X - Width - 12);
        Top = Math.Max(a.Y, b.Y - Height - 12);
    }

    public void Refresh(int pending)
    {
        count.Text = pending > 1 ? string.Format(Copy.Notify.Pending, pending - 1) : "";
        unavailable.Visibility = !grant.CanApprove && !grant.Sending && !grant.Accepted ? Visibility.Visible : Visibility.Collapsed;
        grant.Touch();
    }

    /// <summary>Programmatic close, e.g. DND or request resolution, does not discard deferred notifications.</summary>
    public void Retire() { completed = true; Close(); }
    void OnLater(object sender, RoutedEventArgs e) => Close();
    void OnDetails(object sender, RoutedEventArgs e) { details(); Close(); }
    void OnMute(object sender, RoutedEventArgs e) { completed = true; mute(); Close(); }
    async void OnApprove(object sender, RoutedEventArgs e)
    {
        await grant.Submit();
        if (grant.Accepted) Retire();
    }
}
