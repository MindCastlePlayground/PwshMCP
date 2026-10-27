using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PwshMcp.Tray;

/// <summary>Resource keys. XAML references them through x:Static so no key is a loose string.</summary>
public static class K
{
    public const string Ground = "pm.ground", Panel = "pm.panel", Raised = "pm.raised", Line = "pm.line", Ink = "pm.ink", Muted = "pm.muted";
    public const string Accent = "pm.accent", AccentInk = "pm.accentInk", AccentSoft = "pm.accentSoft", Hover = "pm.hover", Press = "pm.press";
    public const string Amber = "pm.amber", AmberSoft = "pm.amberSoft", Green = "pm.green", GreenSoft = "pm.greenSoft", Red = "pm.red", RedSoft = "pm.redSoft";
    public const string Code = "pm.code", Focus = "pm.focus";
    public const string UiFont = "pm.uiFont", MonoFont = "pm.monoFont";
    public const string Primary = "pm.btnPrimary", Secondary = "pm.btnSecondary", Danger = "pm.btnDanger", Quiet = "pm.btnQuiet";
    public const string Segment = "pm.segment", Row = "pm.row", Label = "pm.label", Value = "pm.value", Title = "pm.title", Section = "pm.section", Body = "pm.body", CodeBox = "pm.codeBox", Choice = "pm.choice", Pane = "pm.pane", Vis = "pm.vis", Shield = "pm.shield", Mark = "pm.mark";
}

public sealed record Palette(string Ground, string Panel, string Raised, string Line, string Ink, string Muted, string Accent, string AccentInk, string AccentSoft, string Hover, string Press,
    string Amber, string AmberSoft, string Green, string GreenSoft, string Red, string RedSoft, string Code, string Focus);

public static class Theme
{
    public static readonly Palette Light = new("#F4F5F7", "#FFFFFF", "#EEF0F3", "#DDE1E6", "#171A21", "#555E6C", "#3346C8", "#FFFFFF", "#E6E9FB", "#0F171A21", "#1C171A21",
        "#8A5300", "#FFF1D6", "#0B7A4E", "#DFF4EA", "#B3261E", "#FDE7E5", "#F6F7F9", "#3346C8");
    public static readonly Palette Dark = new("#14161B", "#1B1E24", "#242830", "#313640", "#ECEEF2", "#A0A8B6", "#9AA6FF", "#10132A", "#262B4A", "#14FFFFFF", "#24FFFFFF",
        "#FFC46B", "#3A2D14", "#6BD9A8", "#15332A", "#FF8F86", "#3D1B1A", "#111317", "#9AA6FF");

    public static bool AppsLight => Read(Names.AppsLight);
    public static bool BarLight => Read(Names.SystemLight);
    public static bool Motion => SystemParameters.ClientAreaAnimation;

    static bool Read(string name)
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(Names.ThemeKey); return key?.GetValue(name) is not int v || v != 0; }
        catch { return true; }
    }

    public static void Apply(ResourceDictionary res)
    {
        var p = AppsLight ? Light : Dark;
        void Set(string key, string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); res[key] = b; }
        Set(K.Ground, p.Ground); Set(K.Panel, p.Panel); Set(K.Raised, p.Raised); Set(K.Line, p.Line); Set(K.Ink, p.Ink); Set(K.Muted, p.Muted);
        Set(K.Accent, p.Accent); Set(K.AccentInk, p.AccentInk); Set(K.AccentSoft, p.AccentSoft); Set(K.Hover, p.Hover); Set(K.Press, p.Press);
        Set(K.Amber, p.Amber); Set(K.AmberSoft, p.AmberSoft); Set(K.Green, p.Green); Set(K.GreenSoft, p.GreenSoft); Set(K.Red, p.Red); Set(K.RedSoft, p.RedSoft);
        Set(K.Code, p.Code); Set(K.Focus, p.Focus);
        res[K.UiFont] = new FontFamily(Names.UiFont);
        res[K.MonoFont] = new FontFamily(Names.MonoFont);
    }
}
