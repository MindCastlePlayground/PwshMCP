using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

/// <summary>
/// Offline self-test (--self-test) and render check (--render-check). Windows are built from synthetic data and laid out
/// without ever being shown, so Loaded never fires: no broker pipe, no UAC, no tray icon, no real grant is touched.
/// </summary>
public static class Check
{
    static readonly List<string> log = [];
    static int runs, bad;

    static readonly Regex Hole = new(@"\{(\d+)[^}]*\}", RegexOptions.Compiled);
    static readonly Regex Mnemonic = new(@"(?<!_)_([A-Za-z0-9])", RegexOptions.Compiled);

    static readonly int[] MainWidths = [720, 1000, 1400], ApproveWidths = [520, 640];
    const int MainHeight = 660, ApproveHeight = 760;

    public static int Run(string? dir, bool render)
    {
        dir ??= Path.Combine(Environment.CurrentDirectory, Names.ArtifactDir, Names.ArtifactSub);
        Directory.CreateDirectory(dir);
        var keep = L.Current;
        try
        {
            Strings();
            Tokens();
            Wraps();
            foreach (var l in new[] { Lang.Zh, Lang.En }) Windows(l, render ? dir : null);
        }
        catch (Exception e) { Note(false, Report.Layout, e.ToString()); }
        finally { L.Current = keep; Theme.Fonts(Application.Current.Resources); }
        log.Add(string.Format(Report.Sum, runs, bad));
        File.WriteAllLines(Path.Combine(dir, Names.Report), log);
        return bad;
    }

    static void Note(bool ok, string area, string what)
    {
        runs++; if (!ok) bad++;
        log.Add(string.Format(Report.Line, ok ? Report.Pass : Report.Fail, area, what));
    }

    static void Info(string area, string what) => log.Add(string.Format(Report.Line, Report.Info, area, what));

    // ---- bilingual copy -------------------------------------------------------------------------------------------

    static IEnumerable<PropertyInfo> Props(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
         .Concat(t.GetNestedTypes(BindingFlags.Public).SelectMany(Props));

    static string Read(PropertyInfo p, Lang l) { L.Current = l; return (string?)p.GetValue(null) ?? ""; }

    static void Strings()
    {
        int n = 0;
        foreach (var p in Props(typeof(Copy)))
        {
            var name = $"{p.DeclaringType?.Name}.{p.Name}";
            var zh = Read(p, Lang.Zh); var en = Read(p, Lang.En);
            n++;
            if (zh.Length == 0 || en.Length == 0) Note(false, Report.Keys, name);
            var hz = Hole.Matches(zh).Select(m => m.Groups[1].Value).ToHashSet();
            var he = Hole.Matches(en).Select(m => m.Groups[1].Value).ToHashSet();
            if (!hz.SetEquals(he)) Note(false, Report.Holes, $"{name} zh{{{string.Join(",", hz)}}} en{{{string.Join(",", he)}}}");
            var kz = Mnemonic.Match(zh); var ke = Mnemonic.Match(en);
            if (kz.Success != ke.Success || kz.Success && !string.Equals(kz.Groups[1].Value, ke.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                Note(false, Report.Access, name);
            if (zh == en && zh.Length > 0) Info(Report.Same, name);
        }
        Note(true, Report.Keys, n.ToString());
    }

    // ---- PowerShell lexer -----------------------------------------------------------------------------------------

    static readonly (string Code, string At, Syn Kind)[] Cases =
    [
        ("Get-ChildItem -Path 'C:\\x' | Where-Object { $_.Length -gt 10 } # tail", "Get-ChildItem", Syn.Cmd),
        ("Get-ChildItem -Path 'C:\\x' | Where-Object { $_.Length -gt 10 } # tail", "-Path", Syn.Param),
        ("Get-ChildItem -Path 'C:\\x' | Where-Object { $_.Length -gt 10 } # tail", "'C:", Syn.Str),
        ("Get-ChildItem -Path 'C:\\x' | Where-Object { $_.Length -gt 10 } # tail", "$_", Syn.Var),
        ("Get-ChildItem -Path 'C:\\x' | Where-Object { $_.Length -gt 10 } # tail", "10", Syn.Num),
        ("Get-ChildItem -Path 'C:\\x' | Where-Object { $_.Length -gt 10 } # tail", "# tail", Syn.Comment),
        ("if ($a) { return 0x1F }", "if", Syn.Key),
        ("if ($a) { return 0x1F }", "0x1F", Syn.Num),
        ("[string]$s = \"a $b\"", "[string]", Syn.Type),
        ("[string]$s = \"a $b\"", "\"a", Syn.Str),
        ("<# block\n#>\nWrite-Host 1", "block", Syn.Comment),
        ("<# block\n#>\nWrite-Host 1", "Write-Host", Syn.Cmd),
        ("@'\nliteral $x\n'@\n$y", "$x", Syn.Str),
        ("@'\nliteral $x\n'@\n$y", "$y", Syn.Var),
        ("$size = 10mb", "10mb", Syn.Num),
    ];

    static void Tokens()
    {
        foreach (var (code, at, kind) in Cases)
        {
            var i = code.IndexOf(at, StringComparison.Ordinal);
            var k = Lex.Kinds(code);
            Note(k.Length == code.Length && i >= 0 && k[i] == kind, Report.Lex, $"{at} → {kind} ({(i >= 0 && i < k.Length ? k[i] : Syn.Plain)})");
        }
    }

    // ---- code wrapping and copy fidelity --------------------------------------------------------------------------

    static readonly string LongLine = "Write-Output '" + new string('x', 2000) + "'";
    static readonly string LongPath = "Set-Location C:\\" + string.Join("\\", Enumerable.Repeat("VeryLongFolderName", 60));
    static readonly string[] Samples =
    [
        "Get-Item 'C:\\路径\\文件.txt'\n\tWrite-Host \"✓ 完成 done\" # 注释\n",
        "$a = 1\r\n$b = 2\r\n\r\nif ($a -lt $b) { 'ok' }",
        LongLine,
        LongPath,
        "@\"\nhere $env:PATH\n\"@\nGet-Date",
        string.Join("\n", Enumerable.Range(0, 900).Select(i => $"Write-Output {i} # line {i}")),
        "",
    ];

    static CodeView View(string code, double w)
    {
        var v = new CodeView { Code = code, MaxBody = double.PositiveInfinity };
        var host = Host(v, K.Code);
        host.Measure(new Size(w, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        return v;
    }

    static void Wraps()
    {
        int n = 0;
        foreach (var s in Samples)
        {
            var v = View(s, 260);
            var tag = $"#{n++} ({s.Length} chars)";
            Note(v.Flat() == s, Report.Fidelity, $"{tag} runs");
            Note(v.Whole() == s, Report.Fidelity, $"{tag} document text");
            Note(v.Pick(v.Doc.ContentStart, v.Doc.ContentEnd) == s, Report.Fidelity, $"{tag} select-all copy");
        }

        var mixed = "a\r\nb\nc";
        var m = View(mixed, 400);
        Note(m.Pick(m.Doc.ContentStart, m.Doc.ContentEnd) == mixed, Report.Fidelity, "mixed newlines select-all copy");

        var four = View("l1\nl2\nl3\nl4", 400);
        var ps = four.Doc.Blocks.OfType<Paragraph>().ToArray();
        Note(four.Raw(ps[1].ContentStart, ps[2].ContentEnd) == "l2\nl3", Report.Fidelity, "partial selection lines 2-3");

        var huge = new string('$', Lex.Limit + 5);
        Note(View(huge, 400).Flat() == huge, Report.Fidelity, $"> {Lex.Limit} chars shown as plain text");

        var narrow = View(LongLine, 260).Box.DesiredSize; var wide = View(LongLine, 900).Box.DesiredSize;
        Note(narrow.Height > wide.Height * 2 && narrow.Width <= 260 && wide.Width <= 900, Report.Wrap,
            $"2000-char line wraps: 260px→{narrow.Height:0}px tall, 900px→{wide.Height:0}px tall");
        var path = View(LongPath, 260).Box.DesiredSize;
        Note(path.Width <= 260, Report.Wrap, $"long path without spaces stays within 260px ({path.Width:0})");
    }

    // ---- windows: layout in both languages, optional PNGs ---------------------------------------------------------

    static readonly DateTimeOffset Now = DateTimeOffset.Now;
    static readonly string LongDesc = string.Concat(Enumerable.Repeat("检查磁盘空间并清理临时文件 Check disk space and clean temp files. ", 32))[..1000];

    static RunSpec Spec(string desc, string code) => new()
    {
        Description = desc, Code = code, Cwd = "C:\\Users\\sample\\source\\repos\\" + string.Join("\\", Enumerable.Repeat("nested-folder", 8)),
    };

    static AccessInfo Grant(string id, AccessState st, string task) => new()
    {
        Id = id, TaskName = task, ClientName = "Claude Code · sample-workstation", Client = "client-key-" + new string('7', 40), Label = "sample",
        User = "SAMPLE\\operator", Sid = "S-1-5-21-1000000000-2000000000-3000000000-1001", SessionId = 2, State = st,
        CreatedAt = Now.AddMinutes(-3), ExpiresAt = st == AccessState.Pending ? Now.AddMinutes(4) : Now.AddHours(3),
        HeartbeatAt = st == AccessState.Active ? Now : null,
    };

    static JobInfo Job(string id, JobState st, string? grant, RunSpec run, ExecutionResult? res = null) => new()
    {
        Id = id, Client = "client-key-" + new string('7', 40), ClientName = "Claude Code · sample-workstation", TaskName = "清理构建缓存 / Clean build caches",
        OwnerSid = "S-1-5-21-1000000000-2000000000-3000000000-1001", SessionId = 2, GrantId = grant, Run = run, State = st, CreatedAt = Now.AddMinutes(-2), Result = res,
    };

    static Snapshot Snap()
    {
        var waiting = Job("job-wait-0001", JobState.NeedsApproval, "grant-pending-0001", Spec(LongDesc, Samples[0] + LongLine));
        var done = Job("job-done-0002", JobState.Succeeded, "grant-active-0002", Spec("列出最大的十个文件 / List the ten largest files", Samples[1]),
            new ExecutionResult { ExitCode = 0, Output = "ok\n" + LongPath, Engine = "PowerShell 7.4", Identity = "SAMPLE\\operator (elevated)" });
        var bare = Job("job-run-0003", JobState.Running, null, Spec("", LongPath));
        return new Snapshot
        {
            Grants = [Grant("grant-pending-0001", AccessState.Pending, "清理构建缓存并释放磁盘空间 Clean build caches and free disk space " + new string('长', 60)),
                      Grant("grant-active-0002", AccessState.Active, "整理日志 / Tidy logs")],
            Jobs = [waiting, done, bare], ElevatedBroker = true,
        };
    }

    /// <summary>Bare layout root. Unparented elements still resolve app-level resources, so no dictionary is copied.</summary>
    static Border Host(FrameworkElement root, string ground)
    {
        var host = new Border { Child = root, Language = L.Xml, UseLayoutRounding = true, SnapsToDevicePixels = true };
        host.SetResourceReference(Border.BackgroundProperty, ground);
        host.SetResourceReference(TextElement.FontFamilyProperty, K.UiFont);
        host.SetResourceReference(TextElement.ForegroundProperty, K.Ink);
        return host;
    }

    /// <summary>Moves a never-shown window's content into a bare host so it can be measured and rendered offline.</summary>
    static Border Detach(Window w, string ground)
    {
        var root = (FrameworkElement)w.Content;
        var ctx = w.DataContext;
        w.Content = null;
        foreach (var k in w.Resources.Keys) root.Resources[k] = w.Resources[k];
        root.DataContext = ctx;
        return Host(root, ground);
    }

    static void Windows(Lang l, string? dir)
    {
        L.Current = l;
        Theme.Fonts(Application.Current.Resources);
        var tag = L.Tag(l);

        var vm = new MainVm(new Monitor(null, _ => { }), null);
        var main = new MainWindow(vm);
        var host = Detach(main, K.Ground);
        foreach (var t in new[] { Tab.Requests, Tab.Access, Tab.Tasks })
        {
            vm.Seed(new Status(Conn.Online, Snap(), Now, "", true, true), t);
            vm.SelReq ??= vm.Requests.FirstOrDefault(); vm.SelAcc ??= vm.Access.FirstOrDefault(); vm.SelTask ??= vm.Tasks.FirstOrDefault();
            if (t == Tab.Tasks) vm.SelTask = vm.Tasks.FirstOrDefault(j => !j.HasDesc) ?? vm.SelTask;
            // Skip the selection fade so a render shows the settled frame.
            main.detail.BeginAnimation(UIElement.OpacityProperty, null);
            main.rise.BeginAnimation(TranslateTransform.YProperty, null);
            foreach (var w in MainWidths) Lay(host, w, MainHeight, $"{Report.Main} {tag} {t} {w}px", dir, Report.Main, tag, $"{t}-{w}");
            Shared(host, $"{Report.Main} {tag} {t}", SharedOn[t]);
        }
        Note(vm.Requests.Count > 0 && vm.Access.Count > 0 && vm.Tasks.Count > 0, Report.Layout, $"{Report.Main} {tag} sample data loaded");

        var avm = new ApproveVm("grant-pending-0001", null, false);
        var s = Snap();
        avm.Seed(s.Grants[0], s.Jobs.Where(j => j.GrantId == s.Grants[0].Id));
        var ah = Detach(new ApproveWindow(avm), K.Panel);
        foreach (var w in ApproveWidths) Lay(ah, w, ApproveHeight, $"{Report.Approve} {tag} {w}px", dir, Report.Approve, tag, w.ToString());
        Shared(ah, $"{Report.Approve} {tag}", [Who(nameof(Copy.Who.Scope))]);

        // The whole highlighted sample at full height: the detail pane only shows its first screen.
        foreach (var w in CodeWidths)
        {
            var ch = Host(new CodeView { Code = Samples[0] + LongLine, MaxBody = double.PositiveInfinity }, K.Code);
            ch.Measure(new Size(w, double.PositiveInfinity));
            Lay(ch, w, (int)Math.Ceiling(ch.DesiredSize.Height), $"{Report.Wrap} {tag} {w}px", dir, Report.Wrap, tag, w.ToString());
        }
    }

    static readonly int[] CodeWidths = [360, 900];

    static PropertyInfo Who(string n) => typeof(Copy.Who).GetProperty(n)!;
    static PropertyInfo Det(string n) => typeof(Copy.Detail).GetProperty(n)!;
    static PropertyInfo Dia(string n) => typeof(Copy.Diag).GetProperty(n)!;

    /// <summary>Labels from app-level shared templates, per tab: they must follow a language switch like the window's own labels.</summary>
    static readonly Dictionary<Tab, PropertyInfo[]> SharedOn = new()
    {
        [Tab.Requests] = [Who(nameof(Copy.Who.Scope)), Det(nameof(Copy.Detail.Cwd))],
        [Tab.Access] = [Who(nameof(Copy.Who.Scope)), Dia(nameof(Copy.Diag.Head))],
        [Tab.Tasks] = [Det(nameof(Copy.Detail.Cwd)), Dia(nameof(Copy.Diag.Head))],
    };

    /// <summary>Reads the text actually laid out, not the Copy property: the current language must be shown and the other absent.</summary>
    static void Shared(Border host, string what, PropertyInfo[] props)
    {
        var all = Texts(host).ToList();
        var keep = L.Current;
        foreach (var p in props)
        {
            var mine = Read(p, keep); var theirs = Read(p, keep == Lang.Zh ? Lang.En : Lang.Zh);
            L.Current = keep;
            bool has = all.Any(t => t.StartsWith(mine)), stale = all.Any(t => t.StartsWith(theirs));
            Note(has && !stale, Report.Keys, $"{what} {p.DeclaringType?.Name}.{p.Name}: \"{mine}\" shown={has}, \"{theirs}\" shown={stale}");
        }
    }

    static IEnumerable<string> Texts(DependencyObject d)
    {
        if (d is TextBlock tb) yield return tb.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            foreach (var c in Texts(VisualTreeHelper.GetChild(d, i))) yield return c;
    }

    static void Lay(Border host, int w, int h, string what, string? dir, string kind, string tag, string size)
    {
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        var cut = Clipped(host).ToList();
        Note(cut.Count == 0, Report.Clip, cut.Count == 0 ? what : $"{what}: {string.Join(" | ", cut.Take(5))}");
        if (dir is null) return;
        try
        {
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(host);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            var file = Path.Combine(dir, string.Format(Report.Name, kind, tag, size, Names.PngExt));
            using (var f = File.Create(file)) png.Save(f);
            Note(true, Report.Render, Path.GetFileName(file));
        }
        catch (Exception e) { Note(false, Report.Render, $"{what}: {e.Message}"); }
    }

    /// <summary>Text cut off by layout without an ellipsis: what a reader would lose silently.</summary>
    static IEnumerable<string> Clipped(DependencyObject d)
    {
        if (d is TextBlock tb && tb.ActualWidth > 0 && tb.TextTrimming == TextTrimming.None
            && LayoutInformation.GetLayoutClip(tb) is { } g && !g.Bounds.IsEmpty && g.Bounds.Width + 0.5 < tb.DesiredSize.Width)
            yield return tb.Text.Length > 40 ? tb.Text[..40] : tb.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            foreach (var c in Clipped(VisualTreeHelper.GetChild(d, i))) yield return c;
    }
}
