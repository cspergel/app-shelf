using System.Collections.Generic;
using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests.Process;

/// <summary>Pure-walk tests for <see cref="WindowsProcessEvidenceProvider.TryFindServiceHost"/> and
/// its delegation from <c>IsServiceBacked</c>. The live WMI name lookup
/// (<c>QueryRunningServiceHostsWithNames</c>) and the elevated Stop-Service spawn are OS glue and
/// are verified manually on screen.</summary>
public class ServiceNameResolutionTests
{
    // snapshotDict maps PID -> (ParentPid, ExeName); ExeName is irrelevant to TryFindServiceHost.
    private static Dictionary<uint, (uint ParentPid, string ExeName)> Snapshot(
        params (uint Pid, uint ParentPid)[] entries)
    {
        var dict = new Dictionary<uint, (uint, string)>();
        foreach (var (pid, parent) in entries)
            dict[pid] = (parent, "proc.exe");
        return dict;
    }

    [Fact]
    public void TryFindServiceHost_TargetIsSelf_ReturnsHostPid()
    {
        var snapshot = Snapshot((1000, 1));
        var hosts = new HashSet<int> { 1000 };

        var found = WindowsProcessEvidenceProvider.TryFindServiceHost(
            1000, snapshot, hosts, WindowsProcessEvidenceProvider.MaxAncestryDepth, out var hostPid);

        Assert.True(found);
        Assert.Equal(1000, hostPid);
    }

    [Fact]
    public void TryFindServiceHost_TargetIsDirectChild_ReturnsParentPid()
    {
        var snapshot = Snapshot((2000, 1000), (1000, 1));
        var hosts = new HashSet<int> { 1000 };

        var found = WindowsProcessEvidenceProvider.TryFindServiceHost(
            2000, snapshot, hosts, WindowsProcessEvidenceProvider.MaxAncestryDepth, out var hostPid);

        Assert.True(found);
        Assert.Equal(1000, hostPid);
    }

    [Fact]
    public void TryFindServiceHost_TwoLevelsDeep_ReturnsGrandparentPid()
    {
        var snapshot = Snapshot((3000, 2000), (2000, 1000), (1000, 1));
        var hosts = new HashSet<int> { 1000 };

        var found = WindowsProcessEvidenceProvider.TryFindServiceHost(
            3000, snapshot, hosts, WindowsProcessEvidenceProvider.MaxAncestryDepth, out var hostPid);

        Assert.True(found);
        Assert.Equal(1000, hostPid);
    }

    [Fact]
    public void TryFindServiceHost_NotServiceBacked_ReturnsFalse()
    {
        var snapshot = Snapshot((5000, 100), (100, 1));
        var hosts = new HashSet<int>();

        var found = WindowsProcessEvidenceProvider.TryFindServiceHost(
            5000, snapshot, hosts, WindowsProcessEvidenceProvider.MaxAncestryDepth, out var hostPid);

        Assert.False(found);
        Assert.Equal(0, hostPid);
    }

    [Fact]
    public void TryFindServiceHost_DepthCapRespected_ReturnsFalse()
    {
        // Build a chain of maxDepth+2 ancestors above the target; place the service host at the
        // far end (beyond the depth cap) so the walk must stop before reaching it.
        const int maxDepth = WindowsProcessEvidenceProvider.MaxAncestryDepth;
        var entries = new List<(uint Pid, uint ParentPid)>();
        // PIDs 1..(maxDepth+2): each child's parent is the next PID up.
        for (uint i = 1; i <= maxDepth + 2; i++)
            entries.Add((i, i + 1));
        var snapshot = Snapshot(entries.ToArray());

        // Service host is the top-most ancestor, well beyond the depth cap from PID 1.
        var serviceHostPid = (int)(maxDepth + 2);
        var hosts = new HashSet<int> { serviceHostPid };

        var found = WindowsProcessEvidenceProvider.TryFindServiceHost(
            1, snapshot, hosts, maxDepth, out var hostPid);

        Assert.False(found);
        Assert.Equal(0, hostPid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsServiceBacked_DelegatesToTryFindServiceHost_Consistent(bool serviceBacked)
    {
        var snapshot = Snapshot((2000, 1000), (1000, 1));
        var hosts = serviceBacked ? new HashSet<int> { 1000 } : new HashSet<int>();

        var viaIsBacked = WindowsProcessEvidenceProvider.IsServiceBacked(
            2000, snapshot, hosts, WindowsProcessEvidenceProvider.MaxAncestryDepth);
        var viaTryFind = WindowsProcessEvidenceProvider.TryFindServiceHost(
            2000, snapshot, hosts, WindowsProcessEvidenceProvider.MaxAncestryDepth, out _);

        Assert.Equal(serviceBacked, viaIsBacked);
        Assert.Equal(viaIsBacked, viaTryFind);
    }
}
