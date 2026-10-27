using System.Globalization;
using System.Windows.Markup;
using Microsoft.Win32;

namespace PwshMcp.Tray;

public enum Lang { Zh, En }

/// <summary>Current interface language. Exactly one language is shown at a time; every <see cref="Copy"/> entry carries both.</summary>
public static class L
{
    public static Lang Current { get; set; } = Lang.Zh;
    /// <summary>Raised only by a user choice in the UI; never by --language or startup.</summary>
    public static event Action? Changed;

    public static string T(string zh, string en) => Current == Lang.En ? en : zh;
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Current == Lang.En ? Names.EnCulture : Names.ZhCulture);
    public static XmlLanguage Xml => XmlLanguage.GetLanguage(Culture.IetfLanguageTag);
    public static string Tag(Lang l) => l == Lang.En ? Names.EnTag : Names.ZhTag;
    public static Lang? Parse(string? s)
    {
        var t = s?.Trim().ToLowerInvariant() ?? "";
        if (t.StartsWith(Names.EnTag)) return Lang.En;
        if (t.StartsWith(Names.ZhTag)) return Lang.Zh;
        return null;
    }
    public static Lang System => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Names.ZhTag ? Lang.Zh : Lang.En;

    public static void Set(Lang l)
    {
        if (l == Current) return;
        Current = l;
        Changed?.Invoke();
    }
}

/// <summary>Remembered language choice (HKCU). Reading or writing it never touches the broker or any grant.</summary>
public static class Prefs
{
    public static Lang? Load()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(Names.PrefsKey); return L.Parse(k?.GetValue(Names.LangValue) as string); }
        catch { return null; }
    }

    public static void Save(Lang l)
    {
        try { using var k = Registry.CurrentUser.CreateSubKey(Names.PrefsKey); k.SetValue(Names.LangValue, L.Tag(l)); }
        catch { }
    }
}
