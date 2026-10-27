using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

public enum Tab { Requests, Access, Tasks }

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class Relay : ICommand
{
    readonly Func<Task> run; readonly Func<bool>? can; bool busy;
    public Relay(Func<Task> run, Func<bool>? can = null) { this.run = run; this.can = can; }
    public Relay(Action run, Func<bool>? can = null) : this(() => { run(); return Task.CompletedTask; }, can) { }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? p) => !busy && (can?.Invoke() ?? true);
    public async void Execute(object? p)
    {
        busy = true; CommandManager.InvalidateRequerySuggested();
        try { await run(); } finally { busy = false; CommandManager.InvalidateRequerySuggested(); }
    }
}

/// <summary>Two-step inline confirmation instead of a modal: first click arms for a few seconds, second click acts.</summary>
public sealed class Confirm : Bindable
{
    readonly string label, armed; readonly Func<Task> act; readonly DispatcherTimer timer;
    string text; bool isArmed, busy;
    public Confirm(string label, string armed, Func<Task> act, Func<bool>? can = null)
    {
        this.label = label; this.armed = armed; this.act = act; text = label;
        timer = new DispatcherTimer { Interval = Timing.Confirm };
        timer.Tick += (_, _) => Disarm();
        Click = new Relay(Press, () => !busy && (can?.Invoke() ?? true));
    }
    public string Text { get => text; private set => Set(ref text, value); }
    public string? Tag => isArmed ? "armed" : null;
    public ICommand Click { get; }
    async Task Press()
    {
        if (!isArmed) { isArmed = true; Text = armed; Raise(nameof(Tag)); timer.Start(); return; }
        Disarm(); busy = true;
        try { await act(); } finally { busy = false; }
    }
    void Disarm() { timer.Stop(); isArmed = false; Text = label; Raise(nameof(Tag)); }
}

public sealed class CmdVm
{
    public CmdVm(JobInfo j)
    {
        Code = j.Run.Code; Cwd = j.Run.Cwd; Raw = j.Run.Description;
        HasDesc = !string.IsNullOrWhiteSpace(j.Run.Description);
        Desc = Fmt.Or(j.Run.Description, Copy.Detail.NoDescription);
        var meta = new List<string> { $"{Copy.Detail.Timeout} {string.Format(Copy.Time.Seconds, j.Run.TimeoutSeconds)}" };
        if (j.Run.Env.Count > 0) meta.Add($"{Copy.Detail.EnvNames}: {string.Join(", ", j.Run.Env.Keys)}");
        if (!string.IsNullOrEmpty(j.Run.Session)) meta.Add($"{Copy.Detail.SessionName}: {j.Run.Session}");
        if (j.Run.Profile) meta.Add(Copy.Detail.Profile);
        Meta = string.Join("  ·  ", meta);
    }
    public string Code { get; }
    public string Cwd { get; }
    public string Meta { get; }
    /// <summary>The description as sent; used only to detect changes.</summary>
    public string Raw { get; }
    public string Desc { get; }
    public bool HasDesc { get; }
    public string DescLine => Desc.ReplaceLineEndings(" ");
}

/// <summary>A grant in any state. Pending ones are requests.</summary>
public sealed class GrantVm : Bindable
{
    readonly MainVm main;
    AccessInfo info = new();
    string notice = ""; bool bad, isNew, divider;
    DateTimeOffset now = DateTimeOffset.UtcNow;

    public GrantVm(MainVm main, AccessInfo a)
    {
        this.main = main; info = a; Id = a.Id;
        Approve = new Relay(() => main.Elevate(Cli.Approve, Id, this), () => IsPending);
        Restart = new Relay(() => main.Elevate(Cli.Resume, Id, this), () => IsActive && !WorkerOn);
        Deny = new Confirm(Copy.Detail.Deny, Copy.Detail.ConfirmDeny, () => main.Act(Ops.Deny, Id, this, Copy.Detail.Denied, Copy.Verb.Deny), () => IsPending);
        Revoke = new Confirm(Copy.Detail.Revoke, Copy.Detail.ConfirmRevoke, () => main.Act(Ops.Revoke, Id, this, Copy.Detail.Revoked, Copy.Verb.Revoke), () => IsActive);
    }

    public string Id { get; }
    public AccessInfo Info => info;
    public ObservableCollection<CmdVm> Waiting { get; } = [];
    public ICommand Approve { get; }
    public ICommand Restart { get; }
    public Confirm Deny { get; }
    public Confirm Revoke { get; }

    public string Client => string.IsNullOrWhiteSpace(info.Client) ? Copy.Product : info.Client;
    public string Reason => string.IsNullOrWhiteSpace(info.Label) ? Copy.Detail.NoReason : info.Label;
    public string ReasonLine => string.IsNullOrWhiteSpace(info.Label) ? Copy.Detail.NoReason : info.Label.ReplaceLineEndings(" ");
    public string TaskName => Fmt.Or(info.TaskName, Copy.Detail.NoTaskName);
    public string TaskLine => Fmt.OneLine(info.TaskName, Copy.Detail.NoTaskName);
    public bool HasTask => !string.IsNullOrWhiteSpace(info.TaskName);
    public string User => info.User;
    public string Session => info.SessionId.ToString();
    public string Submitted => Fmt.Stamp(info.CreatedAt);
    public string RequestEnds => Fmt.Full(info.CreatedAt + Timing.RequestLife);
    public string Age => Fmt.Ago(info.CreatedAt);
    public string StateName => Fmt.Name(info.State);
    public Tone Tone => Fmt.Tone(info.State);
    public bool IsPending => info.State == AccessState.Pending;
    public bool IsActive => info.State == AccessState.Active;
    public bool IsEnded => !IsPending && !IsActive;
    public string Head => string.Format(IsPending ? Copy.Detail.From : Copy.Detail.Grant, Client);
    public string Requested => Fmt.Full(info.CreatedAt);
    public string Spoken => string.Format(Copy.Tabs.GrantRow, Client, StateName, TaskLine, RowSub);
    public string Ends => info.ExpiresAt is { } e ? $"{Fmt.Full(e)} · {Fmt.Left(e)}" : Copy.Detail.SignOut;
    public string RowSub => IsActive ? (info.ExpiresAt is { } e ? Fmt.Left(e) : Copy.Detail.SignOut) : IsPending ? ReasonLine : Fmt.Clock(info.ExpiresAt ?? info.CreatedAt);
    public bool WorkerOn => info.HeartbeatAt is { } h && DateTimeOffset.UtcNow - h <= Timing.Heartbeat;
    public string Worker => WorkerOn ? string.Format(Copy.Detail.WorkerOn, Fmt.ClockSec(info.HeartbeatAt!.Value)) : Copy.Detail.WorkerOff;
    public string WaitingHead => string.Format(LinkedExact ? Copy.Detail.Waiting : Copy.Detail.WaitingClient, Waiting.Count);
    public bool LinkedExact { get; private set; } = true;
    public DateTimeOffset Start => info.CreatedAt;
    public DateTimeOffset? End => info.ExpiresAt;
    public DateTimeOffset Now { get => now; private set => Set(ref now, value); }

    public bool IsNew { get => isNew; set => Set(ref isNew, value); }
    public bool Divider { get => divider; set => Set(ref divider, value); }
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public bool NoticeBad { get => bad; private set => Set(ref bad, value); }
    public void Say(string text, bool error) { NoticeBad = error; Notice = text; }

    public void Update(AccessInfo a, IReadOnlyList<JobInfo> jobs)
    {
        info = a; Now = DateTimeOffset.UtcNow;
        if (IsPending)
        {
            var exact = jobs.Where(j => j.State == JobState.NeedsApproval && j.GrantId == a.Id).ToList();
            LinkedExact = exact.Count > 0 || !jobs.Any(j => j.State == JobState.NeedsApproval && j.GrantId is null);
            var list = LinkedExact ? exact : jobs.Where(j => j.State == JobState.NeedsApproval && j.GrantId is null && j.Client == a.Client && j.OwnerSid == a.Sid && j.SessionId == a.SessionId).ToList();
            if (list.Count != Waiting.Count || list.Where((j, i) => j.Run.Code != Waiting[i].Code || j.Run.Description != Waiting[i].Raw).Any())
            {
                Waiting.Clear();
                foreach (var j in list.OrderBy(j => j.CreatedAt)) Waiting.Add(new CmdVm(j));
            }
        }
        Raise(string.Empty);
    }
}

public sealed class JobVm : Bindable
{
    readonly MainVm main;
    JobInfo job;
    bool shown; string notice = ""; bool bad;
    public JobVm(MainVm main, JobInfo j)
    {
        this.main = main; job = j; Id = j.Id; Cmd = new CmdVm(j);
        Toggle = new Relay(() => Shown = !Shown, () => job.Result is not null);
        Cancel = new Confirm(Copy.Detail.Cancel, Copy.Detail.ConfirmCancel, () => main.Act(Ops.Cancel, Id, this, Copy.Detail.Cancelled, Copy.Verb.Cancel), () => Fmt.Live(job.State));
    }
    public string Id { get; }
    public CmdVm Cmd { get; private set; }
    public ICommand Toggle { get; }
    public Confirm Cancel { get; }
    public string Client => string.IsNullOrWhiteSpace(job.Client) ? Copy.Product : job.Client;
    public string Head => string.Format(Copy.Detail.Task, Client);
    public string Line => Cmd.DescLine;
    public bool HasDesc => Cmd.HasDesc;
    public string Spoken => string.Format(Copy.Tabs.TaskRow, Line, StateName, Client);
    public string Age => Fmt.Ago(job.CreatedAt);
    public string Submitted => Fmt.Stamp(job.CreatedAt);
    public string StateName => Fmt.Name(job.State);
    public Tone Tone => Fmt.Tone(job.State);
    public string Privilege => job.Run.Privilege == Shared.Privilege.Elevated ? Copy.State.Elevated : Copy.State.Normal;
    public bool CanCancel => Fmt.Live(job.State);
    public bool HasResult => job.Result is not null;
    public string Engine => job.Result?.Engine ?? "";
    public string Identity => job.Result?.Identity ?? "";
    public string ExitCode => job.Result?.ExitCode.ToString() ?? "";
    public string Environment => job.Result is { } r ? $"{Fmt.ClockSec(r.EnvironmentAt)} · {r.EnvironmentId[..Math.Min(8, r.EnvironmentId.Length)]}" : "";
    public bool Shown { get => shown; set { if (Set(ref shown, value)) { Raise(nameof(ToggleText)); Raise(nameof(Output)); Raise(nameof(Errors)); } } }
    public string ToggleText => Shown ? Copy.Detail.HideOutput : Copy.Detail.ShowOutput;
    public string OutputHint => job.Result is null ? Copy.Detail.OutputPending : Copy.Detail.OutputWarn;
    public string Output => !Shown || job.Result is null ? "" : string.IsNullOrEmpty(job.Result.Output) && string.IsNullOrEmpty(job.Result.Error) ? Copy.Detail.NoOutput : job.Result.Output;
    public string Errors => Shown ? job.Result?.Error ?? "" : "";
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public bool NoticeBad { get => bad; private set => Set(ref bad, value); }
    public void Say(string text, bool error) { NoticeBad = error; Notice = text; }
    public void Update(JobInfo j)
    {
        var codeChanged = j.Run.Code != job.Run.Code || j.Run.Description != job.Run.Description;
        job = j;
        if (codeChanged) Cmd = new CmdVm(j);
        Raise(string.Empty);
    }
}

public sealed class MainVm : Bindable
{
    readonly Monitor monitor;
    readonly string? backend;
    readonly HashSet<string> seen = [];
    Status status;
    Tab tab = Tab.Requests;
    GrantVm? selReq, selAcc; JobVm? selTask;

    public MainVm(Monitor monitor, string? backend)
    {
        this.monitor = monitor; this.backend = backend; status = monitor.Current;
        RevokeAll = new Confirm(Copy.Header.RevokeAll, Copy.Header.ConfirmAll, RevokeEvery, () => Online && ActiveCount > 0);
        Retry = new Relay(monitor.Retry, () => !Online);
    }

    public ObservableCollection<GrantVm> Requests { get; } = [];
    public ObservableCollection<GrantVm> Access { get; } = [];
    public ObservableCollection<JobVm> Tasks { get; } = [];
    public Confirm RevokeAll { get; }
    public ICommand Retry { get; }

    public Status Status => status;
    public bool Online => status.Conn == Conn.Online;
    public Conn Conn => status.Conn;
    public string ConnTitle => status.Conn switch { Conn.Online => Copy.Conn.Online, Conn.Connecting => Copy.Conn.Connecting, _ => Copy.Conn.Offline };
    public Tone ConnTone => status.Conn switch { Conn.Online => Tone.On, Conn.Connecting => Tone.Wait, _ => Tone.Bad };
    public string ConnDetail
    {
        get
        {
            if (status.Conn != Conn.Online) return status.Detail;
            var who = status.Owned ? Copy.Conn.OwnedBroker : Copy.Conn.ExternalBroker;
            return status.Elevated ? $"{who} · {Copy.Conn.Elevated}" : who;
        }
    }
    public string Stale => !Online && status.At is { } at ? string.Format(Copy.Conn.Stale, Fmt.ClockSec(at)) : "";
    public int PendingCount => Requests.Count;
    public int ActiveCount => Access.Count(a => a.IsActive);
    public int LiveCount => Tasks.Count(t => t.CanCancel);
    public int RunningCount => status.Snap?.Jobs.Count(j => j.State is JobState.Queued or JobState.Running) ?? 0;
    public string RequestsTab => Count(Copy.Tabs.Requests, PendingCount);
    public string AccessTab => Count(Copy.Tabs.Access, ActiveCount);
    public string TasksTab => Count(Copy.Tabs.Tasks, LiveCount);
    static string Count(string s, int n) => n > 0 ? $"{s}  {n}" : s;

    public Tab Tab { get => tab; set { if (Set(ref tab, value)) { See(); Raise(nameof(Selected)); Raise(nameof(IsEmpty)); Raise(nameof(EmptyHead)); Raise(nameof(EmptyBody)); Raise(nameof(IsReq)); Raise(nameof(IsAcc)); Raise(nameof(IsTask)); } } }
    public bool IsReq { get => tab == Tab.Requests; set { if (value) Tab = Tab.Requests; } }
    public bool IsAcc { get => tab == Tab.Access; set { if (value) Tab = Tab.Access; } }
    public bool IsTask { get => tab == Tab.Tasks; set { if (value) Tab = Tab.Tasks; } }
    public GrantVm? SelReq { get => selReq; set { if (Set(ref selReq, value)) { See(); Raise(nameof(Selected)); } } }

    bool viewing;
    /// <summary>True while the window is on screen; only then does selecting a request count as having seen it.</summary>
    public bool Viewing { get => viewing; set { viewing = value; See(); } }
    void See() { if (viewing && tab == Tab.Requests && selReq is { } r) { seen.Add(r.Id); r.IsNew = false; } }
    public GrantVm? SelAcc { get => selAcc; set { if (Set(ref selAcc, value)) Raise(nameof(Selected)); } }
    public JobVm? SelTask { get => selTask; set { if (Set(ref selTask, value)) Raise(nameof(Selected)); } }
    public object? Selected => tab switch { Tab.Requests => selReq, Tab.Access => selAcc, _ => selTask };
    public bool IsEmpty => tab switch { Tab.Requests => Requests.Count == 0, Tab.Access => Access.Count == 0, _ => Tasks.Count == 0 };
    public string EmptyHead => tab switch { Tab.Requests => Copy.Empty.RequestsHead, Tab.Access => Copy.Empty.AccessHead, _ => Copy.Empty.TasksHead };
    public string EmptyBody => tab switch { Tab.Requests => Copy.Empty.RequestsBody, Tab.Access => Copy.Empty.AccessBody, _ => Copy.Empty.TasksBody };

    public void Apply(Status s)
    {
        status = s;
        if (s.Snap is { } snap)
        {
            var jobs = snap.Jobs;
            Sync(Requests, snap.Grants.Where(g => g.State == AccessState.Pending).OrderByDescending(g => g.CreatedAt), g => g.Id, g => new GrantVm(this, g), (vm, g) => vm.Update(g, jobs));
            Sync(Access, snap.Grants.Where(g => g.State != AccessState.Pending).OrderBy(g => g.State == AccessState.Active ? 0 : 1).ThenByDescending(g => g.ExpiresAt ?? g.CreatedAt),
                g => g.Id, g => new GrantVm(this, g), (vm, g) => vm.Update(g, jobs));
            Sync(Tasks, jobs.OrderByDescending(j => j.CreatedAt), j => j.Id, j => new JobVm(this, j), (vm, j) => vm.Update(j));
            See();
            foreach (var r in Requests) r.IsNew = !seen.Contains(r.Id);
            for (int i = 0; i < Access.Count; i++) Access[i].Divider = Access[i].IsEnded && (i == 0 || !Access[i - 1].IsEnded);
            if (selReq is not null && !Requests.Contains(selReq)) SelReq = null;
            if (selReq is null && Requests.Count > 0) SelReq = Requests[0];
            if (selAcc is not null && !Access.Contains(selAcc)) SelAcc = null;
            if (selTask is not null && !Tasks.Contains(selTask)) SelTask = null;
        }
        Raise(string.Empty);
    }

    static void Sync<T, TVm>(ObservableCollection<TVm> col, IEnumerable<T> src, Func<T, string> key, Func<T, TVm> make, Action<TVm, T> update) where TVm : class
    {
        var items = src.ToList();
        var byId = col.ToDictionary(v => v switch { GrantVm g => g.Id, JobVm j => j.Id, _ => "" });
        var wanted = new List<TVm>(items.Count);
        foreach (var it in items)
        {
            if (byId.TryGetValue(key(it), out var vm)) update(vm, it);
            else { vm = make(it); update(vm, it); }
            wanted.Add(vm);
        }
        for (int i = col.Count - 1; i >= 0; i--) if (!wanted.Contains(col[i])) col.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            var at = col.IndexOf(wanted[i]);
            if (at < 0) col.Insert(i, wanted[i]);
            else if (at != i) col.Move(at, i);
        }
    }

    /// <summary>Sends a deny/revoke/cancel and reports the outcome on the item. Never assumes success.</summary>
    public async Task Act(string op, string id, Bindable target, string ok, string verb)
    {
        void Say(string t, bool e) { if (target is GrantVm g) g.Say(t, e); else if (target is JobVm j) j.Say(t, e); }
        try
        {
            var r = await Launch.Send(new Request { Op = op, Id = id });
            Say(r.Ok ? ok : string.Format(Copy.Detail.ActionFailed, verb, Fmt.Err(r)), !r.Ok);
        }
        catch (Exception e) { Say(string.Format(Copy.Detail.ActionFailed, verb, Fmt.Err(e)), true); }
        monitor.Retry();
    }

    /// <summary>Opens the elevated approval/resume window. Only reachable from a button click.</summary>
    public async Task Elevate(string flag, string id, GrantVm g)
    {
        g.Say("", false);
        var (res, err) = await Launch.Elevated(flag, id, backend);
        if (res == Uac.Cancelled) g.Say(Copy.Detail.UacCancelled, false);
        else if (res == Uac.Failed) g.Say(string.Format(Copy.Detail.UacFailed, err), true);
    }

    public async Task<(int ok, string? error)> RevokeActive()
    {
        int ok = 0; string? error = null;
        foreach (var g in Access.Where(a => a.IsActive).ToList())
        {
            try { var r = await Launch.Send(new Request { Op = Ops.Revoke, Id = g.Id }); if (r.Ok) ok++; else error = Fmt.Err(r); }
            catch (Exception e) { error = Fmt.Err(e); }
        }
        monitor.Retry();
        return (ok, error);
    }

    async Task RevokeEvery()
    {
        var (_, error) = await RevokeActive();
        Notice = error is null ? "" : string.Format(Copy.Detail.ActionFailed, Copy.Tray.RevokeAll, error);
    }

    string notice = "";
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public void Fault(Exception e) => Notice = string.Format(Copy.Err.Generic, e.Message);
}
