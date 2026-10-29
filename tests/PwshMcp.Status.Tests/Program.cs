using System.IO;
using System.Reflection;
using System.Text.Json;
using PwshMcp.Shared;
using PwshMcp.Tray;

internal static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static class Keys
    {
        public const string Host = "PwshMcp.Host", Broker = "PwshMcp.Host.Broker", Grants = "grants", Stamps = "sessionStamps";
        public const string Sweep = "Sweep", Restore = "Restore", Store = "store", Path = "path";
    }
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    static object Field(object o, string name) => o.GetType().GetField(name, Private)!.GetValue(o)!;
    static void Call(object o, string name) => o.GetType().GetMethod(name, Private)!.Invoke(o, null);
    static int Main()
    {
        var type = Assembly.Load(Keys.Host).GetType(Keys.Broker)!;
        var broker = Activator.CreateInstance(type, true)!;
        var grants = (Dictionary<string, AccessInfo>)Field(broker, Keys.Grants);
        var now = DateTimeOffset.UtcNow;
        var pending = new AccessInfo { Id = "pending", State = AccessState.Pending, CreatedAt = now.AddHours(-25) };
        var duration = new AccessInfo { Id = "duration", State = AccessState.Active, ExpiresAt = now.AddSeconds(-1) };
        var session = new AccessInfo { Id = "session", State = AccessState.Active, SessionId = -1 };
        var active = new AccessInfo { Id = "active", State = AccessState.Active, ExpiresAt = now.AddHours(1) };
        var recent = new AccessInfo { Id = "recent", State = AccessState.Pending };
        foreach (var g in new[] { pending, duration, session, active, recent }) grants.Add(g.Id, g);
        ((Dictionary<string, long>)Field(broker, Keys.Stamps))[session.Id] = long.MaxValue;
        Call(broker, Keys.Sweep);
        Check(pending.EndReason == AccessEnd.RequestTimeout && pending.State == AccessState.Expired, "pending timeout");
        Check(duration.EndReason == AccessEnd.Duration && duration.State == AccessState.Expired, "grant duration");
        Check(session.EndReason == AccessEnd.Session && session.State == AccessState.Expired, "session invalidation");
        Check(active.State == AccessState.Active && recent.State == AccessState.Pending, "live grants unaffected");
        var path = System.IO.Path.GetTempFileName();
        try
        {
            var legacy = new AccessInfo { Id = "legacy", State = AccessState.Expired };
            grants.Add(legacy.Id, legacy);
            File.WriteAllText(path, JsonSerializer.Serialize(new { Grants = grants.Values, Jobs = Array.Empty<JobInfo>(), Fingerprints = new Dictionary<string, string>() }, Protocol.Json));
            var store = Field(broker, Keys.Store);
            store.GetType().GetField(Keys.Path, Private)!.SetValue(store, path);
            Call(broker, Keys.Restore);
            Check(grants[active.Id].EndReason == AccessEnd.Restart && grants[recent.Id].EndReason == AccessEnd.Restart, "restart invalidation");
            Check(grants[pending.Id].EndReason == AccessEnd.RequestTimeout && grants[duration.Id].EndReason == AccessEnd.Duration, "ended reasons survive restore");
            foreach (var lang in new[] { Lang.Zh, Lang.En })
            {
                L.Current = lang;
                Check(Fmt.Name(grants[pending.Id]) == Copy.State.TimedOut, "timeout label");
                Check(Fmt.Name(grants[duration.Id]) == Copy.State.Ended, "ended label");
                foreach (var id in new[] { session.Id, active.Id, legacy.Id }) Check(Fmt.Name(grants[id]) == Copy.State.Expired, "expiry label");
                Check(Fmt.Name(JobState.Expired) == Copy.State.TimedOut, "waiting job timeout label");
                Check(Fmt.Name(new AccessInfo { State = AccessState.Revoked, EndReason = AccessEnd.Duration }) == Copy.State.Revoked, "other states unchanged");
            }
        }
        finally { File.Delete(path); }
        Console.WriteLine("PASS: timeout, duration, session, restart, persisted reasons, legacy data and bilingual labels");
        return 0;
    }
}
