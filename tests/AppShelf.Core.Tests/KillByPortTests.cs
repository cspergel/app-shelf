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

    [Fact]
    public void KillTree_ProtectedSystemProcess_OutcomeIsFailureWithReason()
    {
        // PID 4 is the Windows System process — always running, always protected.
        // taskkill /PID 4 /T /F exits non-zero with "Access is denied", so KillTree must
        // return a failing outcome with a non-empty reason, not throw an exception.
        var outcome = ProcessManager.KillTree(4);
        Assert.False(outcome.Success);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason));
    }

    [Fact]
    public void KillTree_ProtectedSystemProcess_OutcomeIsAccessDenied()
    {
        // PID 4 (System) is killed with taskkill -> "Access is denied", so KillTree must
        // classify the outcome as AccessDenied so the GUI can show a friendly, service/elevation
        // -aware message instead of the raw taskkill PID dump.
        var outcome = ProcessManager.KillTree(4);
        Assert.False(outcome.Success);
        Assert.True(outcome.AccessDenied);
    }

    [Fact]
    public void KillByPort_NothingListening_NotClassifiedAccessDenied()
    {
        // A "nothing listening" failure is not an access-denied refusal.
        var outcome = ProcessManager.KillByPort(59999);
        Assert.False(outcome.Success);
        Assert.False(outcome.AccessDenied);
    }

    [Fact]
    public void KillOutcome_Fail_PreservesReason()
    {
        // KillOutcome.Fail must carry the exact reason string so the GUI can surface it.
        const string reason = "Access is denied.";
        var outcome = KillOutcome.Fail(reason);
        Assert.False(outcome.Success);
        Assert.Equal(reason, outcome.Reason);
        Assert.False(outcome.AccessDenied); // not flagged unless explicitly classified
    }

    [Fact]
    public void KillOutcome_Fail_CanCarryAccessDeniedFlag()
    {
        var outcome = KillOutcome.Fail("Access is denied.", accessDenied: true);
        Assert.False(outcome.Success);
        Assert.True(outcome.AccessDenied);
    }

    [Fact]
    public void KillOutcome_Ok_IsSuccessWithNullReason()
    {
        Assert.True(KillOutcome.Ok.Success);
        Assert.Null(KillOutcome.Ok.Reason);
        Assert.False(KillOutcome.Ok.AccessDenied);
    }
}
