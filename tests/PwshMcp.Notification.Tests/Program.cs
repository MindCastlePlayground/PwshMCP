using PwshMcp.Shared;
using PwshMcp.Tray;
using Monitor = PwshMcp.Tray.Monitor;

internal static class Program
{
    static class Keys
    {
        public const string First = "first", Later = "later", Missing = "missing";
        public const string Live = "live", Done = "done", Ended = "ended", Text = "hello", Err = "err", Code = "Get-Item ", RunCode = "Get-Date";
        public const string Riff = "RIFF", Wave = "WAVE", Fmt = "fmt ", Data = "data";
    }

    static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
    }

    static void QueueCases()
    {
        var prefs = new NoticePrefs();
        Check(!prefs.Manual && prefs.Auto && !prefs.Sound, "notification preference defaults (sound off)");

        Check(Hushes.Of(false, false, true, false) == Hush.Full, "full screen holds notices even with automatic quiet off");
        Check(Hushes.Of(true, true, true, true) == Hush.Full, "full screen wins over every preference");
        Check(Hushes.Of(true, false, false, false) == Hush.Manual, "manual quiet");
        Check(Hushes.Of(false, true, false, true) == Hush.Auto && Hushes.Of(false, false, false, true) == Hush.None, "Windows quiet time only with the automatic preference");
        Check(Hushes.Of(false, true, false, false) == Hush.None, "nothing holds notices back");

        // Full screen: nothing is presented or warmed; the request is presented once when it ends.
        var held = new NoticeQueue();
        int warms = 0;
        foreach (var full in new[] { true, true, false, false, false })
            if (held.Next([Keys.First], Hushes.Of(false, false, full, false) != Hush.None) is { } id) { held.Seen(id); warms++; }
        Check(warms == 1, "deferred notice appears after full screen and warms the window once");
        held.Dismiss([Keys.First]);
        Check(held.Next([Keys.First], false) is null, "dismissed request is not presented or warmed again");

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

    static Snapshot History() => new()
    {
        Grants = [Pending(Keys.First), new() { Id = Keys.Live, State = AccessState.Active }, new() { Id = Keys.Ended, State = AccessState.Revoked }],
        Jobs =
        [
            new() { Id = Keys.Live, State = JobState.Running },
            new() { Id = Keys.Done, State = JobState.Succeeded, Result = new ExecutionResult() }
        ]
    };

    static Status Online(Snapshot s) => new(Conn.Online, s, DateTimeOffset.UtcNow, "", false, false);

    /// <summary>Lean/rehydrate, on-demand output and bounded memory. Fake transport only; no pipe is opened.</summary>
    static async Task LeanCases()
    {
        var setup = MakeMain([], (_, _) => throw new InvalidOperationException("Unexpected admin call"));
        var main = setup.Main;
        try
        {
            main.Seed(Online(History()), Tab.Requests);
            Check(main.Requests.Count == 1 && main.Access.Count == 2 && main.Tasks.Count == 2, "full history loaded");
            var cursors = new List<int>();
            main.Send = r =>
            {
                cursors.Add(r.Cursor);
                Check(r.Op == Ops.Output && r.Id == Keys.Done, "output read uses the existing output operation");
                var chunk = r.Cursor == 0 ? Keys.Text : "";
                return Task.FromResult(new Response { Output = chunk, Cursor = r.Cursor + chunk.Length, Job = new JobInfo { Id = Keys.Done, Result = new ExecutionResult { Error = Keys.Err } } });
            };
            var done = main.Tasks.Single(t => t.Id == Keys.Done);
            Check(done.Output == "" && !done.Holds, "output not loaded until asked");
            done.Toggle.Execute(null);
            await Task.Yield();
            Check(done.Shown && done.Output == Keys.Text && done.Errors == Keys.Err && done.Holds && cursors.SequenceEqual([0, Keys.Text.Length]), "output loaded in chunks on Show");
            done.Shown = false;
            Check(!done.Holds && done.Output == "", "hiding releases loaded output");

            main.Shrink();
            Check(main.Lean && main.Requests.Count == 1 && main.Access.Count == 1 && main.Access[0].IsActive && main.Tasks.Count == 1 && main.Tasks[0].Id == Keys.Live,
                "lean keeps pending requests, active access and live tasks only");
            main.Apply(Online(History()));
            Check(main.Access.Count == 1 && main.Tasks.Count == 1, "polls stay lean");
            main.Grow();
            Check(!main.Lean && main.Access.Count == 2 && main.Tasks.Count == 2, "opening rebuilds history from the last snapshot");

            main.Viewing = true;
            main.Approved(Keys.Missing);
            Check(main.Remembered == 2, "seen request and pending handoff remembered");
            main.Apply(Online(new Snapshot()));
            Check(main.Remembered == 0, "remembered ids bounded by the snapshot");

            const int chunk = 65536;
            main.Send = r => Task.FromResult(new Response { Output = new string('x', chunk), Cursor = r.Cursor + chunk });
            var (text, _, cut) = await main.Read(Keys.Done);
            Check(cut && text.Length == Limits.Output, "output capped in memory");
        }
        finally { setup.Monitor.Dispose(); }
    }

    /// <summary>Archived tasks arrive without code; selecting one reads the full command, deselecting releases it. Fake transport only.</summary>
    static async Task CommandCases()
    {
        var setup = MakeMain([], (_, _) => throw new InvalidOperationException("Unexpected admin call"));
        var main = setup.Main;
        try
        {
            var snap = History() with { Jobs = [.. History().Jobs, new() { Id = Keys.Ended, State = JobState.Rejected }] };
            main.Seed(Online(snap), Tab.Tasks);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var statuses = new List<string>();
            var fail = false;
            main.Send = async r =>
            {
                if (r.Op == Ops.Output)
                    return new Response { Output = r.Cursor == 0 ? Keys.Text : "", Cursor = r.Cursor + (r.Cursor == 0 ? Keys.Text.Length : 0) };
                Check(r.Op == Ops.Status, "command read uses the existing status operation");
                statuses.Add(r.Id);
                if (r.Id == Keys.Ended) await gate.Task;
                if (fail) return new Response { Ok = false, Error = Keys.Err };
                return new Response { Job = new JobInfo { Id = r.Id, Run = new RunSpec { Code = Keys.Code + r.Id } } };
            };
            var done = main.Tasks.Single(t => t.Id == Keys.Done);
            Check(done.Cmd.Code == "" && statuses.Count == 0, "snapshot carries no code and nothing is prefetched");
            main.SelTask = done;
            await Task.Yield();
            Check(done.HoldsCmd && done.Cmd.Code == Keys.Code + Keys.Done && statuses.SequenceEqual([Keys.Done]), "selection reads the full command");
            main.Apply(Online(snap));
            Check(done.Cmd.Code == Keys.Code + Keys.Done && statuses.Count == 1, "lightweight poll keeps the fetched command without refetching");
            done.Toggle.Execute(null);
            await Task.Yield();
            Check(done.Output == Keys.Text && done.Cmd.Code == Keys.Code + Keys.Done, "output toggle stays separate from the command");
            done.Shown = false;
            Check(done.HoldsCmd, "hiding output keeps the selected command");
            main.Tab = Tab.Requests;
            Check(!done.HoldsCmd && done.Cmd.Code == "", "leaving the tab releases the command");
            main.Tab = Tab.Tasks;
            await Task.Yield();
            Check(done.HoldsCmd && statuses.Count == 2, "returning to the tab reads it again");

            var ended = main.Tasks.Single(t => t.Id == Keys.Ended);
            main.SelTask = ended;
            Check(!done.HoldsCmd && done.Cmd.Code == "", "deselection releases the command");
            main.SelTask = null;
            gate.SetResult();
            await Task.Delay(10);
            Check(!ended.HoldsCmd && ended.Cmd.Code == "" && ended.Notice == "", "a reply after deselection is dropped");
            main.SelTask = ended;
            await Task.Delay(10);
            Check(ended.HoldsCmd && ended.Cmd.Code == Keys.Code + Keys.Ended, "rejected task without a result shows its command");
            main.SelTask = null;

            fail = true;
            main.SelTask = done;
            await Task.Yield();
            Check(!done.HoldsCmd && done.NoticeBad && done.Notice.Contains(Keys.Err), "a failed read is shown as an error");
            main.Shrink();
            Check(!done.HoldsCmd && !ended.HoldsCmd, "teardown holds no command");
        }
        finally { setup.Monitor.Dispose(); }
    }

    /// <summary>A live command survives a completed summary poll, then rehydrates once when the snapshot omits it.</summary>
    static async Task RunningCommandCases()
    {
        var setup = MakeMain([], (_, _) => throw new InvalidOperationException("Unexpected admin call"));
        var main = setup.Main;
        try
        {
            var running = new JobInfo { Id = Keys.Live, State = JobState.Running, Run = new RunSpec { Code = Keys.RunCode } };
            var liveSnapshot = new Snapshot { Jobs = [running] };
            main.Seed(Online(liveSnapshot), Tab.Tasks);
            var task = main.Tasks.Single();
            main.SelTask = task;
            Check(task.Cmd.Code == Keys.RunCode, "selected running command comes from its live snapshot");

            var release = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
            var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int statuses = 0;
            task.PropertyChanged += (_, _) => { if (task.Cmd.Code == Keys.Code + Keys.Live) restored.TrySetResult(); };
            main.Send = r =>
            {
                Check(r.Op == Ops.Status && r.Id == Keys.Live, "completed summary rehydrates through status");
                statuses++;
                return release.Task;
            };
            var completed = new Snapshot { Jobs = [new JobInfo { Id = Keys.Live, State = JobState.Succeeded, Result = new ExecutionResult() }] };
            main.Apply(Online(completed));
            Check(task.Cmd.Code == "" && !task.HoldsCmd && statuses == 1, "summary drops live code and starts one rehydration");
            main.Apply(Online(completed));
            Check(statuses == 1, "pending rehydration is not duplicated by another Apply");
            release.SetResult(new Response { Job = new JobInfo { Id = Keys.Live, Run = new RunSpec { Code = Keys.Code + Keys.Live } } });
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(task.Cmd.Code == Keys.Code + Keys.Live && task.HoldsCmd && statuses == 1, "rehydration restores the full command");
        }
        finally { setup.Monitor.Dispose(); }
    }

    /// <summary>Output requests are generation-bound: hide/reopen starts a fresh read and stale replies are ignored.</summary>
    static async Task OutputRaceCases()
    {
        var setup = MakeMain([], (_, _) => throw new InvalidOperationException("Unexpected admin call"));
        var main = setup.Main;
        try
        {
            var snap = History();
            main.Seed(Online(snap), Tab.Tasks);
            var done = main.Tasks.Single(t => t.Id == Keys.Done);
            var first = new TaskCompletionSource<Response>();
            var fresh = new TaskCompletionSource<Response>();
            var freshRendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            done.PropertyChanged += (_, _) =>
            {
                if (done.Shown && done.Output == Keys.Later) freshRendered.TrySetResult();
            };
            int reads = 0;
            main.Send = r =>
            {
                if (r.Op == Ops.Status)
                    return Task.FromResult(new Response { Job = new JobInfo { Id = r.Id, Run = new RunSpec { Code = Keys.Code + r.Id } } });
                Check(r.Op == Ops.Output && r.Id == Keys.Done, "output requests target the completed task");
                if (r.Cursor > 0) return Task.FromResult(new Response { Cursor = r.Cursor });
                if (Interlocked.Increment(ref reads) == 1) return first.Task;
                return fresh.Task;
            };
            main.SelTask = done;
            done.Toggle.Execute(null);
            Check(reads == 1 && done.Shown && done.Toggle.CanExecute(null), "loading output still allows hide");
            done.Toggle.Execute(null);
            Check(!done.Shown && !done.Holds, "hiding during load clears displayed output and cache");
            done.Toggle.Execute(null);
            Check(reads == 2, "reopen starts a second request immediately");
            fresh.SetResult(new Response { Output = Keys.Later, Cursor = Keys.Later.Length });
            await freshRendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(done.Shown && done.Output == Keys.Later && done.Holds && reads == 2, "reopen starts a fresh request and displays its reply");
            first.SetResult(new Response { Output = Keys.Text, Cursor = Keys.Text.Length });
            Check(done.Shown && done.Output == Keys.Later && reads == 2, "older reply cannot overwrite fresh output");

            var other = main.Tasks.Single(t => t.Id == Keys.Live);
            done.Toggle.Execute(null);
            // Reopen output, then switch selection and verify task state is released.
            done.Toggle.Execute(null);
            Check(done.Shown, "output can be reopened");
            main.SelTask = other;
            Check(!done.Shown && !done.Holds, "switching selected task clears output visibility and cache");
            other.Shown = true;
            main.Tab = Tab.Requests;
            Check(!other.Shown && !other.Holds, "leaving task tab clears output visibility and cache");
        }
        finally { setup.Monitor.Dispose(); }
    }

    static void ReclaimCases()
    {
        var hold = true; var drops = 0;
        var r = new Reclaim(TimeSpan.FromSeconds(10), () => hold, () => drops++);
        Check(!r.Armed, "idle timer starts disarmed");
        r.Arm();
        Check(r.Armed, "hide arms teardown");
        Check(!r.Fire() && r.Armed && drops == 0, "held (visible, notice, submit) does not tear down and waits again");
        hold = false;
        Check(r.Fire() && !r.Armed && drops == 1 && r.Drops == 1, "teardown once after idle");
        r.Arm(); r.Cancel();
        Check(!r.Armed && drops == 1, "explicit show cancels teardown");
    }

    /// <summary>Format and envelope only; nothing is played.</summary>
    static void ChimeCases()
    {
        var wav = Chime.Wav();
        string Tag(int at) => System.Text.Encoding.ASCII.GetString(wav, at, 4);
        Check(Tag(0) == Keys.Riff && Tag(8) == Keys.Wave && Tag(12) == Keys.Fmt && Tag(36) == Keys.Data, "wave container");
        Check(BitConverter.ToInt16(wav, 20) == 1 && BitConverter.ToInt16(wav, 22) == Sound.Channels && BitConverter.ToInt32(wav, 24) == Sound.Rate && BitConverter.ToInt16(wav, 34) == Sound.Bits, "16-bit mono PCM");
        Check(BitConverter.ToInt32(wav, 4) == wav.Length - 8 && BitConverter.ToInt32(wav, 40) == wav.Length - 44, "wave sizes");
        var pcm = Chime.Samples();
        Check(Math.Abs((double)pcm.Length / Sound.Rate - 0.45) < 0.01, "about 0.45 s");
        static double Peak(IEnumerable<short> xs) => xs.Max(x => Math.Abs((int)x)) / (double)short.MaxValue;
        static int Ms(double m) => (int)(Sound.Rate * m / 1000);
        Check(Peak(pcm) is > 0.1 and <= 0.3, "quiet peak");
        Check(pcm[0] == 0 && pcm[^1] == 0, "starts and ends at silence");
        Check(Peak(pcm.Take(Ms(2))) < 0.02, "smooth attack, no click");
        Check(Peak(pcm.Skip(pcm.Length - Ms(5))) < 0.02, "smooth release, no click");
        Check(Peak(pcm.Take(Ms(100))) > 0.1 && Peak(pcm.Skip(Ms(200)).Take(Ms(150))) > 0.05, "two notes audible in sequence");
    }

    static void GlyphCases()
    {
        static bool Any(System.Drawing.Bitmap b, Func<System.Drawing.Color, bool> ok)
        {
            for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++) if (ok(b.GetPixel(x, y))) return true;
            return false;
        }
        static bool Indigo(System.Drawing.Color c) => c.A > 200 && c.B - c.R > 60;
        static bool Amber(System.Drawing.Color c) => c.A > 200 && c.R > 180 && c.B < 90;
        foreach (var size in Glyphs.TraySizes)
            foreach (var light in new[] { false, true })
                foreach (var pending in new[] { false, true })
                {
                    var at = $"{size}px light={light} pending={pending}";
                    using var idle = Glyphs.Draw(Glyph.Idle, size, light, pending);
                    using var active = Glyphs.Draw(Glyph.Active, size, light, pending);
                    using var offline = Glyphs.Draw(Glyph.Offline, size, light, pending);
                    Check(idle.Width == size && active.Height == size && Any(idle, c => c.A > 200) && Any(offline, c => c.A > 100), $"glyphs render at {at}");
                    Check(Any(active, Indigo) && !Any(idle, Indigo) && !Any(offline, Indigo), $"active is the only indigo state at {at}");
                    Check(pending ? Any(idle, Amber) && Any(active, Amber) && Any(offline, Amber) : !Any(idle, Amber) && !Any(active, Amber), $"amber dot only when pending at {at}");
                    if (!pending) Check(!Any(offline, c => c.A > 160), $"offline is dimmed at {at}");
                }
        var ico = Glyphs.AppIco();
        Check(BitConverter.ToInt16(ico, 2) == 1 && BitConverter.ToInt16(ico, 4) == Glyphs.AppSizes.Length, "app icon frames");
    }

    [STAThread]
    static void Main()
    {
        QueueCases();
        ApprovalCases().GetAwaiter().GetResult();
        SnapshotCases().GetAwaiter().GetResult();
        LeanCases().GetAwaiter().GetResult();
        CommandCases().GetAwaiter().GetResult();
        RunningCommandCases().GetAwaiter().GetResult();
        OutputRaceCases().GetAwaiter().GetResult();
        ReclaimCases();
        ChimeCases();
        GlyphCases();
        Console.WriteLine("PASS: notification queue, preferences, full-screen hold, lean/rehydrate, output release, selected command read/release, reclaim debounce, chime format and envelope, glyph states");
    }
}
