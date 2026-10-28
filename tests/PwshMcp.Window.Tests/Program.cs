using System.Windows;
using System.Runtime.InteropServices;
using PwshMcp.Tray;

internal static class Program
{
    const int Cbt = 5, Create = 3;
    delegate nint Hook(int code, nint w, nint l);
    [DllImport("user32.dll")] static extern nint SetWindowsHookEx(int type, Hook hook, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nint w, nint l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [STAThread]
    static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PwshMcp.Tray;component/Styles.xaml") });
        Theme.Apply(app.Resources);
        using var monitor = new PwshMcp.Tray.Monitor(null, _ => { });
        using var coord = new Coord(_ => { }, (_, _) => throw new InvalidOperationException("Unexpected backend call"));
        var vm = new MainVm(monitor, coord);
        var window = new MainWindow(vm)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000, Top = -10000
        };
        try
        {
            bool entered = false;
            Hook callback = (code, w, l) =>
            {
                if (code == Create && !entered) { entered = true; window.Present(); }
                return CallNextHookEx(0, code, w, l);
            };
            var hook = SetWindowsHookEx(Cbt, callback, 0, GetCurrentThreadId());
            if (hook == 0) throw new InvalidOperationException("Could not install native creation hook");
            // Native window creation can pump another tray/second-launch activation before Show finishes.
            window.SourceInitialized += (_, _) => window.Present();
            try { window.Present(); }
            finally { UnhookWindowsHookEx(hook); GC.KeepAlive(callback); }
            if (!entered) throw new InvalidOperationException("Native reentrant activation did not run");
            window.Hide();
            window.Present();
            window.Close();
            window.Present();
            if (!window.IsVisible) throw new InvalidOperationException("Main window did not reopen");
            window.Retire();
            Console.WriteLine("PASS: native window creation, reentrant activation, hide/show and close/reopen");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
        finally { app.Shutdown(); }
    }
}
