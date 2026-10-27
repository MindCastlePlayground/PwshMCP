using System.IO;
using PwshMcp.Shared;

namespace PwshMcp.Tray;

public enum Tone { Wait, On, Off, Bad, Info }

/// <summary>Display formatting: time, state, names and errors. Dates follow the interface language, not the OS locale.</summary>
public static class Fmt
{
    public static string Ago(DateTimeOffset at)
    {
        var d = DateTimeOffset.UtcNow - at;
        if (d.TotalSeconds < 45) return Copy.Time.JustNow;
        if (d.TotalMinutes < 60) return string.Format(Copy.Time.Minutes, (int)d.TotalMinutes);
        if (d.TotalHours < 24) return string.Format(Copy.Time.Hours, (int)d.TotalHours);
        return string.Format(Copy.Time.Days, (int)d.TotalDays);
    }

    public static string Left(DateTimeOffset end)
    {
        var d = end - DateTimeOffset.UtcNow;
        if (d <= TimeSpan.Zero) return Copy.State.Expired;
        if (d.TotalHours < 1) return string.Format(Copy.Time.Left, string.Format(Copy.Time.MinutesShort, Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))));
        return string.Format(Copy.Time.Left, string.Format(Copy.Time.HoursMin, (int)d.TotalHours, d.Minutes));
    }

    public static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString(Copy.Time.Clock, L.Culture);
    public static string ClockSec(DateTimeOffset at) => at.ToLocalTime().ToString(Copy.Time.ClockSec, L.Culture);
    public static string Full(DateTimeOffset at) => at.ToLocalTime().ToString(Copy.Time.Full, L.Culture);
    public static string Stamp(DateTimeOffset at) => $"{Full(at)} · {Ago(at)}";

    public static string Name(AccessState s) => s switch
    {
        AccessState.Pending => Copy.State.Pending, AccessState.Active => Copy.State.Active, AccessState.Denied => Copy.State.Denied,
        AccessState.Revoked => Copy.State.Revoked, _ => Copy.State.Expired
    };

    public static Tone Tone(AccessState s) => s switch { AccessState.Pending => Tray.Tone.Wait, AccessState.Active => Tray.Tone.On, AccessState.Denied => Tray.Tone.Bad, _ => Tray.Tone.Off };

    public static string Name(JobState s) => s switch
    {
        JobState.NeedsApproval => Copy.State.NeedsApproval, JobState.NeedsSession => Copy.State.NeedsSession, JobState.Queued => Copy.State.Queued,
        JobState.Running => Copy.State.Running, JobState.Succeeded => Copy.State.Succeeded, JobState.Failed => Copy.State.Failed,
        JobState.Cancelled => Copy.State.Cancelled, JobState.Rejected => Copy.State.Rejected, JobState.Expired => Copy.State.Expired, _ => Copy.State.Unknown
    };

    public static Tone Tone(JobState s) => s switch
    {
        JobState.NeedsApproval or JobState.NeedsSession => Tray.Tone.Wait,
        JobState.Queued or JobState.Running => Tray.Tone.Info,
        JobState.Succeeded => Tray.Tone.On,
        JobState.Failed or JobState.Rejected or JobState.OutcomeUnknown => Tray.Tone.Bad,
        _ => Tray.Tone.Off
    };

    public static bool Live(JobState s) => s is JobState.NeedsApproval or JobState.NeedsSession or JobState.Queued or JobState.Running;

    public static string Err(Exception e) => e switch
    {
        OperationCanceledException or TimeoutException => Copy.Err.Timeout,
        FileNotFoundException => Copy.Err.NoPipe,
        IOException io when io.Message == Names.WrongBroker => string.Format(Copy.Err.WrongBroker, Launch.HostPath(Launch.Backend)),
        IOException io => io.Message,
        _ => string.Format(Copy.Err.Generic, e.Message)
    };

    public static string Err(Response r) => r.Error switch
    {
        Codes.DescriptionRequired => Copy.Err.DescriptionRequired,
        Codes.TaskNameRequired => Copy.Err.TaskNameRequired,
        var e when string.IsNullOrWhiteSpace(e) => Copy.Err.NoResponse,
        var e => e
    };

    /// <summary>Agent-supplied text, or the explicit "old record" fallback. Never derived from code or client name.</summary>
    public static string Or(string? text, string missing) => string.IsNullOrWhiteSpace(text) ? missing : text.Trim();
    public static string OneLine(string? text, string missing) => Or(text, missing).ReplaceLineEndings(" ");

    /// <summary>
    /// Readable client name: the configured/declared display name, else a human-looking key (older records), else "Unnamed client".
    /// Hash-like keys never become the headline; they stay in the scope block and diagnostics.
    /// </summary>
    public static string Who(string? name, string? key)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        var k = key?.Trim() ?? "";
        if (k.Length is > 0 and <= 40 && !(k.Length >= 16 && k.All(Uri.IsHexDigit))) return k;
        return Copy.Who.Unnamed;
    }

    public static string Key(string? key) => string.IsNullOrWhiteSpace(key) ? Copy.Who.NoKey : key;
}
