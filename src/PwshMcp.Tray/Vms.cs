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
    // Labels are read on demand so a language switch relabels them without rebuilding the action.
    readonly Func<string> label, armed; readonly Func<Task> act; readonly DispatcherTimer timer;
    bool isArmed, busy;
    public Confirm(Func<string> label, Func<string> armed, Func<Task> act, Func<bool>? can = null)
    {
        this.label = label; this.armed = armed; this.act = act;
        timer = new DispatcherTimer { Interval = Timing.Confirm };
        timer.Tick += (_, _) => Disarm();
        Click = new Relay(Press, () => !busy && (can?.Invoke() ?? true));
    }
    public string Text => isArmed ? armed() : label();
    public string? Tag => isArmed ? Names.Armed : null;
    public ICommand Click { get; }
    async Task Press()
    {
        if (!isArmed) { isArmed = true; Raise(string.Empty); timer.Start(); return; }
        Disarm(); busy = true;
        try { await act(); } finally { busy = false; }
    }
    void Disarm() { timer.Stop(); isArmed = false; Raise(string.Empty); }
    public void Relabel() => Raise(string.Empty);
}

/// <summary>One labelled value in a diagnostics or scope block. Values are shown verbatim and selectable.</summary>
public sealed record Fact(string Label, string Value);

/// <summary>A command as sent. Display text is computed on read so it always follows the current language.</summary>
public sealed class CmdVm
{
    readonly RunSpec run;
    public CmdVm(JobInfo j) { run = j.Run; }
    public string Code => run.Code;
    public string Cwd => run.Cwd;
    public string Meta
    {
        get
        {
            var meta = new List<string> { $"{Copy.Detail.Timeout} {string.Format(Copy.Time.Seconds, run.TimeoutSeconds)}" };
            if (run.Env.Count > 0) meta.Add($"{Copy.Detail.EnvNames}: {string.Join(", ", run.Env.Keys)}");
            if (!string.IsNullOrEmpty(run.Session)) meta.Add($"{Copy.Detail.SessionName}: {run.Session}");
            if (run.Profile) meta.Add(Copy.Detail.Profile);
            return string.Join("  ·  ", meta);
        }
    }
    /// <summary>The description as sent; used only to detect changes.</summary>
    public string Raw => run.Description;
    public string Desc => Fmt.Or(run.Description, Copy.Detail.NoDescription);
    public bool HasDesc => !string.IsNullOrWhiteSpace(run.Description);
    public string DescLine => Desc.ReplaceLineEndings(" ");
}

/// <summary>A grant in any state. Pending ones are requests.</summary>
public sealed class GrantVm : Bindable
{
    readonly MainVm main;
    AccessInfo info = new();
    string notice = ""; bool bad, isNew, sending;
    DateTimeOffset now = DateTimeOffset.UtcNow;
    Life life = Life.SignOut;

    public GrantVm(MainVm main, AccessInfo a)
    {
        this.main = main; info = a; Id = a.Id;
        Approve = new Relay(Submit, () => IsPending && main.Ready && !sending);
        Restart = new Relay(Resume, () => IsActive && !WorkerOn && main.Ready && !sending);
        Deny = new Confirm(() => Copy.Detail.Deny, () => Copy.Detail.ConfirmDeny, () => main.Act(Ops.Deny, Id, this, () => Copy.Detail.Denied, () => Copy.Verb.Deny), () => IsPending);
        Revoke = new Confirm(() => Copy.Detail.Revoke, () => Copy.Detail.ConfirmRevoke, () => main.Act(Ops.Revoke, Id, this, () => Copy.Detail.Revoked, () => Copy.Verb.Revoke), () => IsActive);
    }

    public string Id { get; }
    public AccessInfo Info => info;
    public ObservableCollection<CmdVm> Waiting { get; } = [];
    public ICommand Approve { get; }
    public ICommand Restart { get; }
    public Confirm Deny { get; }
    public Confirm Revoke { get; }

    /// <summary>Readable client name (display only). The authorization key is <see cref="Key"/>.</summary>
    public string Client => Fmt.Who(info.ClientName, info.Client);
    public string Key => Fmt.Key(info.Client);
    /// <summary>What the grant is bound to. Always shown in full next to approve/revoke so styling never hides the permission scope.</summary>
    public IReadOnlyList<Fact> Scope => [new(Copy.Who.Key, Key), new(Copy.Detail.User, User), new(Copy.Detail.Session, Session)];
    public IReadOnlyList<Fact> Diag => [new(IsPending ? Copy.Diag.RequestId : Copy.Diag.GrantId, Id), new(Copy.Diag.Key, Key), new(Copy.Diag.Sid, info.Sid), new(Copy.Diag.SessionId, Session)];
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
    public string StateName => Fmt.Name(info);
    public Tone Tone => Fmt.Tone(info.State);
    public bool IsPending => info.State == AccessState.Pending;
    public bool IsActive => info.State == AccessState.Active;
    public bool IsEnded => !IsPending && !IsActive;
    public string Head => string.Format(IsPending ? Copy.Detail.From : Copy.Detail.Grant, Client);
    public string Requested => Fmt.Full(info.CreatedAt);
    public string Spoken => string.Format(Copy.Tabs.GrantRow, TaskLine, StateName, Client, RowSub);
    public string Ends => info.ExpiresAt is { } e ? $"{Fmt.Full(e)} · {(IsEnded ? StateName : Fmt.Left(e))}" : Copy.Detail.SignOut;
    public string RowSub => IsActive ? (info.ExpiresAt is { } e ? Fmt.Left(e) : Copy.Detail.SignOut) : IsPending ? ReasonLine : Fmt.Clock(info.ExpiresAt ?? info.CreatedAt);
    public bool WorkerOn => info.HeartbeatAt is { } h && DateTimeOffset.UtcNow - h <= Timing.Heartbeat;
    public string Worker => WorkerOn ? string.Format(Copy.Detail.WorkerOn, Fmt.ClockSec(info.HeartbeatAt!.Value)) : Copy.Detail.WorkerOff;
    public string WaitingHead => string.Format(LinkedExact ? Copy.Detail.Waiting : Copy.Detail.WaitingClient, Waiting.Count);
    public bool LinkedExact { get; private set; } = true;
    public DateTimeOffset Start => info.CreatedAt;
    public DateTimeOffset? End => info.ExpiresAt;
    public DateTimeOffset Now { get => now; private set => Set(ref now, value); }

    public bool IsNew { get => isNew; set => Set(ref isNew, value); }
    public string Notice { get => notice; private set { if (Set(ref notice, value)) { Raise(nameof(Footer)); Raise(nameof(ShowPick)); } } }
    public bool NoticeBad { get => bad; private set => Set(ref bad, value); }
    public void Say(string text, bool error) { NoticeBad = error; Notice = text; }
    public void Touch() => Raise(string.Empty);

    // ---- inline approval: lifetime picked here, applied by the installed admin executor ----------------------------

    public bool IsSignOut { get => life == Life.SignOut; set { if (value) Pick(Life.SignOut); } }
    public bool IsHour { get => life == Life.OneHour; set { if (value) Pick(Life.OneHour); } }
    public bool IsFour { get => life == Life.FourHours; set { if (value) Pick(Life.FourHours); } }
    public bool IsToday { get => life == Life.Today; set { if (value) Pick(Life.Today); } }
    public void Pick(Life l) { if (sending) return; life = l; Raise(string.Empty); }
    public Life Life => life;
    public DateTimeOffset? Until => Lives.End(life);
    public double LifeSpan => Lives.Span(life);
    public string LifeText => Until is { } e ? string.Format(Copy.Inline.Until, Fmt.Full(e)) : Copy.Inline.UntilSignOut;
    public string LifeNote => Until is { } e ? Fmt.Left(e) : Copy.Inline.SignOutNote;
    /// <summary>Outcome first; otherwise a pending request says why it can't be approved, or (ShowPick) echoes the chosen lifetime beside Approve.</summary>
    public string Footer => string.IsNullOrEmpty(notice) && IsPending && main.GateBad ? Copy.Gate.Head : notice;
    public bool ShowPick => string.IsNullOrEmpty(notice) && IsPending && !main.GateBad;
    public string Picked => string.Format(Copy.Inline.Picked, LifeText);
    /// <summary>Deadline each lifetime would mean if chosen now; the popup shows them beside the choices.</summary>
    public string SignOutEnd => Copy.Inline.AtSignOut;
    public string HourEnd => Preview(Life.OneHour);
    public string FourEnd => Preview(Life.FourHours);
    public string TodayEnd => Preview(Life.Today);
    static string Preview(Life l) => Lives.End(l) is { } e ? string.Format(Copy.Inline.Until, Fmt.Full(e)) : Copy.Inline.AtSignOut;
    public string Applies => string.Format(Copy.Inline.Applies, Client);
    public bool Accepted { get; private set; }
    public bool CanApprove => IsPending && main.Ready && !sending && !Accepted;
    public string Allows => string.Format(Copy.Inline.Allows, Client);
    public bool GateBad => main.GateBad;
    public string GateBody => main.GateBody;
    public string GateAt => main.GateAt;
    public ICommand Recheck => main.Recheck;
    public bool Sending { get => sending; private set { if (Set(ref sending, value)) { Raise(nameof(CanPick)); Raise(nameof(CanApprove)); Raise(nameof(ApproveText)); Raise(nameof(RestartText)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool CanPick => !sending && main.Ready;
    public string ApproveText => sending ? Copy.Inline.Sending : Copy.Detail.Approve;
    public string RestartText => sending ? Copy.Inline.Resuming : Copy.Detail.RestartWorker;

    /// <summary>One click, one exchange. Success only when the executor returns the grant as Active; anything else leaves the request waiting.</summary>
    public async Task Submit()
    {
        if (!CanApprove) return;
        Sending = true; Say("", false);
        try
        {
            var r = await main.Admin(AdminOp.Approve, Id, Until);
            if (!r.Ok) Say(string.Format(Copy.Inline.Failed, Fmt.Err(r)), true);
            else if (r.Grant?.State != AccessState.Active) Say(string.Format(Copy.Inline.NotActive, r.Grant is { } g ? Fmt.Name(g.State) : Copy.Err.NoResponse), true);
            else { Accepted = true; main.Approved(Id); }
        }
        catch (Exception e) { Say(string.Format(Copy.Inline.Failed, e is UnauthorizedAccessException ? Copy.Gate.Untrusted : Fmt.Err(e)), true); }
        finally { Sending = false; }
    }

    async Task Resume()
    {
        if (sending) return;
        Sending = true; Say("", false);
        try
        {
            var r = await main.Admin(AdminOp.Resume, Id, null);
            Say(r.Ok ? Copy.Inline.Resumed : string.Format(Copy.Inline.ResumeFailed, Fmt.Err(r)), !r.Ok);
        }
        catch (Exception e) { Say(string.Format(Copy.Inline.ResumeFailed, e is UnauthorizedAccessException ? Copy.Gate.Untrusted : Fmt.Err(e)), true); }
        finally { Sending = false; }
    }

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
        Cancel = new Confirm(() => Copy.Detail.Cancel, () => Copy.Detail.ConfirmCancel, () => main.Act(Ops.Cancel, Id, this, () => Copy.Detail.Cancelled, () => Copy.Verb.Cancel), () => Fmt.Live(job.State));
    }
    public string Id { get; }
    public CmdVm Cmd { get; private set; }
    public ICommand Toggle { get; }
    public Confirm Cancel { get; }
    public string Client => Fmt.Who(job.ClientName, job.Client);
    public string TaskName => Fmt.Or(job.TaskName, Copy.Detail.NoTaskName);
    public bool HasTask => !string.IsNullOrWhiteSpace(job.TaskName);
    public IReadOnlyList<Fact> Diag
    {
        get
        {
            var d = new List<Fact> { new(Copy.Diag.TaskId, Id), new(Copy.Diag.Key, Fmt.Key(job.Client)), new(Copy.Diag.Sid, job.OwnerSid), new(Copy.Diag.SessionId, job.SessionId.ToString()) };
            if (job.GrantId is { Length: > 0 } g) d.Insert(1, new(Copy.Diag.GrantId, g));
            if (job.Result is { } r) d.Add(new(Copy.Diag.EnvId, r.EnvironmentId));
            return d;
        }
    }
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
    public string Environment => job.Result is { } r ? Fmt.ClockSec(r.EnvironmentAt) : "";
    public bool Shown { get => shown; set { if (Set(ref shown, value)) { Raise(nameof(ToggleText)); Raise(nameof(Output)); Raise(nameof(Errors)); } } }
    public string ToggleText => Shown ? Copy.Detail.HideOutput : Copy.Detail.ShowOutput;
    public string OutputHint => job.Result is null ? Copy.Detail.OutputPending : Copy.Detail.OutputWarn;
    public string Output => !Shown || job.Result is null ? "" : string.IsNullOrEmpty(job.Result.Output) && string.IsNullOrEmpty(job.Result.Error) ? Copy.Detail.NoOutput : job.Result.Output;
    public string Errors => Shown ? job.Result?.Error ?? "" : "";
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public bool NoticeBad { get => bad; private set => Set(ref bad, value); }
    public void Say(string text, bool error) { NoticeBad = error; Notice = text; }
    public void Touch() => Raise(string.Empty);
    public void Update(JobInfo j)
    {
        var codeChanged = j.Run.Code != job.Run.Code || j.Run.Description != job.Run.Description;
        job = j;
        if (codeChanged) Cmd = new CmdVm(j);
        Raise(string.Empty);
    }
}

/// <summary>The two independently repairable components.</summary>
public enum Part { Conn, Exec }

/// <summary>One component's repair in flight and its last checked outcome. <see cref="Alt"/> marks the second action (restart / elevate).</summary>
public sealed class FixPart : Bindable
{
    bool busy, alt; string note = "", last = ""; Tone tone = Tone.Off;
    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public bool Alt { get => alt; private set => Set(ref alt, value); }
    public string Note { get => note; private set => Set(ref note, value); }
    public Tone Tone { get => tone; private set => Set(ref tone, value); }
    /// <summary>Backend state and detail of the last attempt, verbatim; diagnostics only.</summary>
    public string Last { get => last; private set => Set(ref last, value); }
    public void Begin(bool second) { Alt = second; Busy = true; Note = ""; }
    public void End(RepairResult r, string text, Tone t)
    {
        Last = string.IsNullOrWhiteSpace(r.Detail) ? r.State.ToString() : $"{r.State}: {r.Detail}";
        Note = text; Tone = t; Busy = false;
    }
}

/// <summary>
/// Repair window model. Every attempt starts from a click and reports what the follow-up probe saw; a started process is never called ready.
/// The elevated fallback is offered only after the backend says the scheduled task needs it, and runs only from its own button.
/// </summary>
public sealed class FixVm : Bindable
{
    public delegate Task<RepairResult> Run(bool second);
    readonly MainVm main;
    Run broker, exec; Func<bool> owns;
    readonly Action recheck;
    bool elev;

    public FixVm(MainVm main, Run broker, Run exec, Func<bool> owns, Action recheck)
    {
        this.main = main; this.broker = broker; this.exec = exec; this.owns = owns; this.recheck = recheck;
        Reconnect = new Relay(() => Go(Conn, false), () => !Conn.Busy && !main.Online);
        Restart = new Confirm(() => Copy.Fix.Restart, () => Copy.Fix.ConfirmRestart, () => Go(Conn, true), () => !Conn.Busy && Owns);
        Start = new Relay(() => Go(Exec, false), () => !Exec.Busy && !main.GateOk);
        Elevate = new Relay(() => Go(Exec, true), () => !Exec.Busy && elev && !main.GateOk);
    }

    public FixPart Conn { get; } = new();
    public FixPart Exec { get; } = new();
    public ICommand Reconnect { get; }
    public Confirm Restart { get; }
    public ICommand Start { get; }
    public ICommand Elevate { get; }
    /// <summary>Which part the window was opened for; it gets focus and a brief cue.</summary>
    public Part Focus { get; set; }
    /// <summary>Set when a language switch closed an open repair window, so the rebuilt main window reopens it.</summary>
    public bool Reopen { get; set; }
    /// <summary>Raised by the tray menu; the main window opens the repair window on it.</summary>
    public event Action<Part>? Asked;
    public void Ask(Part p) { Focus = p; Asked?.Invoke(p); }
    /// <summary>The part to open on: the connection while it is down (approvals need both), otherwise the executor.</summary>
    public Part Worst => main.Online ? Part.Exec : Part.Conn;

    // ---- local connection ------------------------------------------------------------------------------------------

    public string ConnTitle => main.ConnTitle;
    public Tone ConnTone => main.ConnTone;
    public string ConnBody => main.Conn switch { Tray.Conn.Online => Copy.Fix.ConnOn, Tray.Conn.Connecting => Copy.Fix.ConnWait, _ => Copy.Fix.ConnOff };
    public bool Owns => owns();
    public bool ShowReconnect => !main.Online || Conn.Busy && !Conn.Alt;
    public bool Other => main.Online && !main.Status.Owned;
    public string ReconnectText => Conn.Busy && !Conn.Alt ? Copy.Fix.Reconnecting : Copy.Fix.Reconnect;
    public bool ConnRunning => Conn.Busy && !Conn.Alt;
    public bool Restarting => Conn.Busy && Conn.Alt;
    public string RestartText => Restarting ? Copy.Fix.Restarting : Restart.Text;
    public string RestartWarn => main.RunningCount > 0 ? string.Format(Copy.Fix.RestartTasks, main.RunningCount) : Copy.Fix.RestartWarn;

    // ---- admin executor --------------------------------------------------------------------------------------------

    public string GateTitle => main.GateTitle;
    public Tone GateTone => main.GateTone;
    public string ExecBody => main.Gate.Gate switch { Gate.Ready => Copy.Fix.ExecOn, Gate.Checking => Copy.Fix.ExecCheck, Gate.Broken => Copy.Fix.ExecBroken, _ => Copy.Fix.ExecOff };
    public bool ShowStart => !main.GateOk || Exec.Busy;
    public bool ShowElev => elev && !main.GateOk;
    /// <summary>The state paragraph; once the fallback card is up, the result line and card say it instead.</summary>
    public bool ShowBody => !ShowElev;
    public bool Starting => Exec.Busy && !Exec.Alt;
    public bool Elevating => Exec.Busy && Exec.Alt;
    public string StartText => Starting ? Copy.Fix.Starting : Copy.Fix.Start;
    public string ElevText => Elevating ? Copy.Fix.Elevating : Copy.Fix.Elevate;

    public IReadOnlyList<Fact> Diag
    {
        get
        {
            var d = new List<Fact> { new(Copy.Fix.DiagConn, Fmt.Or(main.Status.Detail, ConnTitle)), new(Copy.Fix.DiagExec, Fmt.Or(main.Gate.Detail, GateTitle)) };
            if (Conn.Last.Length > 0) d.Add(new(Copy.Fix.DiagConnLast, Conn.Last));
            if (Exec.Last.Length > 0) d.Add(new(Copy.Fix.DiagExecLast, Exec.Last));
            return d;
        }
    }
    async Task Go(FixPart p, bool second)
    {
        if (p.Busy) return;
        p.Begin(second); Touch();
        RepairResult r;
        try { r = await (p == Conn ? broker : exec)(second); }
        catch (Exception e) { r = new(RepairState.Failed, e.Message); }
        if (p == Exec)
        {
            // An unsuccessful registered startup unlocks the shield button; a cancelled prompt keeps it available.
            if (r.State == RepairState.NeedsElevation) elev = true;
            else if (r.State == RepairState.Ready) elev = false;
            recheck();
        }
        var (text, tone) = Say(p == Conn ? Part.Conn : Part.Exec, r.State);
        p.End(r, text, tone);
        Touch();
    }

    /// <summary>What an outcome means to the user. Only Ready (the probe answered) is worded as working.</summary>
    public static (string Text, Tone Tone) Say(Part p, RepairState s) => s switch
    {
        RepairState.Ready => (p == Part.Conn ? Copy.Fix.ConnReady : Copy.Fix.ExecReady, Tone.On),
        RepairState.Started => (Copy.Fix.Started, Tone.Wait),
        RepairState.Cancelled => (Copy.Fix.Cancelled, Tone.Off),
        RepairState.NeedsElevation => (Copy.Fix.NeedsElev, Tone.Wait),
        RepairState.NotOwned => (Copy.Fix.NotOwned, Tone.Bad),
        _ => (Copy.Fix.Failed, Tone.Bad),
    };

    /// <summary>Re-reads state after a status, gate or language change. A component seen ready clears its elevated fallback.</summary>
    public void Touch()
    {
        if (main.GateOk) elev = false;
        Raise(string.Empty);
        CommandManager.InvalidateRequerySuggested();
    }

    public void Relabel() { Restart.Relabel(); Touch(); }

    /// <summary>Offline self-test seam: swaps the backend for local fakes so no task, broker or UAC prompt is ever touched.</summary>
    public void Seed(Run broker, Run exec, Func<bool> owns) { this.broker = broker; this.exec = exec; this.owns = owns; elev = false; Touch(); }
}

public sealed class MainVm : Bindable
{
    readonly Monitor monitor;
    readonly Coord coord;
    readonly HashSet<string> seen = [];
    readonly HashSet<string> handoff = [];
    Status status;
    GateInfo gate;
    Tab tab = Tab.Requests;
    GrantVm? selReq, selAcc; JobVm? selTask;

    public MainVm(Monitor monitor, Coord coord)
    {
        this.monitor = monitor; this.coord = coord; status = monitor.Current; gate = coord.Current;
        RevokeAll = new Confirm(() => Copy.Header.RevokeAll, () => Copy.Header.ConfirmAll, RevokeEvery, () => Online && ActiveCount > 0);
        Retry = new Relay(monitor.Retry, () => !Online);
        Recheck = new Relay(coord.Retry, () => !Ready);
        Fix = new FixVm(this, r => monitor.RecoverAsync(r), e => Recovery.ExecutorAsync(e), () => monitor.Owns, coord.Retry);
    }

    /// <summary>Repair window model for the connection and the executor. Reachable offline, with or without a selected request.</summary>
    public FixVm Fix { get; }
    public string ConnSpoken => string.Format(Copy.Fix.Spoken, ConnTitle);
    public string GateSpoken => string.Format(Copy.Fix.Spoken, GateTitle);

    public ObservableCollection<GrantVm> Requests { get; } = [];
    public ObservableCollection<GrantVm> Access { get; } = [];
    public ObservableCollection<JobVm> Tasks { get; } = [];
    public Confirm RevokeAll { get; }
    public ICommand Retry { get; }
    public ICommand Recheck { get; }

    // ---- admin executor gate (separate from the broker connection) ------------------------------------------------

    public GateInfo Gate => gate;
    /// <summary>New approvals need both the broker and the installed admin executor.</summary>
    public bool Ready => gate.Ready && Online;
    public bool GateOk => gate.Ready;
    public bool GateBad => gate.Gate is Tray.Gate.Down or Tray.Gate.Broken;
    public string GateTitle => gate.Gate switch { Tray.Gate.Ready => Copy.Gate.Ready, Tray.Gate.Down => Copy.Gate.Down, Tray.Gate.Broken => Copy.Gate.Broken, _ => Copy.Gate.Checking };
    public Tone GateTone => gate.Gate switch { Tray.Gate.Ready => Tone.On, Tray.Gate.Checking => Tone.Wait, _ => Tone.Bad };
    public string GateBody => gate.Gate == Tray.Gate.Broken ? string.Format(Copy.Gate.BrokenBody, gate.Detail) : Copy.Gate.DownBody;
    public string GateAt => gate.At is { } at ? string.Format(Copy.Gate.Checked, Fmt.ClockSec(at)) : "";
    public string? GateTip => GateBad ? GateBody : null;

    public void Apply(GateInfo g)
    {
        var was = gate;
        gate = g;
        Raise(nameof(Gate)); Raise(nameof(Ready)); Raise(nameof(GateOk)); Raise(nameof(GateBad)); Raise(nameof(GateTitle)); Raise(nameof(GateTone)); Raise(nameof(GateBody)); Raise(nameof(GateAt)); Raise(nameof(GateTip));
        Raise(nameof(GateSpoken));
        Fix.Touch();
        // Only the checked-at time changes on a steady poll; rebind the detail pane when the state itself moves.
        if (was.Gate != g.Gate || was.Detail != g.Detail) { foreach (var r in Requests.Concat(Access)) r.Touch(); CommandManager.InvalidateRequerySuggested(); }
        else if (GateBad) selReq?.Touch();
    }

    /// <summary>Approve/Resume through the admin executor. Only reachable from a button click; never opens a Windows prompt.</summary>
    public async Task<AdminResponse> Admin(AdminOp op, string id, DateTimeOffset? end)
    {
        try { return await coord.Send(op, id, end); }
        finally { monitor.Retry(); }
    }

    /// <summary>The request became Active: follow it to Access on the next snapshot so the outcome and its band are visible.</summary>
    public void Approved(string id) { handoff.Add(id); monitor.Retry(); }

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
            if (selReq is not null && !Requests.Contains(selReq)) SelReq = null;
            if (selReq is null && Requests.Count > 0) SelReq = Requests[0];
            if (selAcc is not null && !Access.Contains(selAcc)) SelAcc = null;
            if (selTask is not null && !Tasks.Contains(selTask)) SelTask = null;
            foreach (var g in Access.Where(a => handoff.Remove(a.Id)).ToList())
            {
                g.Say(Copy.Inline.Done, false);
                SelAcc = g; Tab = Tab.Access;
            }
        }
        Raise(string.Empty);
        Fix.Touch();
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
    public async Task Act(string op, string id, Bindable target, Func<string> ok, Func<string> verb)
    {
        void Say(string t, bool e) { if (target is GrantVm g) g.Say(t, e); else if (target is JobVm j) j.Say(t, e); }
        try
        {
            var r = await Launch.Send(new Request { Op = op, Id = id });
            Say(r.Ok ? ok() : string.Format(Copy.Detail.ActionFailed, verb(), Fmt.Err(r)), !r.Ok);
        }
        catch (Exception e) { Say(string.Format(Copy.Detail.ActionFailed, verb(), Fmt.Err(e)), true); }
        monitor.Retry();
    }

    /// <summary>Interface language, offered in the header and the tray menu. Switching is display-only: no UAC, no broker restart, no grant change.</summary>
    public bool IsZh { get => L.Current == Lang.Zh; set { if (value) Pick(Lang.Zh); } }
    public bool IsEn { get => L.Current == Lang.En; set { if (value) Pick(Lang.En); } }
    public static void Pick(Lang l)
    {
        if (l == L.Current) return;
        Prefs.Save(l);
        L.Set(l);
    }

    /// <summary>Re-reads every displayed string after a language switch. Messages already shown stay as they were said.</summary>
    public void Relabel()
    {
        RevokeAll.Relabel();
        foreach (var g in Requests.Concat(Access)) { g.Deny.Relabel(); g.Revoke.Relabel(); g.Touch(); }
        foreach (var t in Tasks) { t.Cancel.Relabel(); t.Touch(); }
        Fix.Relabel();
        Raise(string.Empty);
    }

    /// <summary>Synthetic snapshot for the offline self-test and render check; never sent anywhere.</summary>
    public void Seed(Status s, Tab t) { Viewing = false; Apply(s); Tab = t; Raise(string.Empty); }
    public void Seed(GateInfo g) => coord.Seed(g);

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
