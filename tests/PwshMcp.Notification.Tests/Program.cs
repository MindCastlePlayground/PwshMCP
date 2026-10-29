using PwshMcp.Shared;
using PwshMcp.Tray;
using Monitor = PwshMcp.Tray.Monitor;

internal static class Program
{
    static class Keys
    {
        public const string First = "first", Later = "later", Missing = "missing";
    }

    static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
    }

    static void QueueCases()
    {
        var prefs = new NoticePrefs();
        Check(!prefs.Manual && prefs.Auto, "notification preference defaults");

        var queue = new NoticeQueue();
        Check(queue.Next([Keys.First, Keys.Later], false) == Keys.First, "first pending notification");
        queue.Seen(Keys.First);
        Check(queue.Next([Keys.First, Keys.Later], false) == Keys.Later, "deduplicates seen notification");
        Check(queue.Next([Keys.First, Keys.Later], true) is null, "mute defers notification");
        queue.Defer(Keys.First);
        Check(queue.Next([Keys.First, Keys.Later], false) == Keys.First, "defer permits retry");
        queue.Dismiss([Keys.First, Keys.Later]);
        Check(queue.Next([Keys.First, Keys.Later], false) is null, "dismiss suppresses batch");
        Check(queue.Next([Keys.Later], false) is null, "remaining dismissed id stays suppressed");
        Check(queue.Next([Keys.First, Keys.Later], false) == Keys.First, "removed pending id is pruned");
    }

    static (MainVm Main, Monitor Monitor) MakeMain(List<AccessInfo> grants, Coord.Transport send, bool online = true, bool ready = true)
    {
        var monitor = new Monitor(null, _ => { });
        MainVm? main = null;
        var coord = new Coord(g => main?.Apply(g), send);
        main = new MainVm(monitor, coord);
        main.Seed(new Status(online ? Conn.Online : Conn.Offline, new Snapshot { Grants = grants }, DateTimeOffset.UtcNow, "", false, false), Tab.Requests);
        main.Seed(new GateInfo(ready ? Gate.Ready : Gate.Down, "", DateTimeOffset.UtcNow));
        return (main, monitor);
    }

    static AccessInfo Pending(string id) => new() { Id = id, State = AccessState.Pending };
    static AdminResponse Active(string id) => new() { Ok = true, Grant = new AccessInfo { Id = id, State = AccessState.Active } };

    static async Task ApprovalCases()
    {
        int sends = 0;
        var setup = MakeMain([Pending(Keys.First)], (r, _) =>
        {
            sends++;
            return Task.FromResult(Active(r.Id));
        });
        try
        {
            var result = await QuickApproval.Run(setup.Main, [Keys.First, Keys.First, Keys.Missing], Life.FourHours);
            Check(result == (1, 1) && sends == 1, "stale id skipped and duplicates counted once");
            var grant = setup.Main.Requests.Single();
            Check(grant.Accepted && grant.Life == Life.FourHours, "successful response latches accepted with selected lifetime");
            await grant.Submit();
            Check(sends == 1, "accepted latch prevents duplicate sends");
        }
        finally { setup.Monitor.Dispose(); }

        sends = 0;
        var offline = MakeMain([Pending(Keys.First)], (r, _) =>
        {
            sends++;
            return Task.FromResult(Active(r.Id));
        }, online: false);
        try
        {
            var result = await QuickApproval.Run(offline.Main, [Keys.First], Life.SignOut);
            Check(result == (0, 1) && sends == 0 && !offline.Main.Requests.Single().Accepted, "offline guard prevents approval");
        }
        finally { offline.Monitor.Dispose(); }

        foreach (var response in new[]
        {
            new AdminResponse { Ok = false, Error = "offline" },
            new AdminResponse { Ok = true, Grant = Pending(Keys.First) }
        })
        {
            sends = 0;
            var failed = MakeMain([Pending(Keys.First)], (r, _) =>
            {
                sends++;
                return Task.FromResult(response);
            });
            try
            {
                var result = await QuickApproval.Run(failed.Main, [Keys.First], Life.SignOut);
                Check(result == (0, 1) && sends == 1 && !failed.Main.Requests.Single().Accepted, "failed or non-active response does not latch success");
            }
            finally { failed.Monitor.Dispose(); }
        }
    }

    static async Task SnapshotCases()
    {
        var ids = new List<string> { Keys.First };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AdminResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = new List<string>();
        var setup = MakeMain([Pending(Keys.First), Pending(Keys.Later)], (r, _) =>
        {
            sends.Add(r.Id);
            if (r.Id == Keys.First)
            {
                entered.SetResult();
                return release.Task;
            }
            return Task.FromResult(Active(r.Id));
        });
        try
        {
            var run = QuickApproval.Run(setup.Main, ids, Life.SignOut);
            await entered.Task;
            ids.Add(Keys.Later);
            release.SetResult(Active(Keys.First));
            var result = await run;
            Check(sends.SequenceEqual([Keys.First]) && result == (1, 0), "batch freezes ids before awaiting");
        }
        finally { setup.Monitor.Dispose(); }
    }

    static async Task Main()
    {
        QueueCases();
        await ApprovalCases();
        await SnapshotCases();
        Console.WriteLine("PASS: notification queue, preferences, stale/offline guards, outcomes and batch snapshot");
    }
}
