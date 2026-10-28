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
    public const string CodeTpl = "pm.tpl.code", CmdTpl = "pm.tpl.cmd", ScopeTpl = "pm.tpl.scope", DiagTpl = "pm.tpl.diag", BusyTpl = "pm.tpl.busy";
    public const string Issue = "pm.issue", Blocked = "pm.blocked", Pick = "pm.pick", Pop = "pm.pop";
    public const string Scroll = "pm.scroll", List = "pm.list", Page = "pm.page", Thumb = "pm.thumb";
    public const string Desc = "pm.desc", Task = "pm.task", Fact = "pm.fact", Facts = "pm.facts", Diag = "pm.diag", Ident = "pm.ident", Scope = "pm.scope", Lang = "pm.lang";
    public const string SynComment = "pm.synComment", SynStr = "pm.synStr", SynVar = "pm.synVar", SynKey = "pm.synKey", SynCmd = "pm.synCmd", SynNum = "pm.synNum", SynParam = "pm.synParam", SynType = "pm.synType";
}

/// <summary>PowerShell token colours, tuned against the code surface of each theme (all ≥ 4.5:1).</summary>
public sealed record Syntax(string Comment, string Str, string Var, string Key, string Cmd, string Num, string Param, string Type);

public sealed record Palette(string Ground, string Panel, string Raised, string Line, string Ink, string Muted, string Accent, string AccentInk, string AccentSoft, string Hover, string Press,
    string Amber, string AmberSoft, string Green, string GreenSoft, string Red, string RedSoft, string Code, string Focus);

public static class Theme
{
    public static readonly Palette Light = new("#F4F5F7", "#FFFFFF", "#EEF0F3", "#DDE1E6", "#171A21", "#555E6C", "#3346C8", "#FFFFFF", "#E6E9FB", "#0F171A21", "#1C171A21",
        "#8A5300", "#FFF1D6", "#0B7A4E", "#DFF4EA", "#B3261E", "#FDE7E5", "#F6F7F9", "#3346C8");
    public static readonly Palette Dark = new("#14161B", "#1B1E24", "#242830", "#313640", "#ECEEF2", "#A0A8B6", "#9AA6FF", "#10132A", "#262B4A", "#14FFFFFF", "#24FFFFFF",
        "#FFC46B", "#3A2D14", "#6BD9A8", "#15332A", "#FF8F86", "#3D1B1A", "#111317", "#9AA6FF");

    public static readonly Syntax SynLight = new("#5F6B7A", "#9A3412", "#0B6E79", "#7A2DB0", "#2F42BE", "#8A5300", "#5A4AB0", "#6B4E16");
    public static readonly Syntax SynDark = new("#8B94A3", "#F2A97E", "#6FD3E0", "#D29CFF", "#9AA6FF", "#FFC46B", "#BBB2FF", "#E6C98A");

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
        var y = AppsLight ? SynLight : SynDark;
        Set(K.SynComment, y.Comment); Set(K.SynStr, y.Str); Set(K.SynVar, y.Var); Set(K.SynKey, y.Key);
        Set(K.SynCmd, y.Cmd); Set(K.SynNum, y.Num); Set(K.SynParam, y.Param); Set(K.SynType, y.Type);
        Fonts(res);
    }

    /// <summary>
    /// Everything app-level that follows the interface language. UI face: each language sits on its own native baseline.
    /// Shared templates read labels with x:Static once, when the dictionary loads, so Styles is loaded again under the
    /// current language; windows built afterwards (App.Relang rebuilds the open one) pick up the fresh templates.
    /// </summary>
    public static void Fonts(ResourceDictionary res)
    {
        res[K.UiFont] = new FontFamily(L.Current == Lang.En ? Names.EnUiFont : Names.ZhUiFont);
        res[K.MonoFont] = new FontFamily(Names.MonoFont);
        var src = new Uri(Names.Styles, UriKind.Absolute);
        var md = res.MergedDictionaries;
        for (int i = 0; i < md.Count; i++)
            if (md[i].Source == src) { md[i] = new ResourceDictionary { Source = src }; return; }
    }
}
