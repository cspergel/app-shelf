namespace AppShelf.Core.Process;

/// <summary>Result of one crash-watcher tick: updated miss counter and whether to fire the alert.</summary>
public readonly record struct CrashWatchStep(int Misses, bool Alert);

/// <summary>
/// Pure dead-man's-switch state machine. No OS calls, no timer, no dependencies.
/// The caller (MainViewModel) owns the per-card state (prevMisses) and the OS query
/// (GuiAppService.DidManagedAppExit). Unit-testable without any mock or fixture setup.
/// </summary>
public static class CrashWatch
{
    /// <summary>
    /// Advance the watcher by one tick.
    /// Increments the miss counter when <paramref name="managedAndExited"/> is true.
    /// The alert fires exactly once: when the counter first reaches
    /// <paramref name="threshold"/>. Subsequent ticks do not re-alert.
    /// Resets to zero when the app is running, cleanly stopped, or never launched.
    /// </summary>
    public static CrashWatchStep Step(bool managedAndExited, int prevMisses, int threshold)
    {
        if (!managedAndExited)
            return new CrashWatchStep(0, false);
        var newMisses = prevMisses + 1;
        return new CrashWatchStep(newMisses, newMisses == threshold);
    }
}
