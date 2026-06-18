using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests.Process;

/// <summary>Covers the pure <see cref="CrashWatch.Step"/> dead-man's-switch state machine: reset
/// on not-exited, debounce up to threshold, alert-exactly-once at threshold, and re-alert after a
/// reset.
///
/// ProcessManager.IsManagedAndExited is a one-liner (TryGetValue &amp;&amp; HasExited); OS-integration
/// tests for process tracking are out of scope here (no clean mock seam — real-OS process).
/// Covered by manual verification (see plan MV-1/MV-2).</summary>
public class CrashWatchTests
{
    [Theory]
    // managedAndExited=false always resets to 0 and never alerts, regardless of prior misses.
    [InlineData(false, 0, 2, 0, false)] // not exited, fresh -> reset, no alert
    [InlineData(false, 1, 2, 0, false)] // not exited after one miss -> reset, no alert
    [InlineData(false, 5, 3, 0, false)] // not exited after many misses, various threshold -> reset
    // managedAndExited=true increments; alert fires exactly when newMisses == threshold.
    [InlineData(true, 0, 2, 1, false)]  // first miss -> 1, no alert
    [InlineData(true, 1, 2, 2, true)]   // reaches threshold -> 2, ALERT
    [InlineData(true, 2, 2, 3, false)]  // beyond threshold -> 3, no repeat alert
    [InlineData(true, 0, 1, 1, true)]   // threshold of 1 -> alert on first miss
    public void Step_ProducesExpectedMissesAndAlert(
        bool managedAndExited, int prevMisses, int threshold, int expectedMisses, bool expectedAlert)
    {
        var result = CrashWatch.Step(managedAndExited, prevMisses, threshold);

        Assert.Equal(expectedMisses, result.Misses);
        Assert.Equal(expectedAlert, result.Alert);
    }

    [Fact]
    public void Step_AfterAlertThenReset_ReAlertsOnNextCrash()
    {
        const int threshold = 2;

        // First crash: two consecutive missed ticks -> alert on the second.
        var t1 = CrashWatch.Step(managedAndExited: true, prevMisses: 0, threshold);
        Assert.False(t1.Alert);
        var t2 = CrashWatch.Step(managedAndExited: true, prevMisses: t1.Misses, threshold);
        Assert.True(t2.Alert);

        // App is restarted/stopped -> not exited -> counter resets, no alert.
        var reset = CrashWatch.Step(managedAndExited: false, prevMisses: t2.Misses, threshold);
        Assert.Equal(0, reset.Misses);
        Assert.False(reset.Alert);

        // Second crash from the reset state must alert again on its own second consecutive miss.
        var c1 = CrashWatch.Step(managedAndExited: true, prevMisses: reset.Misses, threshold);
        Assert.False(c1.Alert);
        var c2 = CrashWatch.Step(managedAndExited: true, prevMisses: c1.Misses, threshold);
        Assert.True(c2.Alert);
    }

    [Fact]
    public void Step_WhileExited_AlertsOnlyOnceAcrossManyTicks()
    {
        const int threshold = 2;
        int misses = 0;
        int alertCount = 0;

        // Simulate 6 consecutive crashed ticks; alert must fire exactly once.
        for (int i = 0; i < 6; i++)
        {
            var step = CrashWatch.Step(managedAndExited: true, prevMisses: misses, threshold);
            misses = step.Misses;
            if (step.Alert)
                alertCount++;
        }

        Assert.Equal(1, alertCount);
    }
}
