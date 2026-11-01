using System.Windows.Threading;

namespace PwshMcp.Tray;

/// <summary>
/// Debounced teardown of the main window once it is hidden or minimized. It never tears down while held
/// (visible window, open notice, approval or repair in flight); a held fire simply waits another period.
/// </summary>
public sealed class Reclaim
{
    readonly DispatcherTimer timer;
    readonly Func<bool> hold;
    readonly Action drop;

    public Reclaim(TimeSpan delay, Func<bool> hold, Action drop)
    {
        this.hold = hold; this.drop = drop;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };
        timer.Tick += (_, _) => Fire();
    }

    public bool Armed => timer.IsEnabled;
    /// <summary>Teardowns actually performed.</summary>
    public int Drops { get; private set; }

    /// <summary>(Re)starts the countdown.</summary>
    public void Arm() { timer.Stop(); timer.Start(); }
    public void Cancel() => timer.Stop();

    /// <summary>Tears down unless held. True when it did.</summary>
    public bool Fire()
    {
        timer.Stop();
        if (hold()) { timer.Start(); return false; }
        drop();
        Drops++;
        return true;
    }
}
