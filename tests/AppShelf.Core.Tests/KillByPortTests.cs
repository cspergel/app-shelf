using AppShelf.Core.Process;

namespace AppShelf.Core.Tests;

public class KillByPortTests
{
    [Fact]
    public void KillByPort_NothingListening_OutcomeIsFailureWithReason()
    {
        // A port in the unlikely-to-be-used high range with nothing bound.
        var outcome = ProcessManager.KillByPort(59999);
        Assert.False(outcome.Success);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason)); // e.g. "nothing listening on port 59999"
    }
}
