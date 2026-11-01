using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using PwshMcp.Shared;
using PwshMcp.Tray;

internal static class Program
{
    const int Cbt = 5, Create = 3;
    const string Later = "later";
    delegate nint Hook(int code, nint w, nint l);
    [DllImport("user32.dll")] static extern nint SetWindowsHookEx(int type, Hook hook, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nint w, nint l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [STAThread]
    static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PwshMcp.Tray;component/Styles.xaml") });
        Theme.Apply(app.Resources);
        if (args.Contains(Inspect.Flag)) return Inspect.Run(app);
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
            window.Hide();
            var spot = window.Spot;
            if (spot.Box.IsEmpty) throw new InvalidOperationException("Shown window has no placement to remember");
            window.Retire();
            if (window.DataContext is not null) throw new InvalidOperationException("Retired window still holds its view model");

            // Prewarm: laid out without a native window, nothing shown or activated.
            vm.Shrink();
            vm.Grow();
            var warm = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
            if (!new MainWindow(vm).Spot.Box.IsEmpty) throw new InvalidOperationException("Never-shown window reported a placement");
            warm.Take(spot);
            warm.Warm();
            if (warm.IsVisible || new WindowInteropHelper(warm).Handle != 0) throw new InvalidOperationException("Prewarm created or showed a window");
            if (warm.Left != spot.Box.Left || warm.Top != spot.Box.Top || warm.Width != spot.Box.Width) throw new InvalidOperationException("Placement not carried to the recreated window");
            warm.Present();
            if (!warm.IsVisible) throw new InvalidOperationException("Recreated window did not open");
            warm.Retire();

            var grant = new GrantVm(vm, new PwshMcp.Shared.AccessInfo { Id = Later, State = PwshMcp.Shared.AccessState.Pending });
            int deferred = 0;
            var notice = new NoticeWindow(grant, 1, () => { }, () => deferred++, () => { }) { ShowActivated = false, Left = -10000, Top = -10000 };
            notice.Warm();
            if (notice.IsVisible || new WindowInteropHelper(notice).Handle != 0) throw new InvalidOperationException("Notice prewarm created a window");
            notice.Show();
            notice.Close();
            if (!notice.Released || deferred != 1) throw new InvalidOperationException("Closed notice kept its references or lost the deferral");
            Console.WriteLine("PASS: native window creation, reentrant activation, hide/show, close/reopen, retire/prewarm/recreate with placement, notice release");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
        finally { app.Shutdown(); }
    }
}

/// <summary>
/// Optional desktop acceptance fixture (--inspect): the real main window on screen over synthetic data and fake transports.
/// No pipe, broker, executor, UAC or registry is touched; Monitor and Coord are never started. Hiding or closing the window ends the process.
/// </summary>
internal static class Inspect
{
    public const string Flag = "--inspect";
    static class Keys
    {
        public const string Req = "req-inspect", Grant = "grant-inspect", Waiting = "job-waiting", Done = "job-done", Rejected = "job-rejected";
        public const string Client = "codex-cli", Name = "Codex", User = @"DESKTOP\tester", Sid = "S-1-5-21-0-0-0-1001";
        public const string Engine = "pwsh 7.4.6", Fake = "Inspect fixture only";
        public const string Ready = "INSPECT ready pid={0}";
    }

    static readonly Dictionary<string, RunSpec> runs = new()
    {
        [Keys.Waiting] = new() { Description = "List recent log files", Code = "Get-ChildItem C:\\Logs -Filter *.log |\n  Sort-Object LastWriteTime -Descending |\n  Select-Object -First 5", Cwd = "C:\\Logs", TimeoutSeconds = 120 },
        [Keys.Done] = new() { Description = "Report free disk space", Code = "Get-PSDrive -PSProvider FileSystem |\n  Select-Object Name, Free, Used", Cwd = "C:\\", Env = new() { ["PWSHMCP_INSPECT"] = null } },
        [Keys.Rejected] = new() { Description = "Restart print spooler", Code = "Restart-Service -Name Spooler -Force", Cwd = "C:\\" },
    };
    const string Output = "Name   Free(GB) Used(GB)\n----   -------- --------\nC        212.40   263.10\nD        880.02   119.98\n";

    static readonly List<AccessInfo> grants = [];
    static readonly List<JobInfo> jobs = [];
    static MainVm? vm;

    static AccessInfo Grant(string id, AccessState s, string task, string label, TimeSpan age, DateTimeOffset? end) => new()
    {
        Id = id, State = s, TaskName = task, Label = label, Client = Keys.Client, ClientName = Keys.Name, User = Keys.User, Sid = Keys.Sid,
        SessionId = 1, CreatedAt = DateTimeOffset.UtcNow - age, ExpiresAt = end, HeartbeatAt = s == AccessState.Active ? DateTimeOffset.UtcNow : null
    };

    // Snapshots carry only hot metadata: the code is blanked like the broker's archive, so the window must ask for it on selection.
    static JobInfo Job(string id, JobState s, string? grant, TimeSpan age, ExecutionResult? result) => new()
    {
        Id = id, State = s, GrantId = grant, Client = Keys.Client, ClientName = Keys.Name, TaskName = runs[id].Description, OwnerSid = Keys.Sid, SessionId = 1,
        CreatedAt = DateTimeOffset.UtcNow - age, Result = result,
        Run = s == JobState.NeedsApproval ? runs[id] : runs[id] with { Code = "", Env = new() },
    };

    static ExecutionResult Result() => new() { ExitCode = 0, Engine = Keys.Engine, Identity = Keys.User };

    static void Push() => vm?.Apply(new Status(Conn.Online, new Snapshot { Grants = [.. grants], Jobs = [.. jobs] }, DateTimeOffset.UtcNow, "", true, false));

    static void Swap<T>(List<T> list, Func<T, bool> hit, Func<T, T> change)
    {
        for (int i = 0; i < list.Count; i++) if (hit(list[i])) list[i] = change(list[i]);
    }

    /// <summary>Admin executor stand-in: approve activates the synthetic grant and finishes its waiting task on the next snapshot.</summary>
    static Task<AdminResponse> Admin(AdminRequest r, CancellationToken _)
    {
        if (r.Op == AdminOp.Status) return Task.FromResult(new AdminResponse { Ready = true });
        var g = grants.FirstOrDefault(x => x.Id == r.Id);
        if (g is null) return Task.FromResult(new AdminResponse { Ok = false, Error = Keys.Fake });
        if (r.Op == AdminOp.Approve)
        {
            Swap(grants, x => x.Id == r.Id, x => x with { State = AccessState.Active, ExpiresAt = r.ExpiresAt, HeartbeatAt = DateTimeOffset.UtcNow });
            Swap(jobs, x => x.GrantId == r.Id, x => Job(x.Id, JobState.Succeeded, x.GrantId, DateTimeOffset.UtcNow - x.CreatedAt, Result()));
            Application.Current.Dispatcher.BeginInvoke(Push);
        }
        return Task.FromResult(new AdminResponse { Grant = grants.First(x => x.Id == r.Id) });
    }

    /// <summary>Broker stand-in for the window's own calls: full command, output chunks, deny/revoke/cancel.</summary>
    static Task<Response> Broker(Request r)
    {
        var job = jobs.FirstOrDefault(j => j.Id == r.Id);
        Response res;
        if (r.Op == Ops.Status) res = job is null ? new() { Ok = false, Error = Keys.Fake } : new() { Job = job with { Run = runs[job.Id] with { Env = new() } } };
        else if (r.Op == Ops.Output)
        {
            var chunk = r.Cursor == 0 && job?.Id == Keys.Done ? Output : "";
            res = new() { Output = chunk, Cursor = r.Cursor + chunk.Length, Job = job };
        }
        else
        {
            var end = r.Op == Ops.Deny ? AccessState.Denied : AccessState.Revoked;
            Swap(grants, x => x.Id == r.Id, x => x with { State = end, ExpiresAt = DateTimeOffset.UtcNow });
            Swap(jobs, x => x.Id == r.Id || x.GrantId == r.Id && Fmt.Live(x.State), x => x with { State = r.Op == Ops.Cancel ? JobState.Cancelled : JobState.Rejected });
            res = new();
            Application.Current.Dispatcher.BeginInvoke(Push);
        }
        return Task.FromResult(res);
    }

    public static int Run(Application app)
    {
        grants.AddRange([
            Grant(Keys.Req, AccessState.Pending, "Tidy service logs", "Needs to read the log folder to find the failing service.", TimeSpan.FromMinutes(2), null),
            Grant(Keys.Grant, AccessState.Active, "Disk report", "Weekly disk usage summary.", TimeSpan.FromMinutes(40), DateTimeOffset.UtcNow.AddMinutes(50)),
        ]);
        jobs.AddRange([
            Job(Keys.Waiting, JobState.NeedsApproval, Keys.Req, TimeSpan.FromMinutes(2), null),
            Job(Keys.Done, JobState.Succeeded, Keys.Grant, TimeSpan.FromMinutes(35), Result()),
            Job(Keys.Rejected, JobState.Rejected, null, TimeSpan.FromHours(3), null),
        ]);
        using var monitor = new PwshMcp.Tray.Monitor(null, _ => { });
        MainVm? main = null;
        using var coord = new Coord(g => main?.Apply(g), Admin);
        main = vm = new MainVm(monitor, coord) { Send = Broker };
        main.Fix.Seed(_ => Task.FromResult(new RepairResult(RepairState.Ready, Keys.Fake)), _ => Task.FromResult(new RepairResult(RepairState.Ready, Keys.Fake)), () => false);
        main.Seed(new Status(Conn.Online, new Snapshot(), DateTimeOffset.UtcNow, "", true, false), Tab.Requests);
        Push();
        main.Seed(new GateInfo(Gate.Ready, "", DateTimeOffset.UtcNow));
        var window = new MainWindow(main);
        // Closing only hides the real window; in the fixture that ends the run.
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) { window.Retire(); app.Shutdown(); } };
        window.Present();
        Console.WriteLine(string.Format(Keys.Ready, Environment.ProcessId));
        app.Run();
        return 0;
    }
}
