using System.Net.Sockets;
using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests.Process;

/// <summary>Mirrors the <c>PortRowViewModel.CanReclaim</c> predicate. The WPF view-model is not
/// referenceable from this Core test assembly, so the pure predicate logic is verified here against
/// real <see cref="PortReport"/> values. The full kill → poll → relaunch orchestration in
/// <c>GuiAppService.ReclaimAsync</c> has no clean mock seam (real-OS kill) and is verified manually
/// on screen.</summary>
public class ReclaimEligibilityTests
{
    private static bool EligibleForReclaim(PortReport r) =>
        r.Tier == PortTier.LikelyOrphaned && r.OwnerAppId is not null;

    private static PortReport Report(PortTier tier, string? ownerAppId)
    {
        var evidence = new ProcessEvidence(
            Pid: 1234,
            ProcessName: "node",
            ExePath: null,
            CommandLine: null,
            ExeDir: null,
            StartedAt: null,
            ParentAlive: false);

        return new PortReport(
            Port: 5173,
            Family: AddressFamily.InterNetwork,
            Evidence: evidence,
            OwnerAppId: ownerAppId,
            OwnerAppName: ownerAppId is null ? null : "Sample App",
            Tier: tier);
    }

    [Theory]
    [InlineData(PortTier.LikelyOrphaned, "app-1", true)]
    [InlineData(PortTier.LikelyOrphaned, null, false)]
    [InlineData(PortTier.Managed, "app-1", false)]
    [InlineData(PortTier.Registered, "app-1", false)]
    [InlineData(PortTier.Unknown, "app-1", false)]
    [InlineData(PortTier.Unknown, null, false)]
    public void CanReclaim_OnlyForLikelyOrphanedWithOwner(PortTier tier, string? ownerAppId, bool expected)
    {
        var report = Report(tier, ownerAppId);
        Assert.Equal(expected, EligibleForReclaim(report));
    }
}
