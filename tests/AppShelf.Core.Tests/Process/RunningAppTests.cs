using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests.Process;

/// <summary>Phase 3 D1: <see cref="ProcessManager.RunningApp.AppendLog"/> caps each log line at
/// 4096 chars so a single pathological line cannot bloat the bounded ring buffer to hundreds of MB.
/// Reaches the <c>internal</c> ctor + AppendLog + LogTail via <c>InternalsVisibleTo</c>.</summary>
public class RunningAppTests
{
    [Fact]
    public void AppendLog_WithLineLongerThan4096_IsTruncatedWithEllipsis()
    {
        // The Process is never started — RunningApp only needs the object reference for the log path.
        using var process = new System.Diagnostics.Process();
        using var running = new ProcessManager.RunningApp("log-test", process);

        running.AppendLog(new string('x', 5000));

        var line = Assert.Single(running.LogTail);
        // 4096 retained chars + the single ellipsis char = 4097 UTF-16 units.
        Assert.Equal(4097, line.Length);
        Assert.EndsWith("…", line);
        Assert.StartsWith("xxxx", line);
    }

    [Fact]
    public void AppendLog_WithShortLine_IsNotTruncated()
    {
        using var process = new System.Diagnostics.Process();
        using var running = new ProcessManager.RunningApp("log-test", process);

        running.AppendLog("a normal line");

        var line = Assert.Single(running.LogTail);
        Assert.Equal("a normal line", line);
    }
}
