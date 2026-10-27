using System.IO;
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
    MainVm? vm;
    MainWindow? window;
    TrayHost? tray;
    Mutex? single;
    EventWaitHandle? showSignal;

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
        // Keep the tray (and an owned broker's lifetime) intact on a UI fault, and say so instead of failing silently.
        DispatcherUnhandledException += (_, x) => { x.Handled = true; vm?.Fault(x.Exception); };
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
        vm = new MainVm(monitor, args.BackendDir);
        tray = new TrayHost(vm, Show, Quit);
        L.Changed += () => Dispatcher.BeginInvoke(Relang);
        monitor.Start();
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
        window = new MainWindow(vm);
        window.Take(old);
        old.Retire();
        if (open) window.Present(vm.Tab);
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
        window ??= new MainWindow(vm);
        window.Present(tab);
    }

    void Quit()
    {
        if (monitor is null || vm is null) return;
        if (!ExitPrompt.Confirm(monitor.Owns, vm.RunningCount)) return;
        Quitting = true;
        tray?.Dispose();
        window?.Close();
        monitor.Dispose();
        monitor.StopOwned();
        showSignal?.Dispose();
        single?.ReleaseMutex();
        Shutdown();
    }
}
