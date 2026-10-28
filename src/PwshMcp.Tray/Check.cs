using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
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
        finally { L.Current = keep; if (Application.Current is { } app) Theme.Fonts(app.Resources); }
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
        var csv = Find<ScrollViewer>(View(LongLine, 260).Box, _ => true);
        Note(csv?.Style == Application.Current.FindResource(K.Scroll), Report.Scroll, "code view scrolls in the shared stable-gutter style");
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

        // Fake admin transport: records what would be sent and answers from `reply`. No pipe is opened and Start() is never called.
        var sent = new List<AdminRequest>();
        var reply = Task.FromResult(new AdminResponse { Ready = true });
        MainVm? vm = null;
        var coord = new Coord(g => vm?.Apply(g), (r, _) => { sent.Add(r); return reply; });
        vm = new MainVm(new Monitor(null, _ => { }), coord);
        vm.Seed(new GateInfo(Gate.Ready, "", Now));
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
        Ended(vm, main, host, tag, dir);
        Scrolls(vm, main, host, tag, dir);
        Inline(vm, main, host, sent, r => reply = r, tag, dir);
        Repairs(tag, dir);

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

    static readonly int[] CodeWidths = [360, 900], GateWidths = [720, 1000];

    static readonly (string Name, AccessState[] States)[] Lists =
    [
        (Report.Mixed, [AccessState.Revoked, AccessState.Active, AccessState.Expired, AccessState.Denied, AccessState.Active]),
        (Report.EndedOnly, [AccessState.Expired, AccessState.Revoked]),
        (Report.ActiveOnly, [AccessState.Active, AccessState.Active]),
    ];

    /// <summary>
    /// The Ended heading is a group header: exactly one when any grant has ended, never inside a row (card, hover, selection),
    /// with every live row above it and every ended row below it, and selection still reaching ended rows.
    /// </summary>
    static void Ended(MainVm vm, MainWindow main, Border host, string tag, string? dir)
    {
        var list = main.accList;
        foreach (var (name, states) in Lists)
        {
            var what = $"{Report.Ended} {tag} {name}";
            vm.Seed(new Status(Conn.Online, new Snapshot { Grants = [.. states.Select((s, i) => Grant($"grant-{name}-{i}", s, $"任务 {i} / Task {i}"))], Jobs = [] }, Now, "", true, true), Tab.Access);
            var end = vm.Access.FirstOrDefault(a => a.IsEnded);
            vm.SelAcc = end ?? vm.Access.FirstOrDefault();
            main.detail.BeginAnimation(UIElement.OpacityProperty, null);
            main.rise.BeginAnimation(TranslateTransform.YProperty, null);
            foreach (var w in GateWidths) Lay(host, w, MainHeight, $"{what} {w}px", dir, Report.Ended, tag, $"{name}-{w}");

            var heads = Heads(list).ToList();
            var rows = Rows(list).ToList();
            bool any = states.Any(s => s != AccessState.Active);
            Note(heads.Count == (any ? 1 : 0), Report.Ended, $"{what} one heading only when something ended ({heads.Count})");
            Note(heads.All(h => Up<ListBoxItem>(h) is null && Up<GroupItem>(h) is { Focusable: false } && !h.IsHitTestVisible && !h.Focusable),
                Report.Ended, $"{what} heading sits on the group, outside every row; not focusable, not hit-testable");
            Note(rows.Count == vm.Access.Count && rows.Select(r => r.DataContext).SequenceEqual(vm.Access), Report.Ended, $"{what} every grant is one selectable row, source order kept ({rows.Count})");
            double y(FrameworkElement e) => e.TransformToAncestor(list).Transform(new Point()).Y;
            var ys = rows.Select(y).ToList();
            var cut = heads.Count == 1 ? y(heads[0]) + heads[0].ActualHeight : double.NaN;
            Note(ys.Zip(ys.Skip(1)).All(p => p.First < p.Second)
                 && (heads.Count == 0 || rows.All(r => ((GrantVm)r.DataContext).IsEnded == (y(r) >= cut))),
                Report.Ended, $"{what} live rows above the heading, all ended rows below it");
            var pick = (ListBoxItem?)list.ItemContainerGenerator.ContainerFromItem(vm.SelAcc!);
            Note(pick is { IsSelected: true } && list.SelectedItem == vm.SelAcc && rows.Count(r => r.IsSelected) == 1 && Heads(pick).Count() == 0,
                Report.Ended, $"{what} selecting {(end is null ? "a live" : "an ended")} grant selects exactly its row; focus can reach it by container");
            if (any) Shared(host, what, [typeof(Copy.Tabs).GetProperty(nameof(Copy.Tabs.Ended))!]);
        }
    }

    /// <summary>Gutter = K.Scroll bar width; Ring = how far pm.focusVisual draws outside a row; RowWidth = sidebar row width before 0.4.0's scroll pass, bar hidden.</summary>
    const double Gutter = 10, Ring = 3, RowWidth = 310;

    static Snapshot Long() => new()
    {
        Grants = [.. Enumerable.Range(0, 24).Select(i => Grant($"grant-wait-{i:00}", AccessState.Pending, $"待批准任务 {i} / Pending task {i}")),
                  .. Enumerable.Range(0, 30).Select(i => Grant($"grant-acc-{i:00}", i % 3 == 0 ? AccessState.Expired : AccessState.Active, $"授权任务 {i} / Access task {i}"))],
        Jobs = [.. Enumerable.Range(0, 40).Select(i => Job($"job-{i:00}", i % 2 == 0 ? JobState.Succeeded : JobState.Running, null, Spec($"任务描述 {i} / Task description {i}", Samples[1])))],
        ElevatedBroker = true,
    };

    static Rect Box(FrameworkElement e, Visual to) => e.TransformToVisual(to).TransformBounds(new Rect(e.RenderSize));

    /// <summary>
    /// Overflowing sidebar lists and detail panes: styled 10px bar in a stable gutter, rows the same size with and without overflow,
    /// first/last rows (selected) whole with room for the focus ring, edge rules only where content continues, footer never covered.
    /// </summary>
    static void Scrolls(MainVm vm, MainWindow main, Border host, string tag, string? dir)
    {
        var lists = new Dictionary<Tab, ListBox> { [Tab.Requests] = main.reqList, [Tab.Access] = main.accList, [Tab.Tasks] = main.taskList };
        void Settle() { main.detail.BeginAnimation(UIElement.OpacityProperty, null); main.rise.BeginAnimation(TranslateTransform.YProperty, null); }
        void Pick(Tab t, bool first)
        {
            if (t == Tab.Requests) vm.SelReq = first ? vm.Requests.First() : vm.Requests.Last();
            else if (t == Tab.Access) vm.SelAcc = first ? vm.Access.First() : vm.Access.Last();
            else vm.SelTask = first ? vm.Tasks.First() : vm.Tasks.Last();
            Settle();
        }

        // At rest (short lists, bar hidden): the reference row size each overflowing list must keep.
        var rest = new Dictionary<Tab, Size>();
        foreach (var (t, list) in lists)
        {
            vm.Seed(new Status(Conn.Online, Snap(), Now, "", true, true), t);
            Pick(t, true);
            Lay(host, MainWidths[1], MainHeight, $"{Report.Scroll} {tag} {t} short", null, Report.Scroll, tag, "");
            var sv = Find<ScrollViewer>(list, _ => true)!;
            rest[t] = Rows(list).First().RenderSize;
            Info(Report.Scroll, $"{t} short row x {Box(Rows(list).First(), host).Left:0} gi {(Up<GroupItem>(Rows(list).First()) is { } gi ? Box(gi, host).Left + "/" + gi.ActualWidth : "-")}");
            Note(sv.ScrollableHeight == 0 && Math.Abs(rest[t].Width - RowWidth) < 0.5, Report.Scroll,
                $"{Report.Scroll} {tag} {t} short list: no overflow, row {rest[t].Width:0}×{rest[t].Height:0.#} (pre-change width {RowWidth})");
        }

        var tabs = (FrameworkElement)VisualTreeHelper.GetParent(Find<UniformGrid>(host, g => g.Columns == 3 && g.Children.OfType<RadioButton>().Any())!);
        var style = (Style)Application.Current.FindResource(K.Scroll);
        foreach (var (t, list) in lists)
        {
            vm.Seed(new Status(Conn.Online, Long(), Now, "", true, true), t);
            foreach (var w in MainWidths)
                foreach (var top in new[] { true, false })
                {
                    var edge = top ? Report.Top : Report.Bottom;
                    var what = $"{Report.Scroll} {tag} {t} {edge} {w}px";
                    Pick(t, top);
                    Lay(host, w, MainHeight, what, null, Report.Scroll, tag, "");
                    var sv = Find<ScrollViewer>(list, _ => true)!;
                    if (top) sv.ScrollToTop(); else sv.ScrollToEnd();
                    host.UpdateLayout(); host.UpdateLayout();
                    var scp = Find<ScrollContentPresenter>(sv, _ => true)!;
                    var bar = Find<ScrollBar>(sv, b => b.Orientation == Orientation.Vertical && Up<ScrollViewer>(b) == sv)!;
                    var view = new Rect(scp.RenderSize);
                    var seen = Rows(list).Where(r => Shown(r) && Box(r, scp).IntersectsWith(view)).ToList();
                    var pick = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
                    var rb = Box(pick, scp);
                    var rules = All<Rectangle>(sv).Where(r => Up<ScrollContentPresenter>(r) is null && r.Height == 1).ToList();
                    var shown = rules.Select(r => r.VerticalAlignment == VerticalAlignment.Top ? (Edge: Report.Top, On: r.Visibility == Visibility.Visible) : (Edge: Report.Bottom, On: r.Visibility == Visibility.Visible)).ToList();
                    double tx = Box(tabs, host).Left, rx = Box(seen[0], host).Left;

                    Note(sv.ScrollableHeight > 0 && sv.Style == style && bar.Visibility == Visibility.Visible && Math.Abs(bar.ActualWidth - Gutter) < 0.5,
                        Report.Scroll, $"{what} overflows; themed bar shown in the {Gutter}px gutter ({bar.ActualWidth:0}px)");
                    Note(seen.All(r => Math.Abs(r.RenderSize.Width - rest[t].Width) < 0.5),
                        Report.Scroll, $"{what} rows keep their at-rest width {rest[t].Width:0} (no jump when the bar appears): {string.Join(",", seen.Select(r => r.RenderSize.Width).Distinct())}");
                    Note(Math.Abs(seen[0].RenderSize.Height - rest[t].Height) < 0.5, Report.Scroll, $"{what} row height kept ({seen[0].RenderSize.Height:0.#} vs {rest[t].Height:0.#})");
                    Note(Math.Abs(rx - tx) < 1.5, Report.Scroll, $"{what} rows start under the tab strip (row x {rx:0}, tabs x {tx:0})");
                    Note(seen.All(r => Box(r, scp) is var b && b.Left - Ring >= 0 && b.Right + Ring <= view.Width) && Box(bar, scp).Left >= rb.Right + Ring,
                        Report.Scroll, $"{what} focus ring room left/right of every row; bar clear of rows");
                    Note(pick.IsSelected && rb.Top - Ring >= 0 && rb.Bottom + Ring <= view.Height, Report.Scroll,
                        $"{what} selected {edge} row whole, focus ring inside the viewport ({rb.Top:0}..{rb.Bottom:0} of {view.Height:0})");
                    Note(rules.Count == 2 && shown.All(s => s.On == (s.Edge == Report.Top ? !top : top)), Report.Scroll,
                        $"{what} edge rules only where content continues ({string.Join(",", shown.Select(s => $"{s.Edge}={s.On}"))})");
                    Info(Report.Scroll, $"{what} row {rest[t].Width:0}×{rest[t].Height:0.#}, viewport {view.Width:0}×{view.Height:0}, bar {bar.ActualWidth:0}px");
                    if (dir is not null) Lay(host, w, MainHeight, what, dir, Report.Scroll, tag, $"{t}-{edge}-{w}");

                    // Detail pane beside it: same gutter, content scrolled to its end clears the footer rule.
                    var dsv = Find<ScrollViewer>(main.detail, s => s.Style == style)!;
                    var dscp = Find<ScrollContentPresenter>(dsv, _ => true)!;
                    var dbar = Find<ScrollBar>(dsv, b => b.Orientation == Orientation.Vertical && Up<ScrollViewer>(b) == dsv)!;
                    var foot = ((Grid)VisualTreeHelper.GetParent(dsv)).Children.OfType<Border>().First(b => Grid.GetRow(b) == 1);
                    if (top) dsv.ScrollToTop(); else dsv.ScrollToEnd();
                    host.UpdateLayout(); host.UpdateLayout();
                    var body = (FrameworkElement)dsv.Content;
                    var bb = Box(body, dscp);
                    var dwhat = $"{Report.Detail} {tag} {t} {edge} {w}px";
                    Note(Math.Abs(dbar.ActualWidth - Gutter) < 0.5 && Math.Abs(dscp.ActualWidth + Gutter - dsv.ActualWidth) < 0.5 && dbar.Visibility != Visibility.Collapsed,
                        Report.Scroll, $"{dwhat} gutter reserved ({dbar.Visibility}), content {dscp.ActualWidth:0} of {dsv.ActualWidth:0}");
                    Note(Box(dbar, host).Right <= w + 0.5 && Box(dsv, host).Bottom <= Box(foot, host).Top + 0.5 && (top || dsv.ScrollableHeight == 0 || bb.Bottom + body.Margin.Bottom <= dscp.ActualHeight + 0.5),
                        Report.Scroll, $"{dwhat} bar on screen, footer not overlapped; end of content reachable");
                    if (dir is not null && !top && dsv.ScrollableHeight > 0) Lay(host, w, MainHeight, dwhat, dir, Report.Detail, tag, $"{t}-{edge}-{w}");
                }
        }
    }

    /// <summary>Laid-out Ended headings (a collapsed header or hidden empty group measures to zero height).</summary>
    static IEnumerable<FrameworkElement> Heads(DependencyObject d) =>
        All<TextBlock>(d).Where(t => t.Text == Copy.Tabs.Ended).Select(t => (FrameworkElement)VisualTreeHelper.GetParent(t)).Where(g => g.ActualHeight > 0 && Shown(g));

    static bool Shown(DependencyObject d)
    {
        for (var p = d; p is not null; p = VisualTreeHelper.GetParent(p)) if (p is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    static IEnumerable<ListBoxItem> Rows(DependencyObject d) => All<ListBoxItem>(d);

    static IEnumerable<T> All<T>(DependencyObject d) where T : DependencyObject
    {
        if (d is T t) yield return t;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            foreach (var c in All<T>(VisualTreeHelper.GetChild(d, i))) yield return c;
    }

    static PropertyInfo Inl(string n) => typeof(Copy.Inline).GetProperty(n)!;
    static PropertyInfo Gat(string n) => typeof(Copy.Gate).GetProperty(n)!;

    /// <summary>
    /// Inline approval against the fake transport: executor online/offline, chosen lifetime sent, failures kept in place and retryable,
    /// duplicate clicks blocked while sending. Nothing here can grant anything: the transport is a local lambda.
    /// </summary>
    static void Inline(MainVm vm, MainWindow main, Border host, List<AdminRequest> sent, Action<Task<AdminResponse>> answer, string tag, string? dir)
    {
        void Fresh(GateInfo g)
        {
            vm.Seed(new Status(Conn.Online, Snap(), Now, "", true, true), Tab.Requests);
            vm.SelReq = vm.Requests.FirstOrDefault();
            vm.Seed(g);
            main.detail.BeginAnimation(UIElement.OpacityProperty, null);
            main.rise.BeginAnimation(TranslateTransform.YProperty, null);
        }
        var what = $"{Report.Inline} {tag}";

        // The popup seam: records open/closed instead of creating a popup window, so nothing is ever shown.
        var open = false;
        main.Pop = v => open = v;
        var pick = (Style)Application.Current.FindResource(K.Pick);

        // Executor online: Approve enabled; the page has no lifetime chooser of its own, only the footer link.
        Fresh(new GateInfo(Gate.Ready, "", Now));
        var req = vm.SelReq!;
        Note(req.Approve.CanExecute(null) && !req.GateBad, Report.Gate, $"{what} ready: approve enabled");
        Shared(Lay2(host, GateWidths[0], $"{what} ready", dir, Report.Gate, tag, $"{Report.Inline}-{GateWidths[0]}"), $"{what} ready", [Inl(nameof(Copy.Inline.Head))]);
        Note(Find<RadioButton>(host, r => r.Style == pick) is null && !Texts(host).Contains(Copy.Inline.Lasts), Report.Pop, $"{what} page carries no duplicate lifetime chooser");

        // Closed until the link is used; light-dismiss, placed above its anchor.
        var btn = Anchor(host);
        Note(btn is { IsEnabled: true } && btn.Visibility == Visibility.Visible && !open && main.Bound is null && !main.life.StaysOpen && main.life.Placement == PlacementMode.Top,
            Report.Pop, $"{what} closed at rest; StaysOpen=false, Placement=Top");
        Note(!main.Press(System.Windows.Input.Key.Escape, null), Report.Pop, $"{what} Esc with the popup closed is left to the window");

        // Click opens it bound to this request with the current choice checked; previews show real deadlines.
        btn!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Note(open && main.Bound == req && main.life.PlacementTarget == btn && main.lifeCard.DataContext == req && main.Checked()?.Tag is Life.SignOut,
            Report.Pop, $"{what} link click opens popup on the selected request, sign-out checked");
        Card(main, host, btn, req, $"{what} popup", dir, tag);

        // Clicking a choice updates the footer label and closes; Approve still sends that choice in one click.
        Click(main, Life.FourHours);
        Lay(host, GateWidths[1], MainHeight, $"{what} picked", null, Report.Pop, tag, "");
        var four = req.Until is { } u4 && Math.Abs((u4 - DateTimeOffset.Now.AddHours(4)).TotalMinutes) < 2;
        Note(req.Life == Life.FourHours && four && !open && main.Bound is null && main.lifeCard.DataContext is null && Texts(host).Contains(req.Picked),
            Report.Pop, $"{what} choosing 4 h closes the popup and the link reads \"{req.Picked}\"");
        foreach (var w in MainWidths) Lay2(host, w, $"{what} picked {w}px", dir, Report.Pop, tag, $"picked-{w}");

        // Reopen shows the new choice; Esc closes without changing it; Enter picks the focused choice.
        btn = Anchor(host)!;
        main.Open(btn);
        Note(open && main.Checked()?.Tag is Life.FourHours, Report.Pop, $"{what} reopened popup checks the current choice");
        Note(main.Press(System.Windows.Input.Key.Escape, null) && !open && main.Bound is null && req.Life == Life.FourHours, Report.Pop, $"{what} Esc closes, choice kept");
        main.Open(btn);
        var hourBtn = Find<RadioButton>(main.lifeCard, r => r.Tag is Life.OneHour);
        Note(main.Press(System.Windows.Input.Key.Enter, hourBtn) && !open && req.Life == Life.OneHour, Report.Pop, $"{what} Enter on a focused choice picks it and closes");

        // No popup outlives its request: tab switch, deselection, and window retirement all close it.
        main.Open(btn);
        vm.Tab = Tab.Access;
        Note(!open && main.Bound is null, Report.Pop, $"{what} switching tab closes the popup");
        vm.Tab = Tab.Requests; vm.SelReq = req;
        Lay(host, GateWidths[1], MainHeight, $"{what} back", null, Report.Pop, tag, "");
        btn = Anchor(host)!;
        main.Open(btn);
        vm.SelReq = null;
        Note(!open && main.Bound is null && !main.Open(btn), Report.Pop, $"{what} deselecting closes it; a stale anchor can't reopen it");
        vm.SelReq = req;
        req.Pick(Life.SignOut);

        // Executor offline / failing checks: guidance in the same slot, Approve disabled, no fallback, no popup.
        foreach (var (g, name) in new[] { (Gate.Down, Report.Down), (Gate.Broken, Report.Broken) })
        {
            Fresh(new GateInfo(g, g == Gate.Broken ? Copy.Gate.Untrusted : Copy.Gate.NoPipe, Now));
            req = vm.SelReq!;
            Note(!req.Approve.CanExecute(null) && req.GateBad && vm.Recheck.CanExecute(null), Report.Gate, $"{what} {name}: approve disabled, recheck offered");
            foreach (var w in GateWidths)
            {
                var h = Lay2(host, w, $"{what} {name} {w}px", dir, Report.Gate, tag, $"{name}-{w}");
                Shared(h, $"{what} {name} {w}px", [Gat(nameof(Copy.Gate.Head)), Gat(nameof(Copy.Gate.Recheck))]);
            }
            var off = Anchor(host);
            Note(off is not null && off.Visibility != Visibility.Visible && !main.Open(off) && !open, Report.Pop, $"{what} {name}: lifetime link hidden, popup refused");
        }

        // Chosen lifetime travels with Approve; a refusal keeps the request waiting and retryable in place.
        Fresh(new GateInfo(Gate.Ready, "", Now));
        req = vm.SelReq!;
        Lay(host, GateWidths[1], MainHeight, $"{what} again", null, Report.Pop, tag, "");
        main.Open(Anchor(host)!);
        Click(main, Life.OneHour);
        answer(Task.FromResult(new AdminResponse { Ok = false, Error = AdminNames.Forbidden }));
        sent.Clear();
        req.Submit().GetAwaiter().GetResult();
        var last = sent.LastOrDefault();
        var hour = last?.ExpiresAt is { } e && Math.Abs((e - DateTimeOffset.UtcNow.AddHours(1)).TotalMinutes) < 2;
        Note(sent.Count == 1 && last!.Op == AdminOp.Approve && last.Id == req.Id && hour, Report.Inline, $"{what} approve after picking 1 h in the popup sends Op=Approve, Id, ExpiresAt≈1h");
        Note(req.NoticeBad && req.IsPending && req.Approve.CanExecute(null) && req.Footer.Contains(Copy.Gate.Forbidden), Report.Inline, $"{what} refusal shown in place, still retryable");
        Lay2(host, GateWidths[0], $"{what} {Report.Failed}", dir, Report.Inline, tag, $"{Report.Failed}-{GateWidths[0]}");

        answer(Task.FromResult(new AdminResponse { Ok = true, Grant = Grant(req.Id, AccessState.Pending, "x") }));
        req.Submit().GetAwaiter().GetResult();
        Note(req.NoticeBad && req.IsPending, Report.Inline, $"{what} Ok without Active grant is not reported as approved");

        req.Pick(Life.SignOut);
        sent.Clear();
        answer(Task.FromResult(new AdminResponse { Ok = true, Grant = Grant(req.Id, AccessState.Active, "x") }));
        req.Submit().GetAwaiter().GetResult();
        Note(sent.Count == 1 && sent[0].ExpiresAt is null && !req.NoticeBad, Report.Inline, $"{what} until sign-out sends no ExpiresAt; Active accepted");

        // A language switch retires the window; its popup goes with it. (Release is Retire short of a real Close.)
        Fresh(new GateInfo(Gate.Ready, "", Now));
        Lay(host, GateWidths[1], MainHeight, $"{what} retire", null, Report.Pop, tag, "");
        var was = main.Open(Anchor(host)!) && open;
        main.Release();
        Note(was && !open && main.Bound is null, Report.Pop, $"{what} retiring the window (language switch) closes the popup");

        // In flight: button turns busy; an open popup closes, can't reopen, and picks plus a second click are blocked.
        Fresh(new GateInfo(Gate.Ready, "", Now));
        req = vm.SelReq!;
        Lay(host, GateWidths[1], MainHeight, $"{what} flight", null, Report.Pop, tag, "");
        btn = Anchor(host)!;
        main.Open(btn);
        var hold = new TaskCompletionSource<AdminResponse>();
        answer(hold.Task);
        sent.Clear();
        _ = req.Submit();
        Note(!open && main.Bound is null && !btn.IsEnabled && !main.Open(btn), Report.Pop, $"{what} sending closes the popup and disables the lifetime link");
        req.Pick(Life.Today);
        _ = req.Submit();
        Note(req.Sending && !req.Approve.CanExecute(null) && !req.CanPick && req.IsSignOut && sent.Count == 1, Report.Inline, $"{what} sending blocks duplicate approve and lifetime changes");
        Lay2(host, GateWidths[0], $"{what} {Report.Sent}", dir, Report.Inline, tag, $"{Report.Sent}-{GateWidths[0]}");
    }

    /// <summary>The repair window's fixed width (it does not resize), and a narrower one for large text scaling.</summary>
    static readonly int[] FixWidths = [440, 520];
    /// <summary>Visible action order per dialog state, from the first language run; the second must match it exactly.</summary>
    static readonly Dictionary<string, string> Shapes = [];

    static PropertyInfo Fx(string n) => typeof(Copy.Fix).GetProperty(n)!;

    /// <summary>
    /// Runs continuations inline on the check's thread. The dispatcher queue is never pumped here: it holds the application's
    /// own shutdown callback (Run is never called), which pumping would execute.
    /// </summary>
    sealed class Sync : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? s) => d(s);
        public override void Send(SendOrPostCallback d, object? s) => d(s);
    }

    /// <summary>What a bound button would show once WPF re-queries it (that re-query is itself a dispatcher post).</summary>
    static bool Live(Button b) => b.Command?.CanExecute(b.CommandParameter) == true;

    static Button? Btn(DependencyObject d, string id) => Find<Button>(d, b => AutomationProperties.GetAutomationId(b) == id && Shown(b));

    /// <summary>
    /// Repair window against local fakes: opened only by clicks (header chips, Repair, the blocked card, tray menu), nothing runs from
    /// polling, busy labels, both components down at once, the shield fallback only after NeedsElevation, and no outcome but Ready
    /// worded as working. The fakes stand in for the scheduled task, the broker and RunAs, so no process, task or prompt is touched.
    /// </summary>
    static void Repairs(string tag, string? dir)
    {
        // No dispatcher loop runs here, so without a context the fakes' continuations would land on the thread pool.
        var ctx = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new Sync());
        try { Fixes(tag, dir); }
        finally { SynchronizationContext.SetSynchronizationContext(ctx); }
    }

    static void Fixes(string tag, string? dir)
    {
        var what = $"{Report.Repair} {tag}";
        MainVm? vm = null;
        var coord = new Coord(g => vm?.Apply(g), (_, _) => Task.FromResult(new AdminResponse { Ready = true }));
        vm = new MainVm(new Monitor(null, _ => { }), coord);
        var main = new MainWindow(vm);
        var shown = 0;
        main.ShowFix = _ => shown++;
        var host = Detach(main, K.Ground);
        var fix = vm.Fix;

        List<bool> bc = [], ec = [];
        Task<RepairResult> ba = Task.FromResult(new RepairResult(RepairState.Ready)), ea = ba;
        var owns = false;
        fix.Seed(b => { bc.Add(b); return ba; }, e => { ec.Add(e); return ea; }, () => owns);
        void Both() { vm.Seed(new Status(Conn.Offline, new Snapshot(), Now, Copy.Gate.NoPipe, false, false), Tab.Requests); vm.Seed(new GateInfo(Gate.Down, Copy.Gate.NoPipe, Now)); }

        // Both down, nothing selected, as polling would leave it: no attempt is made by itself.
        for (int i = 0; i < 3; i++) Both();
        vm.SelReq = null;
        Note(bc.Count == 0 && ec.Count == 0 && shown == 0 && main.Fix is null, Report.Repair, $"{what} polling both-down state starts nothing and opens nothing");
        Note(fix.Worst == Part.Conn && fix.Reconnect.CanExecute(null) && fix.Start.CanExecute(null) && !fix.Elevate.CanExecute(null) && !fix.ShowElev
             && !fix.Owns && !fix.Restart.Click.CanExecute(null), Report.Both, $"{what} both down: reconnect and start offered, no shield, no restart when the broker is not ours");
        // Both header chips are whole buttons at every width: they are the header's way into repair.
        void Chips(string state)
        {
            foreach (var w in GateWidths)
            {
                Lay(host, w, MainHeight, $"{what} {state} main {w}px", dir, Report.Repair, tag, $"{state}-main-{w}");
                var cb = Btn(host, Names.ConnBtn); var gb = Btn(host, Names.GateBtn);
                var lane = cb is null ? null : Up<DockPanel>(cb);
                Note(cb is not null && gb is not null && lane is not null && Box(gb, host).Right <= Box(lane, host).Right + 0.5 && Box(cb, host).Right <= Box(gb, host).Left + 0.5,
                    Report.Clip, $"{what} {state} {w}px: both status chips whole (executor chip ends {(gb is null ? 0 : Box(gb, host).Right):0}, lane ends {(lane is null ? 0 : Box(lane, host).Right):0})");
            }
        }
        Chips(Report.Both);

        // Every entry point opens the same window, aimed at the right part, without running anything.
        Btn(host, Names.ConnBtn)?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var win = main.Fix;
        Note(win is not null && shown == 1 && win.Cued == Part.Conn, Report.Repair, $"{what} connection chip (no request selected, offline) opens on the connection");
        Btn(host, Names.GateBtn)?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Note(ReferenceEquals(main.Fix, win) && shown == 1 && win!.Cued == Part.Exec, Report.Repair, $"{what} executor chip re-aims the open window at the executor");
        fix.Ask(fix.Worst);
        Note(win!.Cued == Part.Conn && bc.Count == 0 && ec.Count == 0, Report.Repair, $"{what} tray menu path opens on the worst part (connection); opening ran nothing");

        // The request's executor-down block links to the same window.
        vm.Seed(new Status(Conn.Online, Snap(), Now, "", true, true), Tab.Requests);
        vm.SelReq = vm.Requests.FirstOrDefault();
        vm.Seed(new GateInfo(Gate.Down, Copy.Gate.NoPipe, Now));
        main.detail.BeginAnimation(UIElement.OpacityProperty, null);
        main.rise.BeginAnimation(TranslateTransform.YProperty, null);
        Chips(Report.Down);
        Lay(host, GateWidths[0], MainHeight, $"{what} blocked", null, Report.Repair, tag, "");
        win.Aim(Part.Conn);
        var link = Btn(host, Names.FixExec);
        link?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Note(link is not null && win.Cued == Part.Exec && ReferenceEquals(main.Fix, win) && fix.Worst == Part.Exec && !fix.Reconnect.CanExecute(null),
            Report.Repair, $"{what} blocked request's Fix executor opens the executor part; reconnect idle while online");

        var dh = Detach(win, K.Panel);
        void Clean() { win.Box(Part.Conn).Background = null; win.Box(Part.Exec).Background = null; }
        int Fit(int w) { dh.Measure(new Size(w, double.PositiveInfinity)); return (int)Math.Ceiling(dh.DesiredSize.Height); }
        void Dialog(string state, PropertyInfo[] copy)
        {
            Clean();
            foreach (var w in FixWidths)
            {
                var h = Fit(w);
                Lay(dh, w, h, $"{what} {Report.Dialog} {state} {w}px", dir, Report.Dialog, tag, $"{state}-{w}");
                // Height only at the real (fixed) width; the narrow pass is a clipping check for large text.
                if (w == FixWidths[^1]) Note(h <= MainHeight, Report.Dialog, $"{what} {Report.Dialog} {state} {w}px fits a {MainHeight}px screen ({h}px)");
            }
            Shared(dh, $"{what} {Report.Dialog} {state}", copy);
            // The shield means "Windows will ask": only the explicit fallback may show one, and only while it is offered.
            var shield = Application.Current.FindResource(K.Shield);
            var shields = All<System.Windows.Shapes.Path>(dh).Where(p => p.Data == shield && Shown(p) && Up<Button>(p) is not null).Select(p => Up<Button>(p)!.Name).ToList();
            Note(shields.All(n => n == win.elevate.Name) && shields.Count == (fix.ShowElev && !fix.Elevating ? 1 : 0), Report.Elev,
                $"{what} {Report.Dialog} {state}: shield only on the elevated fallback ({string.Join(",", shields)})");
            var order = string.Join(",", All<Button>(dh).Where(Shown).OrderBy(b => Box(b, dh).Top).ThenBy(b => Box(b, dh).Left).Select(b => Fmt.Or(b.Name, "close")));
            if (Shapes.TryGetValue(state, out var was)) Note(was == order, Report.Dialog, $"{what} {Report.Dialog} {state}: same actions in the same place as the other language ({order})");
            else Shapes[state] = order;
            // Over the main window at the required widths: the dialog's own size never depends on the main window.
            foreach (var w in GateWidths)
            {
                Lay(host, w, MainHeight, $"{what} {state} under {w}px", null, Report.Dialog, tag, "");
                var dw = FixWidths[1]; var h = Fit(dw);
                Lay(dh, dw, h, $"{what} {Report.Dialog} {state} over {w}px", null, Report.Dialog, tag, "");
                Note(dw <= w && h <= MainHeight, Report.Dialog, $"{what} {Report.Dialog} {state} centred over the {w}px window");
                if (dir is null) continue;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), null, new Rect(0, 0, w, MainHeight));
                    dc.DrawRectangle(new VisualBrush(dh) { Stretch = Stretch.None }, null, new Rect((w - dw) / 2.0, Math.Max(0, (MainHeight - h) / 2.0), dw, h));
                }
                Save([host, dv], w, MainHeight, Path.Combine(dir, string.Format(Report.Name, Report.Dialog, tag, $"{state}-over-{w}", Names.PngExt)));
            }
        }

        // Both down in the dialog.
        Both();
        Dialog(Report.Both, [Fx(nameof(Copy.Fix.ConnHead)), Fx(nameof(Copy.Fix.ExecHead)), Dia(nameof(Copy.Diag.Head))]);
        Note(win.reconnect.Visibility == Visibility.Visible && win.restart.Visibility != Visibility.Visible && Live(win.start) && Live(win.reconnect) &&win.elevCard.Visibility != Visibility.Visible,
            Report.Both, $"{what} dialog both down: reconnect + start, restart hidden (not owned), no shield card");

        // Busy: held fakes; each part busy on its own, a second click is refused.
        var hold = new TaskCompletionSource<RepairResult>();
        ea = hold.Task;
        fix.Start.Execute(null);
        fix.Start.Execute(null);
        Note(ec.Count == 1 && !ec[0] && fix.Starting && fix.StartText == Copy.Fix.Starting && !fix.Start.CanExecute(null) && fix.Reconnect.CanExecute(null) && !fix.ShowElev,
            Report.Busy, $"{what} starting: \"{fix.StartText}\", one scheduled-task attempt, connection still repairable");
        var bh = new TaskCompletionSource<RepairResult>();
        ba = bh.Task;
        fix.Reconnect.Execute(null);
        Note(bc.Count == 1 && !bc[0] && fix.ConnRunning && fix.ReconnectText == Copy.Fix.Reconnecting && !fix.Reconnect.CanExecute(null), Report.Busy, $"{what} reconnecting: \"{fix.ReconnectText}\", disabled while working");
        Dialog(Report.Busy, [Fx(nameof(Copy.Fix.ConnHead)), Fx(nameof(Copy.Fix.ExecHead))]);
        Note(!Live(win.start) && !Live(win.reconnect) && win.start.Visibility == Visibility.Visible
             && new[] { Copy.Fix.Starting, Copy.Fix.Reconnecting }.All(s => Texts(dh).Contains(s.Replace("_", ""))),
            Report.Busy, $"{what} dialog: busy labels shown in place, both buttons bound to refusing commands");

        // Task missing or refused: the shield fallback appears, but nothing elevated runs until its own click.
        hold.SetResult(new RepairResult(RepairState.NeedsElevation, "task missing"));
        bh.SetResult(new RepairResult(RepairState.Failed, "pipe busy"));
        Note(!fix.Exec.Busy && fix.ShowElev && fix.Elevate.CanExecute(null) && ec.Count == 1 && fix.Exec.Note == Copy.Fix.NeedsElev && fix.StartText == Copy.Fix.Start,
            Report.Elev, $"{what} NeedsElevation shows the shield fallback without invoking it");
        Note(!fix.Conn.Busy && fix.Conn.Note == Copy.Fix.Failed && fix.Conn.Tone == Tone.Bad && fix.Diag.Any(d => d.Value == "Failed: pipe busy"),
            Report.Repair, $"{what} failed reconnect: plain result, backend detail only in diagnostics");
        Dialog(Report.Elev, [Fx(nameof(Copy.Fix.ElevHead)), Fx(nameof(Copy.Fix.ExecHead)), Dia(nameof(Copy.Diag.Head))]);
        Note(win.elevCard.Visibility == Visibility.Visible && Live(win.elevate), Report.Elev, $"{what} dialog shows the shield card");

        // Elevated run only from its button; outcomes other than Ready are never worded as working.
        foreach (var (s, text) in new[] { (RepairState.Cancelled, Copy.Fix.Cancelled), (RepairState.Started, Copy.Fix.Started), (RepairState.Failed, Copy.Fix.Failed) })
        {
            ea = Task.FromResult(new RepairResult(s));
            fix.Elevate.Execute(null);
                Note(ec.Last() && fix.Exec.Note == text && text != Copy.Fix.ExecReady && fix.ShowElev, Report.Elev, $"{what} elevated {s}: \"{text}\", fallback kept");
        }
        ea = Task.FromResult(new RepairResult(RepairState.Started));
        fix.Start.Execute(null);
        Note(!ec.Last() && fix.Exec.Note == Copy.Fix.Started && fix.Exec.Tone == Tone.Wait, Report.Repair, $"{what} started-but-unprobed is not called ready");
        ea = Task.FromResult(new RepairResult(RepairState.Ready));
        fix.Start.Execute(null);
        Note(fix.Exec.Note == Copy.Fix.ExecReady && fix.Exec.Tone == Tone.On && !fix.ShowElev && ec.Count(e => e) == 3, Report.Repair, $"{what} probe-ready result; shield fallback cleared; elevated ran exactly 3 times, all from clicks");

        // Restart only for an owned broker, behind a second click.
        owns = true;
        vm.Seed(new Status(Conn.Online, Snap(), Now, "", true, true), Tab.Requests);
        bc.Clear();
        ba = Task.FromResult(new RepairResult(RepairState.NotOwned));
        fix.Restart.Click.Execute(null);
        Note(bc.Count == 0 && fix.Restart.Tag == Names.Armed && fix.RestartText == Copy.Fix.ConfirmRestart, Report.Repair, $"{what} restart arms on the first click, runs nothing");
        Dialog(Report.Repair, [Fx(nameof(Copy.Fix.ConnHead)), Fx(nameof(Copy.Fix.ExecHead))]);
        Note(win.restart.Visibility == Visibility.Visible && Texts(dh).Contains(fix.RestartWarn), Report.Repair, $"{what} owned broker: restart and its warning shown");
        fix.Restart.Click.Execute(null);
        Note(bc.Count == 1 && bc[0] && fix.Conn.Note == Copy.Fix.NotOwned, Report.Repair, $"{what} confirmed restart asks to restart; NotOwned reported");

        main.Release();
        Note(main.Fix is null && fix.Reopen == false, Report.Repair, $"{what} retiring the main window takes the never-shown repair window down");
    }

    static Button? Anchor(Border host) => Find<Button>(host, b => AutomationProperties.GetAutomationId(b) == Names.LifeBtn);

    /// <summary>Clicks a popup choice the way a pointer does: the radio checks (keeping its binding), then Click fires.</summary>
    static void Click(MainWindow main, Life l)
    {
        if (Find<RadioButton>(main.lifeCard, r => r.Tag is Life x && x == l) is not { } r) return;
        r.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        r.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    /// <summary>
    /// The popup's real card, lifted out of the closed Popup for one layout pass: checked alone for clipping and copy,
    /// then composited above the footer link exactly where Placement=Top with the XAML offsets puts it. Restored afterwards.
    /// </summary>
    static void Card(MainWindow main, Border host, Button btn, GrantVm req, string what, string? dir, string tag)
    {
        var card = main.lifeCard;
        main.life.Child = null;
        var ph = Host(card, K.Panel);
        ph.Background = Brushes.Transparent;
        try
        {
            ph.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            int pw = (int)Math.Ceiling(ph.DesiredSize.Width), pht = (int)Math.Ceiling(ph.DesiredSize.Height);
            Lay(ph, pw, pht, $"{what} card", null, Report.Pop, tag, "");
            var texts = Texts(ph).ToList();
            Shared(ph, what, [Inl(nameof(Copy.Inline.Lasts)), Inl(nameof(Copy.Inline.AtSignOut))]);
            Note(new[] { req.HourEnd, req.FourEnd, req.TodayEnd, req.Applies }.All(texts.Contains), Report.Pop, $"{what} shows the 1 h / 4 h / end-of-day deadlines and the client-wide scope line");
            Note(Lives.All.All(l => Find<RadioButton>(card, r => r.Tag is Life x && x == l) is not null), Report.Pop, $"{what} offers all four lifetimes");
            foreach (var w in MainWidths)
            {
                Lay(host, w, MainHeight, $"{what} host {w}px", null, Report.Pop, tag, "");
                var at = btn.TransformToAncestor(host).Transform(new Point());
                double x = at.X + main.life.HorizontalOffset, y = at.Y + main.life.VerticalOffset - pht;
                Note(x >= 0 && y >= 0 && x + pw <= w && at.Y + btn.ActualHeight <= MainHeight, Report.Pop,
                    $"{what} fits inside the window above its link at {w}px (card {x:0},{y:0} {pw}×{pht}; link bottom {at.Y + btn.ActualHeight:0} of {host.ActualHeight:0})");
                if (dir is null) continue;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(ph) { Stretch = Stretch.None }, null, new Rect(x, y, pw, pht));
                Save([host, dv], w, MainHeight, Path.Combine(dir, string.Format(Report.Name, Report.Pop, tag, w, Names.PngExt)));
            }
        }
        finally { ph.Child = null; main.life.Child = card; }
    }

    static void Save(Visual[] vs, int w, int h, string file)
    {
        try
        {
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            foreach (var v in vs) bmp.Render(v);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(file)) png.Save(f);
            Note(true, Report.Render, Path.GetFileName(file));
        }
        catch (Exception e) { Note(false, Report.Render, $"{file}: {e.Message}"); }
    }

    static T? Find<T>(DependencyObject d, Func<T, bool> ok) where T : DependencyObject
    {
        if (d is T t && ok(t)) return t;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (Find(VisualTreeHelper.GetChild(d, i), ok) is { } hit) return hit;
        return null;
    }

    static T? Up<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p)) if (p is T t) return t;
        return null;
    }

    static Border Lay2(Border host, int w, string what, string? dir, string kind, string tag, string size)
    {
        Lay(host, w, MainHeight, what, dir, kind, tag, size);
        return host;
    }

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
        if (d is TextBlock tb) yield return (tb.Inlines.Count > 0 ? Flat(tb.Inlines) : tb.Text).TrimStart();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            foreach (var c in Texts(VisualTreeHelper.GetChild(d, i))) yield return c;
    }

    /// <summary>Text of the Run/Span inlines as laid out; TextBlock.Text alone misses labels composed of Runs.</summary>
    static string Flat(IEnumerable<Inline> xs) => string.Concat(xs.Select(x => x switch
    {
        Run r => r.Text,
        Span s => Flat(s.Inlines),
        LineBreak => "\n",
        _ => "",
    }));

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
