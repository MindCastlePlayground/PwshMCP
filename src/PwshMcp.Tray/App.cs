using System.IO;
using System.Runtime;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

public sealed class App : Application
{
    public static bool Quitting { get; private set; }
    public System.Windows.Media.ImageSource? Icon { get; private set; }

    readonly Args args;
    Monitor? monitor;
    Coord? coord;
    MainVm? vm;
    MainWindow? window;
    TrayHost? tray;
    Reclaim? reclaim;
    (Rect Box, bool Max) spot = (Rect.Empty, false);
    Mutex? single;
    EventWaitHandle? showSignal;
    readonly FaultGuard faults = new(Timing.FaultWindow, Timing.MaxFaults);
    bool recovering, recovered;

    App(Args args) { this.args = args; ShutdownMode = ShutdownMode.OnExplicitShutdown; }

    [STAThread]
    public static int Main(string[] argv)
    {
        var a = Args.Parse(argv);
        Launch.Backend = a.BackendDir;
        if (a.Mode == Mode.Export)
        {
            Glyphs.Export(a.Dir ?? Path.Combine(AppContext.BaseDirectory, Names.AssetDir));
            return 0;
        }
        // --language wins for this run only (the approval window inherits it); otherwise the saved choice, then the OS language.
        L.Current = a.Lang ?? Prefs.Load() ?? L.System;
        System.Windows.Forms.Application.EnableVisualStyles();
        var app = new App(a);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Names.Styles, UriKind.Absolute) });
        Theme.Apply(app.Resources);
        // Offline checks: no tray, no broker, no UAC, no window shown. Exit code is the number of failures.
        if (a.Mode is Mode.SelfTest or Mode.Render) return Check.Run(a.Dir, a.Mode == Mode.Render);
        // Window/taskbar icon drawn from the same vector source as the tray glyph.
        using (var ico = new MemoryStream(Glyphs.AppIco()))
            app.Icon = BitmapFrame.Create(ico, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => { if (s is Window w && w.Icon is null) w.Icon = app.Icon; }));
        return app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Keep the tray (and an owned broker's lifetime) intact on a UI fault, and break layout fault storms before they freeze the UI thread.
        DispatcherUnhandledException += (_, x) => HandleFault(x);
        switch (args.Mode)
        {
            case Mode.Approve: case Mode.Resume: StartApprove(); break;
            default: StartTray(); break;
        }
    }

    void StartApprove()
    {
        var w = new ApproveWindow(new ApproveVm(args.Id, args.BackendDir, args.Mode == Mode.Resume));
        w.Closed += (_, _) => Shutdown();
        w.Show();
        w.Activate();
    }

    void StartTray()
    {
        single = new Mutex(true, Names.Mutex, out var first);
        if (!first)
        {
            // A second launch is explicit user intent: surface the running instance, then leave.
            try { EventWaitHandle.OpenExisting(Names.ShowEvent).Set(); } catch { }
            Shutdown(); return;
        }
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Names.ShowEvent);
        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(() => Show(null)), null, -1, false);

        monitor = new Monitor(args.BackendDir, s => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Publish(s)));
        coord = new Coord(g => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { if (!Quitting) { vm?.Apply(g); tray?.Render(); } }));
        vm = new MainVm(monitor, coord);
        // Nothing is built until it is needed: the tray runs lean until a notice or the user opens the window.
        vm.Shrink();
        reclaim = new Reclaim(Timing.Reclaim, Hold, Drop);
        tray = new TrayHost(vm, Show, Quit, Warm);
        L.Changed += () => Dispatcher.BeginInvoke(Relang);
        monitor.Start();
        coord.Start();
        if (args.Show) Show(null);
    }

    /// <summary>
    /// Applies a user-picked language. Display only: the broker, the monitor and every grant are untouched and no UAC prompt appears.
    /// An open window is rebuilt in place (same bounds, tab and selection) because XAML labels are read once at load.
    /// </summary>
    void Relang()
    {
        if (vm is null || Quitting) return;
        Theme.Fonts(Resources);
        vm.Relabel();
        tray?.Relabel();
        if (window is null) return;
        var old = window;
        var open = old.IsVisible && old.WindowState != WindowState.Minimized;
        spot = old.Spot;
        window = Make();
        old.Retire();
        if (open) window.Present(vm.Tab); else reclaim?.Arm();
    }

    MainWindow Make()
    {
        var w = new MainWindow(vm!);
        w.Take(spot);
        w.IsVisibleChanged += (_, _) => Idle(w);
        w.StateChanged += (_, _) => Idle(w);
        return w;
    }

    /// <summary>Hidden or minimized starts the teardown countdown; on screen stops it.</summary>
    void Idle(MainWindow w)
    {
        if (!ReferenceEquals(w, window) || Quitting) return;
        if (w.IsVisible && w.WindowState != WindowState.Minimized) reclaim?.Cancel(); else reclaim?.Arm();
    }

    bool Hold() => window is { } w && (w.IsVisible && w.WindowState != WindowState.Minimized || w.Fix is { IsVisible: true })
        || vm?.Busy == true || tray?.Busy == true;

    /// <summary>Tears the idle window down, keeps only its placement, and lets the view model go lean. One collection, here only.</summary>
    void Drop()
    {
        if (window is not { } w || vm is null || Quitting) return;
        spot = w.Spot;
        window = null;
        w.Retire();
        vm.Shrink();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Optimized, true, true);
    }

    /// <summary>A new notice is about to appear: have the window built and laid out (never shown) so Details opens at once.</summary>
    void Warm()
    {
        if (vm is null || Quitting || window is not null) return;
        vm.Grow();
        window = Make();
        window.Warm();
        reclaim?.Arm();
    }

    void Publish(Status s)
    {
        if (vm is null || Quitting) return;
        vm.Apply(s);
        tray?.Render();
    }

    void Show(Tab? tab)
    {
        if (vm is null || Quitting) return;
        if (recovered) { recovered = false; faults.Reset(); vm.ClearFault(); }
        reclaim?.Cancel();
        vm.Grow();
        window ??= Make();
        window.Present(tab);
    }

    void HandleFault(DispatcherUnhandledExceptionEventArgs x)
    {
        x.Handled = true;
        switch (faults.Record())
        {
            case FaultAction.Show:
                FaultLog.Write(x.Exception);
                vm?.Fault(x.Exception);
                break;
            case FaultAction.Recover:
                FaultLog.Write(x.Exception);
                RecoverFromFault();
                break;
        }
    }

    void RecoverFromFault()
    {
        if (recovering || Quitting) return;
        recovering = true;
        try
        {
            try { Theme.Fonts(Resources); } catch { }
            if (window is { } w) { spot = w.Spot; window = null; w.Retire(); }
            tray?.CloseNotices();
            recovered = true;
        }
        finally { recovering = false; }
    }

    void Quit()
    {
        if (monitor is null || vm is null) return;
        if (!ExitPrompt.Confirm(monitor.Owns, vm.RunningCount)) return;
        Quitting = true;
        reclaim?.Cancel();
        tray?.Dispose();
        window?.Close();
        coord?.Dispose();
        monitor.Dispose();
        monitor.StopOwned();
        showSignal?.Dispose();
        single?.ReleaseMutex();
        Shutdown();
    }
}

public enum FaultAction { Show, Recover, Suppress }

public sealed class FaultGuard
{
    readonly TimeSpan window;
    readonly int limit;
    readonly Func<DateTimeOffset> clock;
    readonly Queue<DateTimeOffset> times = new();

    public FaultGuard(TimeSpan window, int limit, Func<DateTimeOffset>? clock = null)
    {
        this.window = window; this.limit = limit; this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public FaultAction Record()
    {
        var now = clock();
        while (times.Count > 0 && now - times.Peek() > window) times.Dequeue();
        times.Enqueue(now);
        return times.Count == 1 ? FaultAction.Show : times.Count == limit ? FaultAction.Recover : FaultAction.Suppress;
    }

    public void Reset() => times.Clear();
}

static class FaultLog
{
    static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AdminNames.Folder, Names.LogDir, Names.FaultLog);

    public static void Write(Exception e)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var entry = $"[{DateTimeOffset.UtcNow:O}] {e}{Environment.NewLine}---{Environment.NewLine}";
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= Limits.FaultLogBytes)
                File.WriteAllText(LogPath, entry);
            else
                File.AppendAllText(LogPath, entry);
        }
        catch { }
    }
}
