using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PwshMcp.Tray;

public enum Syn : byte { Plain, Comment, Str, Var, Key, Cmd, Num, Param, Type }

/// <summary>
/// Display-only PowerShell lexer. It colours text and never parses for meaning or executes anything.
/// Covers comments (line and block), strings (quoted and here-strings), variables, keywords, Verb-Noun cmdlets, numbers, parameters/operators and [types].
/// </summary>
public static class Lex
{
    /// <summary>Above this length the command is shown as plain text: colour isn't worth the layout cost.</summary>
    public const int Limit = 20000;

    static readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        "begin", "break", "catch", "class", "continue", "data", "do", "dynamicparam", "else", "elseif", "end", "enum", "exit", "filter", "finally",
        "for", "foreach", "function", "if", "in", "param", "process", "return", "switch", "throw", "trap", "try", "until", "using", "while", "workflow"
    };

    public static Syn[] Kinds(string s)
    {
        var k = new Syn[s.Length];
        int i = 0;
        void Mark(int from, int to, Syn t) { for (int x = from; x < to && x < k.Length; x++) k[x] = t; }
        char At(int x) => x < s.Length ? s[x] : '\0';
        bool Word(char c) => char.IsLetterOrDigit(c) || c == '_';
        bool Edge(int x) => x == 0 || !(Word(s[x - 1]) || s[x - 1] == '-' || s[x - 1] == '.');

        while (i < s.Length)
        {
            char c = s[i];
            int start = i;
            if (c == '<' && At(i + 1) == '#')
            {
                var e = s.IndexOf("#>", i + 2, StringComparison.Ordinal);
                i = e < 0 ? s.Length : e + 2; Mark(start, i, Syn.Comment);
            }
            else if (c == '#')
            {
                while (i < s.Length && s[i] is not ('\n' or '\r')) i++;
                Mark(start, i, Syn.Comment);
            }
            else if (c == '@' && At(i + 1) is '\'' or '"' && At(i + 2) is '\n' or '\r')
            {
                var q = At(i + 1); i += 2;
                // Here-string ends at a line that starts with the quote followed by @.
                while (i < s.Length && !((s[i - 1] is '\n' or '\r') && s[i] == q && At(i + 1) == '@')) i++;
                i = Math.Min(s.Length, i + 2); Mark(start, i, Syn.Str);
            }
            else if (c == '\'')
            {
                i++;
                while (i < s.Length) { if (s[i] == '\'') { if (At(i + 1) == '\'') { i += 2; continue; } i++; break; } i++; }
                Mark(start, i, Syn.Str);
            }
            else if (c == '"')
            {
                i++;
                while (i < s.Length)
                {
                    if (s[i] == '`') { i += 2; continue; }
                    if (s[i] == '"') { if (At(i + 1) == '"') { i += 2; continue; } i++; break; }
                    i++;
                }
                i = Math.Min(i, s.Length); Mark(start, i, Syn.Str);
            }
            else if (c == '$' && At(i + 1) == '{')
            {
                var e = s.IndexOf('}', i + 2);
                i = e < 0 ? s.Length : e + 1; Mark(start, i, Syn.Var);
            }
            else if (c is '$' or '@' && (Word(At(i + 1)) || (c == '$' && At(i + 1) is '_' or '$' or '?' or '^')))
            {
                i += 2;
                while (i < s.Length && (Word(s[i]) || (s[i] == ':' && Word(At(i + 1))))) i++;
                Mark(start, i, Syn.Var);
            }
            else if (char.IsDigit(c) && (i == 0 || !(Word(s[i - 1]) || s[i - 1] == '.')))
            {
                if (c == '0' && At(i + 1) is 'x' or 'X') { i += 2; while (i < s.Length && Uri.IsHexDigit(s[i])) i++; }
                else { while (i < s.Length && (char.IsDigit(s[i]) || (s[i] == '.' && char.IsDigit(At(i + 1))))) i++; }
                // Multiplier and type suffixes: 10kb, 5mb, 2l, 1.5d.
                while (i < s.Length && char.IsLetter(s[i]) && i - start < 24 && "kmgtpbdl".Contains(char.ToLowerInvariant(s[i]))) i++;
                if (i < s.Length && Word(s[i])) { while (i < s.Length && Word(s[i])) i++; continue; }
                Mark(start, i, Syn.Num);
            }
            else if (c == '-' && char.IsLetter(At(i + 1)) && (i == 0 || char.IsWhiteSpace(s[i - 1]) || s[i - 1] is '(' or '{' or ',' or ';' or '|'))
            {
                i++;
                while (i < s.Length && (Word(s[i]) || s[i] == '-')) i++;
                Mark(start, i, Syn.Param);
            }
            else if (c == '[' && char.IsLetter(At(i + 1)))
            {
                int x = i + 1;
                while (x < s.Length && (Word(s[x]) || s[x] is '.' or '[' or ']' or ',' or '`') && s[x] != ']') x++;
                if (x < s.Length && s[x] == ']') { i = x + 1; Mark(start, i, Syn.Type); }
                else i++;
            }
            else if ((char.IsLetter(c) || c == '_') && Edge(i))
            {
                while (i < s.Length && (Word(s[i]) || s[i] == '-' || s[i] == '.')) i++;
                var w = s[start..i];
                if (Keys.Contains(w)) Mark(start, i, Syn.Key);
                else if (Verb(w)) Mark(start, i, Syn.Cmd);
            }
            else i++;
        }
        return k;
    }

    /// <summary>Verb-Noun shape (Get-ChildItem, Set-Content). Native exes and aliases stay plain.</summary>
    static bool Verb(string w)
    {
        var d = w.IndexOf('-');
        return d > 1 && d < w.Length - 1 && !w.Contains('.') && char.IsLetter(w[0]) && char.IsLetter(w[d + 1]) && w.Take(d).All(char.IsLetter);
    }

    /// <summary>Logical lines, keeping each line's text exactly (no separators).</summary>
    public static string[] Lines(string s) => s.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

    /// <summary>The line separator to restore on copy: the one the command itself uses.</summary>
    public static string Nl(string s) => s.Contains("\r\n") ? "\r\n" : s.Contains('\r') && !s.Contains('\n') ? "\r" : "\n";
}

/// <summary>
/// Read-only PowerShell code block: wraps to the available width (soft wraps hang-indented), colours tokens, and copies the original text.
/// Soft wraps are layout only; neither "Copy command" nor a selection copy inserts them.
/// </summary>
public sealed class CodeView : Border
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(string), typeof(CodeView), new(string.Empty, (d, _) => ((CodeView)d).Build()));
    public static readonly DependencyProperty MaxBodyProperty = DependencyProperty.Register(nameof(MaxBody), typeof(double), typeof(CodeView), new(320.0, (d, e) => ((CodeView)d).box.MaxHeight = (double)e.NewValue));
    public string Code { get => (string)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    public double MaxBody { get => (double)GetValue(MaxBodyProperty); set => SetValue(MaxBodyProperty, value); }

    const double Hang = 18;
    static readonly Dictionary<Syn, string> Brush = new()
    {
        [Syn.Plain] = K.Ink, [Syn.Comment] = K.SynComment, [Syn.Str] = K.SynStr, [Syn.Var] = K.SynVar, [Syn.Key] = K.SynKey,
        [Syn.Cmd] = K.SynCmd, [Syn.Num] = K.SynNum, [Syn.Param] = K.SynParam, [Syn.Type] = K.SynType
    };

    readonly RichTextBox box;
    readonly FlowDocument doc = new() { PagePadding = new Thickness(0), FontSize = 12.5, LineHeight = 19, TextAlignment = TextAlignment.Left };
    readonly TextBlock meta = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Button copy;
    readonly DispatcherTimer reset = new() { Interval = Timing.Copied };

    public CodeView()
    {
        CornerRadius = new CornerRadius(8); BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, K.Code); SetResourceReference(BorderBrushProperty, K.Line);
        doc.SetResourceReference(FlowDocument.FontFamilyProperty, K.MonoFont);
        doc.SetResourceReference(FlowDocument.ForegroundProperty, K.Ink);

        box = new RichTextBox
        {
            Document = doc, IsReadOnly = true, IsReadOnlyCaretVisible = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), Margin = new Thickness(12, 8, 12, 10), MaxHeight = MaxBody, FocusVisualStyle = null, IsUndoEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        box.SetResourceReference(TextBoxBase.SelectionBrushProperty, K.Accent);
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, K.Ink);
        box.SetResourceReference(ForegroundProperty, K.Ink);
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { Put(Pick(box.Selection.Start, box.Selection.End)); e.Handled = true; }, (_, e) => { e.CanExecute = true; e.Handled = true; }));
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, e) => e.Handled = true, (_, e) => { e.CanExecute = false; e.Handled = true; }));
        System.Windows.Automation.AutomationProperties.SetName(box, Copy.Code.Name);
        box.GotKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, K.Focus);
        box.LostKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, K.Line);

        meta.SetResourceReference(TextBlock.ForegroundProperty, K.Muted);
        meta.SetResourceReference(TextBlock.FontFamilyProperty, K.UiFont);
        copy = new Button { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 4), MinHeight = 26, FontSize = 12 };
        copy.SetResourceReference(StyleProperty, K.Quiet);
        copy.Click += (_, _) => { Put(Code); Flash(); };
        reset.Tick += (_, _) => { reset.Stop(); copy.Content = Copy.Code.CopyAll; };

        var head = new DockPanel { Margin = new Thickness(12, 6, 6, 0) };
        DockPanel.SetDock(copy, Dock.Right);
        head.Children.Add(copy); head.Children.Add(meta);
        var col = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        col.Children.Add(head); col.Children.Add(box);
        Child = col;
        Build();
    }

    public RichTextBox Box => box;
    public FlowDocument Doc => doc;

    void Build()
    {
        var code = Code ?? "";
        var lines = Lex.Lines(code);
        var plain = code.Length > Lex.Limit;
        meta.Text = $"{Copy.Code.Kind}  ·  {(lines.Length == 1 ? Copy.Code.OneLine : string.Format(Copy.Code.Lines, lines.Length))}" + (plain ? $"  ·  {Copy.Code.Plain}" : "");
        copy.Content = Copy.Code.CopyAll;
        doc.Blocks.Clear();
        var kinds = plain ? new Syn[code.Length] : Lex.Kinds(code);
        int at = 0;
        foreach (var line in lines)
        {
            var p = new Paragraph { Margin = new Thickness(Hang, 0, 0, 0), TextIndent = -Hang };
            int i = 0;
            while (i < line.Length)
            {
                var k = kinds[at + i]; int j = i;
                while (j < line.Length && kinds[at + j] == k) j++;
                var r = new Run(line[i..j]);
                if (k != Syn.Plain) r.SetResourceReference(TextElement.ForegroundProperty, Brush[k]);
                if (k == Syn.Comment) r.FontStyle = FontStyles.Italic;
                p.Inlines.Add(r);
                i = j;
            }
            doc.Blocks.Add(p);
            at += line.Length;
            // Skip the separator this line ended with.
            if (at < code.Length) at += code[at] == '\r' && at + 1 < code.Length && code[at + 1] == '\n' ? 2 : 1;
        }
    }

    /// <summary>Copy text for a selection: the original command when everything (or nothing) is selected, otherwise the selected text.</summary>
    public string Pick(TextPointer a, TextPointer b)
    {
        var t = Raw(a, b);
        return t.Length == 0 || t.TrimEnd('\r', '\n') == Flat().TrimEnd('\r', '\n') ? Code : t;
    }

    /// <summary>Range text with the command's own line separators. FlowDocument reports paragraph breaks as CRLF; soft wraps never appear.</summary>
    public string Raw(TextPointer a, TextPointer b) => new TextRange(a, b).Text.Replace("\r\n", "\n").Replace("\n", Lex.Nl(Code));

    /// <summary>Whole document as text, minus the break FlowDocument appends after the last paragraph.</summary>
    public string Whole()
    {
        var t = Raw(doc.ContentStart, doc.ContentEnd); var nl = Lex.Nl(Code);
        return t.EndsWith(nl, StringComparison.Ordinal) ? t[..^nl.Length] : t;
    }
    /// <summary>The document text rebuilt from its runs; used by the self-test to prove nothing was added or lost.</summary>
    public string Flat() => string.Join(Lex.Nl(Code), doc.Blocks.OfType<Paragraph>().Select(p => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text))));

    static void Put(string text)
    {
        for (int n = 0; n < 3; n++)
        {
            try { Clipboard.SetDataObject(text, true); return; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(40); }
        }
    }

    void Flash() { copy.Content = Copy.Code.Copied; reset.Stop(); reset.Start(); }
}
