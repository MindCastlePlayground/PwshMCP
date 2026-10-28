using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PwshMcp.Tray;

/// <summary>State chip. Shape carries the state, colour reinforces it: dashed = waiting, dot = on, ring = in progress, cross = refused, struck = ended.</summary>
public sealed class Chip : Border
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(Chip), new(string.Empty, (d, _) => ((Chip)d).Build()));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(Tone), typeof(Chip), new(Tone.Off, (d, _) => ((Chip)d).Build()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public Chip() { CornerRadius = new CornerRadius(5); HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Center; Build(); }

    void Build()
    {
        var (fg, bg) = Tone switch
        {
            Tone.Wait => (K.Amber, K.AmberSoft), Tone.On => (K.Green, K.GreenSoft), Tone.Info => (K.Accent, K.AccentSoft), Tone.Bad => (K.Red, K.RedSoft), _ => (K.Muted, K.Raised)
        };
        SetResourceReference(BackgroundProperty, bg);
        var text = new TextBlock { Text = Text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, fg);
        text.SetResourceReference(TextBlock.FontFamilyProperty, K.UiFont);
        if (Tone == Tone.Off) text.TextDecorations = TextDecorations.Strikethrough;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 2, 8, 3) };
        var mark = Mark(fg);
        if (mark is not null) row.Children.Add(mark);
        row.Children.Add(text);
        var grid = new Grid();
        if (Tone == Tone.Wait)
        {
            var dash = new Rectangle { RadiusX = 5, RadiusY = 5, StrokeThickness = 1.2, StrokeDashArray = [3, 2], SnapsToDevicePixels = true };
            dash.SetResourceReference(Shape.StrokeProperty, fg);
            grid.Children.Add(dash);
        }
        grid.Children.Add(row);
        Child = grid;
    }

    static FrameworkElement? Mark(string fg)
    {
        FrameworkElement? m = null;
        switch (fg)
        {
            case K.Green: var dot = new Ellipse { Width = 7, Height = 7 }; dot.SetResourceReference(Shape.FillProperty, fg); m = dot; break;
            case K.Accent: var ring = new Ellipse { Width = 7, Height = 7, StrokeThickness = 1.5 }; ring.SetResourceReference(Shape.StrokeProperty, fg); m = ring; break;
            case K.Red:
                var x = new Path { Data = Geometry.Parse("M0,0 L6,6 M6,0 L0,6"), StrokeThickness = 1.5, Width = 7, Height = 7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                x.SetResourceReference(Shape.StrokeProperty, fg); m = x; break;
        }
        if (m is not null) { m.Margin = new Thickness(0, 0, 6, 0); m.VerticalAlignment = VerticalAlignment.Center; }
        return m;
    }
}

/// <summary>Validity band: issued → now → end. Open-ended (sign-out) access fades out to the right. Width can encode real duration.</summary>
public sealed class Band : FrameworkElement
{
    public static readonly DependencyProperty StartProperty = Reg(nameof(Start), typeof(DateTimeOffset), DateTimeOffset.UtcNow);
    public static readonly DependencyProperty EndProperty = Reg(nameof(End), typeof(DateTimeOffset?), null);
    public static readonly DependencyProperty NowProperty = Reg(nameof(Now), typeof(DateTimeOffset), DateTimeOffset.UtcNow);
    public static readonly DependencyProperty SpanProperty = Reg(nameof(Span), typeof(double), 1.0);
    public static readonly DependencyProperty FillProperty = Reg(nameof(Fill), typeof(Brush), Brushes.Green);
    public static readonly DependencyProperty TrackProperty = Reg(nameof(Track), typeof(Brush), Brushes.LightGray);
    public static readonly DependencyProperty TickProperty = Reg(nameof(Tick), typeof(Brush), Brushes.Black);

    static DependencyProperty Reg(string n, Type t, object? d) => DependencyProperty.Register(n, t, typeof(Band), new FrameworkPropertyMetadata(d, FrameworkPropertyMetadataOptions.AffectsRender));

    public DateTimeOffset Start { get => (DateTimeOffset)GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public DateTimeOffset? End { get => (DateTimeOffset?)GetValue(EndProperty); set => SetValue(EndProperty, value); }
    public DateTimeOffset Now { get => (DateTimeOffset)GetValue(NowProperty); set => SetValue(NowProperty, value); }
    /// <summary>Fraction of the available width the band occupies (duration-proportional previews).</summary>
    public double Span { get => (double)GetValue(SpanProperty); set => SetValue(SpanProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush Tick { get => (Brush)GetValue(TickProperty); set => SetValue(TickProperty, value); }

    protected override Size MeasureOverride(Size s) => new(double.IsInfinity(s.Width) ? 240 : s.Width, 16);

    protected override void OnRender(DrawingContext dc)
    {
        double w = Math.Max(24, ActualWidth * Math.Clamp(Span, 0.05, 1)), h = 6, y = (ActualHeight - h) / 2;
        var open = End is null;
        double frac = open ? Math.Min(0.5, (Now - Start).TotalHours / 16) : Math.Clamp((Now - Start) / ((End!.Value - Start) is var span && span > TimeSpan.Zero ? span : TimeSpan.FromSeconds(1)), 0, 1);
        if (open)
        {
            var fade = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
            var c = (Track as SolidColorBrush)?.Color ?? Colors.Gray;
            fade.GradientStops.Add(new(c, 0)); fade.GradientStops.Add(new(c, 0.7)); fade.GradientStops.Add(new(Color.FromArgb(0, c.R, c.G, c.B), 1));
            dc.DrawRoundedRectangle(fade, null, new Rect(0, y, w, h), 3, 3);
        }
        else
        {
            dc.DrawRoundedRectangle(Track, null, new Rect(0, y, w, h), 3, 3);
            dc.DrawRectangle(Tick, null, new Rect(w - 2, y - 4, 2, h + 8));
        }
        if (frac > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0, y, Math.Max(h, w * frac), h), 3, 3);
        dc.DrawRectangle(Tick, null, new Rect(Math.Clamp(w * frac, 0, w - 2), y - 4, 2, h + 8));
    }
}

public static class All
{
    public static readonly IMultiValueConverter Vis = new AllVis();
    public static readonly IValueConverter Not = new NotConv();
    /// <summary>Accessible name from button content: access-key markers stripped ("撤销(_V)" → "撤销(V)"), icon panels reduced to their AccessText.</summary>
    public static readonly IValueConverter Plain = new PlainConv();
    /// <summary>Scroll edge rules: Above from VerticalOffset; Below from (VerticalOffset, ScrollableHeight). Works in pixel and item units.</summary>
    public static readonly IValueConverter Above = new AboveConv();
    public static readonly IMultiValueConverter Below = new BelowConv();
    sealed class AboveConv : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v is double o && o > 0.5 ? Visibility.Visible : Visibility.Hidden;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
    sealed class BelowConv : IMultiValueConverter
    {
        public object Convert(object[] v, Type t, object p, CultureInfo c) => v is [double o, double s] && o < s - 0.5 ? Visibility.Visible : Visibility.Hidden;
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => [];
    }
    sealed class AllVis : IMultiValueConverter
    {
        public object Convert(object[] v, Type t, object p, CultureInfo c) => v.All(x => x is true) ? Visibility.Visible : Visibility.Collapsed;
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => [];
    }
    sealed class NotConv : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v is not true;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is not true;
    }
    sealed class PlainConv : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v switch
        {
            string s => Strip(s),
            AccessText a => Strip(a.Text),
            Panel g => g.Children.OfType<AccessText>().Select(a => Strip(a.Text)).FirstOrDefault() ?? "",
            _ => ""
        };
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
        static string Strip(string s) => s.Replace("__", "\0").Replace("_", "").Replace('\0', '_');
    }
}

public sealed class Vis : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        bool b = v switch { bool x => x, string s => s.Length > 0, int i => i > 0, null => false, _ => true };
        if (p is not null) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
