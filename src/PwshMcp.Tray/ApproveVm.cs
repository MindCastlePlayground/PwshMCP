using System.Collections.ObjectModel;
using System.Windows.Input;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

public enum Step { Loading, Ready, Blocked, Sending, Starting, Done, Failed }
public enum Life { SignOut, OneHour, FourHours, Today }

/// <summary>Elevated approve / resume flow. Reads one snapshot, freezes the request, and never reports Active without the broker saying so.</summary>
public sealed class ApproveVm : Bindable
{
    readonly string id;
    readonly string? backend;
    readonly bool resume;
    Step step = Step.Loading;
    Life span =Life.SignOut;
    string message = Copy.Approve.Loading;
    bool approved;
    AccessInfo? grant;

    public ApproveVm(string? id, string? backend, bool resume)
    {
        this.id = id ?? ""; this.backend = backend; this.resume = resume;
        Go = new Relay(Run, () => step == Step.Ready);
        Retry = new Relay(RetryWorker, () => step == Step.Failed && approved);
        Revoke = new Relay(RevokeNow, () => step == Step.Failed && approved);
    }

    public event Action? Finished;
    public ObservableCollection<CmdVm> Waiting { get; } = [];
    public ICommand Go { get; }
    public ICommand Retry { get; }
    public ICommand Revoke { get; }

    public string Title => resume ? Copy.Approve.ResumeTitle : Copy.Approve.Title;
    public bool IsResume => resume;
    public bool IsApprove => !resume;
    public Step Step { get => step; private set { if (Set(ref step, value)) { Raise(nameof(Busy)); Raise(nameof(Shows)); Raise(nameof(MessageTone)); Raise(nameof(CanEdit)); Raise(nameof(ShowRecovery)); Raise(nameof(CancelText)); CommandManager.InvalidateRequerySuggested(); } } }
    public string Message { get => message; private set => Set(ref message, value); }
    public Tone MessageTone => step switch { Step.Failed or Step.Blocked => Tone.Bad, Step.Done => Tone.On, _ => Tone.Info };
    public bool Busy => step is Step.Loading or Step.Sending or Step.Starting;
    public bool Shows => grant is not null;
    public bool CanEdit => step == Step.Ready;
    public bool ShowRecovery => step == Step.Failed && approved;
    public string CancelText => step is Step.Ready or Step.Loading ? Copy.Approve.Cancel : Copy.Approve.Close;
    public string ConfirmText => resume ? Copy.Approve.ResumeConfirm : Copy.Approve.Confirm;

    public string Client => grant is null ? "" : string.IsNullOrWhiteSpace(grant.Client) ? Copy.Product : grant.Client;
    public string Head => string.Format(resume ? Copy.Approve.ResumeHead : Copy.Approve.Head, Client);
    public string Reason => string.IsNullOrWhiteSpace(grant?.Label) ? Copy.Detail.NoReason : grant!.Label;
    public string User => grant?.User ?? "";
    public string Session => grant?.SessionId.ToString() ?? "";
    public string Submitted => grant is null ? "" : Fmt.Stamp(grant.CreatedAt);
    public string Allows => string.Format(Copy.Approve.Allows, Client);
    public string Later => string.Format(Copy.Approve.Later, Client);
    public string StartsHead => Waiting.Count == 0 ? Copy.Approve.StartsNone : string.Format(Copy.Approve.StartsHead, Waiting.Count);

    public bool IsSignOut { get => span == Life.SignOut; set { if (value) Pick(Life.SignOut); } }
    public bool IsHour { get => span == Life.OneHour; set { if (value) Pick(Life.OneHour); } }
    public bool IsFour { get => span == Life.FourHours; set { if (value) Pick(Life.FourHours); } }
    public bool IsToday { get => span == Life.Today; set { if (value) Pick(Life.Today); } }
    void Pick(Life s) { span = s; Raise(string.Empty); }

    public DateTimeOffset? End => span switch
    {
        Life.OneHour => DateTimeOffset.Now.AddHours(1),
        Life.FourHours => DateTimeOffset.Now.AddHours(4),
        Life.Today => new DateTimeOffset(DateTime.Today.AddDays(1).AddMinutes(-1)),
        _ => null
    };
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
    /// <summary>Band length proportional to the chosen duration against the longest bounded choice.</summary>
    public double BandSpan
    {
        get
        {
            if (End is not { } e) return 1;
            var longest = Math.Max(4, (new DateTimeOffset(DateTime.Today.AddDays(1)) - DateTimeOffset.Now).TotalHours);
            return Math.Clamp((e - DateTimeOffset.Now).TotalHours / longest, 0.08, 1);
        }
    }
    public string EndText => End is { } e ? string.Format(Copy.Approve.EndsAt, Fmt.Full(e)) : Copy.Approve.SignOutNote;

    public async Task Load()
    {
        if (string.IsNullOrWhiteSpace(id)) { Block(Copy.Approve.NoId); return; }
        if (!Launch.IsAdmin()) { Block(Copy.Approve.NotAdmin); return; }
        try
        {
            var r = await Launch.Send(new Request { Op = Ops.Snapshot });
            if (!r.Ok || r.Snapshot is null) { Block(string.Format(Copy.Approve.Unreachable, Fmt.Err(r))); return; }
            grant = r.Snapshot.Grants.FirstOrDefault(g => g.Id == id);
            if (grant is null) { Block(Copy.Approve.Missing); return; }
            var want = resume ? AccessState.Active : AccessState.Pending;
            if (grant.State != want) { Block(string.Format(resume ? Copy.Approve.NotActive : Copy.Approve.Gone, Fmt.Name(grant.State))); return; }
            if (resume && grant.HeartbeatAt is { } h && DateTimeOffset.UtcNow - h <= Timing.Heartbeat) { Block(Copy.Approve.AlreadyOn); return; }
            foreach (var j in r.Snapshot.Jobs.Where(j => j.State == JobState.NeedsApproval && j.GrantId == id).OrderBy(j => j.CreatedAt)) Waiting.Add(new CmdVm(j));
            approved = resume;
            Message = resume ? "" : Copy.Approve.Elevation;
            Step = Step.Ready;
        }
        catch (Exception e) { Block(string.Format(Copy.Approve.Unreachable, Fmt.Err(e))); }
        finally { Raise(string.Empty); }
    }

    void Block(string text) { Message = text; Step = Step.Blocked; }
    void Fail(string text) { Message = text; Step = Step.Failed; }

    async Task Run()
    {
        if (!resume)
        {
            Step = Step.Sending; Message = Copy.Approve.Sending;
            try
            {
                var r = await Launch.Send(new Request { Op = Ops.Approve, Id = id, ExpiresAt = End?.ToUniversalTime() });
                if (!r.Ok) { Fail(string.Format(Copy.Approve.ApproveFailed, Fmt.Err(r))); return; }
                var state = r.Grant?.State ?? await Query();
                if (state != AccessState.Active) { Fail(string.Format(Copy.Approve.NotActiveAfter, state is { } s ? Fmt.Name(s) : Copy.Err.NoResponse)); return; }
                approved = true;
            }
            catch (Exception e) { Fail(string.Format(Copy.Approve.ApproveFailed, Fmt.Err(e))); return; }
        }
        await StartWorker();
    }

    async Task<AccessState?> Query()
    {
        try { var r = await Launch.Send(new Request { Op = Ops.Snapshot }); return r.Snapshot?.Grants.FirstOrDefault(g => g.Id == id)?.State; }
        catch { return null; }
    }

    async Task StartWorker()
    {
        Step = Step.Starting; Message = Copy.Approve.Starting;
        var err = await Launch.Worker(id, backend);
        if (err is not null) { Fail(string.Format(Copy.Approve.WorkerFailed, err)); return; }
        Message = resume ? Copy.Approve.ResumeDone : Copy.Approve.Done;
        Step = Step.Done;
        await Task.Delay(Timing.DoneClose);
        Finished?.Invoke();
    }

    Task RetryWorker() => StartWorker();

    async Task RevokeNow()
    {
        try
        {
            var r = await Launch.Send(new Request { Op = Ops.Revoke, Id = id });
            if (!r.Ok) { Fail(string.Format(Copy.Detail.ActionFailed, Copy.Verb.Revoke, Fmt.Err(r))); return; }
            approved = false; Message = Copy.Approve.RevokedAfter; Step = Step.Blocked;
        }
        catch (Exception e) { Fail(string.Format(Copy.Detail.ActionFailed, Copy.Verb.Revoke, Fmt.Err(e))); }
    }
}
