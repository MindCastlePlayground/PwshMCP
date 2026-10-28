using System.Diagnostics;
using System.IO;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

public enum Conn { Connecting, Online, Offline }

public sealed record Status(Conn Conn, Snapshot? Snap, DateTimeOffset? At, string Detail, bool Owned, bool Elevated);

/// <summary>Polls the broker once a second and, when no broker pipe exists, owns a hidden local broker.</summary>
public sealed class Monitor : IDisposable
{
    readonly string? backend;
    readonly Action<Status> publish;
    readonly CancellationTokenSource stop = new();
    readonly List<DateTimeOffset> restarts = [];
    readonly SemaphoreSlim kick = new(0);
    Process? owned;
    DateTimeOffset ownedAt;
    bool gaveUp;
    Snapshot? last;
    DateTimeOffset? lastAt;

    public Monitor(string? backend, Action<Status> publish) { this.backend = backend; this.publish = publish; }

    public bool Owns => owned is { HasExited: false };
    public Status Current { get; private set; } = new(Conn.Connecting, null, null, Copy.Conn.Connecting, false, false);

    public void Start() => _ = Task.Run(Loop);

    /// <summary>Re-poll now; clears a restart give-up.</summary>
    public void Retry() { retry = true; kick.Release(); }
    volatile bool retry;
    volatile bool restart;
    int cycles;
    readonly SemaphoreSlim repair = new(1);

    /// <summary>Retry or explicitly restart only our owned Broker. Success requires a new successful snapshot.</summary>
    public async Task<RepairResult> RecoverAsync(bool restart = false)
    {
        await repair.WaitAsync();
        try
        {
            if (restart && !Owns) return new(RepairState.NotOwned);
            var before = Volatile.Read(ref cycles);
            this.restart = restart;
            Retry();
            var until = DateTimeOffset.UtcNow.AddSeconds(8);
            do
            {
                await Task.Delay(100);
                if (Volatile.Read(ref cycles) > before && !this.restart && Current.Conn == Conn.Online)
                    return new(RepairState.Ready);
            } while (!stop.IsCancellationRequested && DateTimeOffset.UtcNow < until);
            return new(RepairState.Failed, Current.Detail);
        }
        finally { repair.Release(); }
    }

    async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            if (restart) { StopOwned(); restart = false; }
            if (retry) { retry = false; gaveUp = false; restarts.Clear(); }
            Emit(await Tick());
            Interlocked.Increment(ref cycles);
            try { await kick.WaitAsync(Timing.Poll, stop.Token); } catch (OperationCanceledException) { return; }
        }
    }

    async Task<Status> Tick()
    {
        try
        {
            var r = await Launch.Send(new Request { Op = Ops.Snapshot }, Timing.Probe);
            if (!r.Ok || r.Snapshot is null) return Down(Fmt.Err(r));
            last = r.Snapshot; lastAt = DateTimeOffset.UtcNow;
            return new(Conn.Online, last, lastAt, Owns ? Copy.Conn.OwnedBroker : Copy.Conn.Online, Owns, last.ElevatedBroker);
        }
        catch (Exception e) when (!stop.IsCancellationRequested)
        {
            return Supervise(e);
        }
    }

    Status Supervise(Exception e)
    {
        if (owned is not null)
        {
            if (!owned.HasExited)
                return DateTimeOffset.UtcNow - ownedAt < TimeSpan.FromSeconds(8) ? Wait(Copy.Conn.Connecting) : Down(string.Format(Copy.Conn.Unreachable, Fmt.Err(e)));
            var code = owned.ExitCode;
            owned.Dispose(); owned = null;
            restarts.RemoveAll(t => DateTimeOffset.UtcNow - t > Timing.RestartWindow);
            if (restarts.Count >= Timing.MaxRestarts) gaveUp = true;
            if (gaveUp) return Down(string.Format(Copy.Conn.BrokerGaveUp, Timing.MaxRestarts));
            return StartOwned() ?? Wait(string.Format(Copy.Conn.BrokerExited, code));
        }
        if (gaveUp) return Down(string.Format(Copy.Conn.BrokerGaveUp, Timing.MaxRestarts));
        // A pipe that exists but doesn't answer belongs to someone else (e.g. the service); never start a second broker.
        if (PipeExists()) return Down(string.Format(Copy.Conn.Unreachable, Fmt.Err(e)));
        return StartOwned() ?? Wait(Copy.Conn.Connecting);
    }

    Status? StartOwned()
    {
        var path = Launch.HostPath(backend);
        if (!File.Exists(path)) return Down(string.Format(Copy.Conn.HostMissing, Launch.HostDir(backend)));
        try { owned = Launch.Broker(backend); ownedAt = DateTimeOffset.UtcNow; restarts.Add(ownedAt); return null; }
        catch (Exception ex) { return Down($"{Copy.Tray.StartFailed}: {ex.Message}"); }
    }

    static bool PipeExists()
    {
        try { return Directory.EnumerateFiles(@"\\.\pipe\").Any(p => p.EndsWith(Protocol.Pipe, StringComparison.OrdinalIgnoreCase)); }
        catch { return false; }
    }

    Status Down(string detail) => new(Conn.Offline, last, lastAt, detail, Owns, false);
    Status Wait(string detail) => new(Conn.Connecting, last, lastAt, detail, Owns, false);

    void Emit(Status s) { Current = s; publish(s); }

    /// <summary>Stops only the broker this tray started.</summary>
    public void StopOwned()
    {
        try { if (owned is { HasExited: false }) { owned.Kill(); owned.WaitForExit(3000); } } catch { }
        owned?.Dispose(); owned = null;
    }

    public void Dispose() { stop.Cancel(); kick.Release(); }
}
