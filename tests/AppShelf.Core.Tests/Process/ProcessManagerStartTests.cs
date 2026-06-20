using AppShelf.Core.Models;
using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests.Process;

/// <summary>Phase 3 C2: the <c>_starting</c> claim + post-claim already-running guard reject a
/// second <see cref="ProcessManager.Start"/> for an id the manager is already tracking, instead of
/// spawning a duplicate process tree. The true concurrent TOCTOU window is timing-dependent and is
/// verified manually (MV-C2); this test pins the observable contract: a second Start for the same
/// tracked id throws. Uses a real fast-exiting process (no clean mock seam for the OS boundary).</summary>
public class ProcessManagerStartTests
{
    [Fact]
    public void Start_WithSameIdWhileTracked_ThrowsInvalidOperationException()
    {
        var dir = Path.Combine(Path.GetTempPath(), "appshelf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var manager = new ProcessManager();
            var entry = new AppEntry
            {
                Id = "dup-start",
                Name = "Dup Start",
                Cmd = "exit 0",
                Dir = dir,
                Url = "http://127.0.0.1:59998",
            };

            manager.Start(entry);

            // The entry stays in _running (present-and-exited) until a Stop/ClearManagedExit, so a
            // second Start must be rejected by the already-running guard rather than double-spawning.
            var ex = Assert.Throws<InvalidOperationException>(() => manager.Start(entry));
            Assert.Contains("already", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
