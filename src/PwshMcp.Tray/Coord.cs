using System.IO;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

/// <summary>Admin executor (coordinator) availability as last observed. Only Ready allows a new approval.</summary>
public enum Gate { Checking, Ready, Down, Broken }

public sealed record GateInfo(Gate Gate, string Detail, DateTimeOffset? At)
{
    public bool Ready => Gate == Gate.Ready;
}

/// <summary>Grant lifetimes offered at approval, and the end each one means.</summary>
public static class Lives
{
    public static readonly Life[] All = [Life.SignOut, Life.OneHour, Life.FourHours, Life.Today];

    public static DateTimeOffset? End(Life l) => l switch
    {
        Life.OneHour => DateTimeOffset.Now.AddHours(1),
        Life.FourHours => DateTimeOffset.Now.AddHours(4),
        Life.Today => new DateTimeOffset(DateTime.Today.AddDays(1).AddMinutes(-1)),
        _ => null
    };

    /// <summary>Band length proportional to the chosen duration against the longest bounded choice.</summary>
    public static double Span(Life l)
    {
        if (End(l) is not { } e) return 1;
        var longest = Math.Max(4, (new DateTimeOffset(DateTime.Today.AddDays(1)) - DateTimeOffset.Now).TotalHours);
        return Math.Clamp((e - DateTimeOffset.Now).TotalHours / longest, 0.08, 1);
    }
}

/// <summary>
/// Talks to the installed admin executor over its per-user, per-session pipe. Polls Status on its own loop so the broker
/// monitor never waits on it, and sends Approve/Resume only from a user click. Never falls back to a Windows prompt.
/// </summary>
public sealed class Coord : IDisposable
{
    public delegate Task<AdminResponse> Transport(AdminRequest r, CancellationToken ct);

    readonly Transport send;
    readonly Action<GateInfo> publish;
    readonly CancellationTokenSource stop = new();
    readonly SemaphoreSlim kick = new(0);

    /// <param name="send">Null uses the real pipe; the offline self-test passes a fake so nothing leaves the process.</param>
    public Coord(Action<GateInfo> publish, Transport? send = null)
    {
        this.publish = publish;
        // Server path stays pinned to the protected Program Files install (AdminClient default): a dev build's
        // --backend-dir never widens who may answer as the admin executor.
        this.send = send ?? ((r, ct) => new AdminClient().SendAsync(r, ct));
    }

    public GateInfo Current { get; private set; } = new(Gate.Checking, "", null);

    public void Start() => _ = Task.Run(Loop);
    public void Retry() => kick.Release();

    async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            Emit(await Probe());
            try { await kick.WaitAsync(Current.Ready ? Timing.GateReady : Timing.GateDown, stop.Token); } catch (OperationCanceledException) { return; }
        }
    }

    async Task<GateInfo> Probe()
    {
        try
        {
            var r = await send(new AdminRequest { Op = AdminOp.Status }, stop.Token);
            if (r.Ok && r.Ready) return new(Gate.Ready, "", DateTimeOffset.UtcNow);
            return new(Gate.Broken, Fmt.Err(r), DateTimeOffset.UtcNow);
        }
        catch (Exception e) when (!stop.IsCancellationRequested) { return Seen(e); }
        catch { return Current; }
    }

    /// <summary>Maps a failed exchange to what the user can do about it: start/install (Down) or repair (Broken).</summary>
    public static GateInfo Seen(Exception e) => e switch
    {
        UnauthorizedAccessException => new(Gate.Broken, Copy.Gate.Untrusted, DateTimeOffset.UtcNow),
        OperationCanceledException or TimeoutException or IOException => new(Gate.Down, Copy.Gate.NoPipe, DateTimeOffset.UtcNow),
        _ => new(Gate.Broken, string.Format(Copy.Err.Generic, e.Message), DateTimeOffset.UtcNow),
    };

    /// <summary>One Approve or Resume. Throws on transport failure; the caller reports it on the item and the gate re-probes.</summary>
    public async Task<AdminResponse> Send(AdminOp op, string id, DateTimeOffset? end = null)
    {
        try { return await send(new AdminRequest { Op = op, Id = id, ExpiresAt = end?.ToUniversalTime() }, stop.Token); }
        catch (Exception e) { Emit(Seen(e)); throw; }
        finally { Retry(); }
    }

    /// <summary>Synthetic state for the offline self-test and render check.</summary>
    public void Seed(GateInfo g) => Emit(g);

    void Emit(GateInfo g) { Current = g; publish(g); }

    public void Dispose() { stop.Cancel(); kick.Release(); }
}
