using AppShelf.Core.Models;
using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests.Process;

/// <summary>Phase 3 C1: <see cref="ProcessManager.ClearManagedExit"/> removes a stale crashed
/// (present-and-exited) managed entry so a status check can "heal" once the port comes back. The
/// no-op case is a pure assertion; the exited-entry case starts a real fast-exiting process (the
/// only honest way to put the manager into the present-and-exited state).</summary>
public class ClearManagedExitTests
{
    [Fact]
    public void ClearManagedExit_OnUnknownId_DoesNotThrow()
    {
        using var manager = new ProcessManager();

        // No entry was ever tracked for this id — must be a silent no-op.
        manager.ClearManagedExit("never-launched");

        Assert.False(manager.IsManagedAndExited("never-launched"));
        Assert.False(manager.IsManaged("never-launched"));
    }

    [Fact]
    public void ClearManagedExit_OnExitedEntry_RemovesEntry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "appshelf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var manager = new ProcessManager();
            var entry = new AppEntry
            {
                Id = "fast-exit",
                Name = "Fast Exit",
                // No port -> LaunchPlanBuilder leaves the command as-is. `exit 0` ends immediately,
                // so the managed process is present-and-exited (a crash from the manager's view —
                // it never went through Stop).
                Cmd = "exit 0",
                Dir = dir,
                Url = "http://127.0.0.1:59999",
            };

            manager.Start(entry);

            // Wait briefly for the trivial process to exit.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!manager.IsManagedAndExited("fast-exit") && DateTime.UtcNow < deadline)
                Thread.Sleep(25);

            Assert.True(manager.IsManagedAndExited("fast-exit"),
                "the fast-exiting process should be tracked as present-and-exited.");

            manager.ClearManagedExit("fast-exit");

            // After clearing, the stale entry is gone — status no longer reads "crashed forever".
            Assert.False(manager.IsManagedAndExited("fast-exit"));
            Assert.False(manager.IsManaged("fast-exit"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
