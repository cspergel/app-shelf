using System.Collections.Generic;
using System.Net.Sockets;
using AppShelf.Core.Models;
using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests;

public class PortDoctorTests
{
    private sealed class FakeEvidence : IProcessEvidenceProvider
    {
        public Dictionary<int, bool> ParentAliveByPid = new();
        public Dictionary<int, IReadOnlyList<AncestorInfo>> AncestryByPid = new();

        public ProcessEvidence ForPid(int pid) => new(
            pid, "node.exe", @"C:\node.exe", "vite", @"C:\proj", null,
            ParentAliveByPid.TryGetValue(pid, out var a) && a,
            IsService: false,
            Ancestry: AncestryByPid.TryGetValue(pid, out var chain) ? chain : null);
    }

    private static AppEntry App(string id, int port) =>
        new() { Id = id, Name = id, Url = $"http://localhost:{port}", Port = port };

    [Fact]
    public void Scan_MapsRegisteredOwner_AndClassifies()
    {
        var apps = new[] { App("api-backend", 8000) };
        var listeners = new List<(int, int, AddressFamily)>
        {
            (8000, 100, AddressFamily.InterNetwork),   // registered, parent alive -> Registered
            (3000, 200, AddressFamily.InterNetwork),   // not registered -> Unknown
        };
        var evidence = new FakeEvidence { ParentAliveByPid = { [100] = true, [200] = true } };
        var doctor = new PortDoctor(() => listeners, evidence);

        var reports = doctor.Scan(apps, managedPids: new HashSet<int>());

        var p8000 = reports.Single(r => r.Port == 8000);
        Assert.Equal("api-backend", p8000.OwnerAppId);
        Assert.Equal(PortTier.Registered, p8000.Tier);

        var p3000 = reports.Single(r => r.Port == 3000);
        Assert.Null(p3000.OwnerAppId);
        Assert.Equal(PortTier.Unknown, p3000.Tier);
    }

    [Fact]
    public void Scan_RegisteredPort_ParentDead_IsLikelyOrphaned()
    {
        var apps = new[] { App("api-backend", 8000) };
        var listeners = new List<(int, int, AddressFamily)> { (8000, 100, AddressFamily.InterNetwork) };
        var evidence = new FakeEvidence { ParentAliveByPid = { [100] = false } };
        var doctor = new PortDoctor(() => listeners, evidence);

        var report = doctor.Scan(apps, new HashSet<int>()).Single();

        Assert.Equal(PortTier.LikelyOrphaned, report.Tier);
    }

    [Fact]
    public void Scan_DualFamilySamePort_EmitsOneReport()
    {
        var apps = new[] { App("api-backend", 8000) };
        var listeners = new List<(int, int, AddressFamily)>
        {
            (8000, 100, AddressFamily.InterNetwork),
            (8000, 100, AddressFamily.InterNetworkV6),
        };
        var evidence = new FakeEvidence { ParentAliveByPid = { [100] = true } };
        var doctor = new PortDoctor(() => listeners, evidence);

        var reports = doctor.Scan(apps, new HashSet<int>());

        var p8000 = Assert.Single(reports, r => r.Port == 8000);
        Assert.Equal(AddressFamily.InterNetwork, p8000.Family);
    }

    [Fact]
    public void Scan_IgnoresPortsOutsideCandidateSet()
    {
        var apps = new[] { App("api-backend", 8000) };
        var listeners = new List<(int, int, AddressFamily)> { (54321, 100, AddressFamily.InterNetwork) };
        var doctor = new PortDoctor(() => listeners, new FakeEvidence());

        Assert.Empty(doctor.Scan(apps, new HashSet<int>()));
    }

    [Fact]
    public void WalkAncestry_DeadImmediateParent_IncludedAsTerminalAndStops()
    {
        // Regression test for the fabricated-lineage bug:
        // child(100) → parent(200)[dead] → grandparent(300)[alive in snapshot, but PID reused]
        // Correct behaviour: include 200 as [dead] terminal; do NOT include 300.
        // Before the fix the walk continued past dead nodes because null currentStartTime
        // disabled the monotonicity guard, producing bogus ancestry.
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)100] = (200u, "worker.exe"),
            [(uint)200] = (300u, "svchost.exe"),
            [(uint)300] = (0u, ""),
        };
        var baseTime = DateTimeOffset.UtcNow;
        DateTimeOffset? GetStartTime(uint pid) => pid switch
        {
            200u => null,                        // dead / inaccessible
            300u => baseTime.AddMinutes(-5),     // live but unrelated (PID reuse)
            _ => null
        };

        var result = WindowsProcessEvidenceProvider.WalkAncestry(100, baseTime, dict, GetStartTime);

        // Walk must stop after the first dead node — only one ancestor in the result.
        Assert.Single(result);
        Assert.Equal(200, result[0].Pid);
        Assert.False(result[0].Alive);
    }

    [Fact]
    public void WalkAncestry_AllAncestorsAlive_WalksFullChain()
    {
        // Verify that a fully-alive chain is NOT truncated by the stop-at-dead rule.
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)100] = (200u, "npm.exe"),
            [(uint)200] = (300u, "cmd.exe"),
            [(uint)300] = (0u, ""),
        };
        var baseTime = DateTimeOffset.UtcNow;
        DateTimeOffset? GetStartTime(uint pid) => pid switch
        {
            200u => baseTime.AddMinutes(-2),
            300u => baseTime.AddMinutes(-5),
            _ => null
        };

        var result = WindowsProcessEvidenceProvider.WalkAncestry(100, baseTime, dict, GetStartTime);

        Assert.Equal(2, result.Count);
        Assert.Equal(200, result[0].Pid);
        Assert.True(result[0].Alive);
        Assert.Equal(300, result[1].Pid);
        Assert.True(result[1].Alive);
    }

    [Fact]
    public void WalkAncestry_PidReuse_StopsBeforeReusedNode()
    {
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)100] = (200u, "reused.exe"),
            [(uint)200] = (300u, "cmd.exe"),
        };
        var childTime = DateTimeOffset.UtcNow;
        // pid 200's start time is AFTER the child's → PID reuse
        DateTimeOffset? GetStartTime(uint pid) => pid switch
        {
            200u => childTime.AddMinutes(5),  // LATER → reuse
            _ => null
        };

        var result = WindowsProcessEvidenceProvider.WalkAncestry(100, childTime, dict, GetStartTime);

        Assert.Empty(result);
    }

    [Fact]
    public void WalkAncestry_ParentNotInSnapshot_StopsCleanly()
    {
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)100] = (200u, "npm.exe"),
            // pid 200 is NOT in dict → parent exited before snapshot
        };

        var result = WindowsProcessEvidenceProvider.WalkAncestry(100, null, dict, _ => null);

        // pid 200 is added (it is the recorded parent of 100), then its own parent is absent → stop.
        Assert.Single(result);
        Assert.Equal(200, result[0].Pid);
        Assert.False(result[0].Alive);
    }

    [Fact]
    public void WalkAncestry_SystemRoot_StopsAtPid4()
    {
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)100] = (200u, "npm.exe"),
            [(uint)200] = (4u, "System"),
        };
        DateTimeOffset? GetStartTime(uint pid) => pid == 200u ? DateTimeOffset.UtcNow.AddMinutes(-10) : null;

        var result = WindowsProcessEvidenceProvider.WalkAncestry(100, DateTimeOffset.UtcNow, dict, GetStartTime);

        Assert.Single(result);
        Assert.Equal(200, result[0].Pid);
    }

    [Fact]
    public void WalkAncestry_CycleInDict_StopsAtCycleNotInfiniteLoop()
    {
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)100] = (200u, "npm.exe"),
            [(uint)200] = (100u, "cmd.exe"),  // cycle back to start
        };

        var result = WindowsProcessEvidenceProvider.WalkAncestry(100, null, dict, _ => null);

        Assert.Single(result);
        Assert.Equal(200, result[0].Pid);
    }

    [Fact]
    public void WalkAncestry_DeepChain_CappedAtMaxDepth()
    {
        var dict = new Dictionary<uint, (uint, string)>();
        for (int i = 1000; i < 1020; i++)
            dict[(uint)i] = ((uint)(i + 1), $"p{i}.exe");
        dict[1020u] = (0u, "");
        var baseTime = DateTimeOffset.UtcNow;
        DateTimeOffset? GetStartTime(uint pid) => baseTime.AddMinutes(-((int)pid - 999));

        var result = WindowsProcessEvidenceProvider.WalkAncestry(1000, baseTime, dict, GetStartTime);

        Assert.Equal(WindowsProcessEvidenceProvider.MaxAncestryDepth, result.Count);
    }

    [Fact]
    public void IsServiceBacked_WorkerWhoseParentIsServiceHost_ReturnsTrue()
    {
        // The httpd-worker case: services.exe → httpd(master, 1616) → httpd(worker, 5716).
        // The worker's immediate parent is the master httpd, which IS a running-service host PID.
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)5716] = (1616u, "httpd.exe"),  // worker → master
            [(uint)1616] = (640u, "httpd.exe"),   // master → services.exe
            [(uint)640] = (4u, "services.exe"),
        };
        var serviceHostPids = new HashSet<int> { 1616 };

        Assert.True(WindowsProcessEvidenceProvider.IsServiceBacked(
            5716, dict, serviceHostPids, WindowsProcessEvidenceProvider.MaxAncestryDepth));
    }

    [Fact]
    public void IsServiceBacked_ProcessIsServiceHostItself_ReturnsTrue()
    {
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)1616] = (640u, "httpd.exe"),
        };
        var serviceHostPids = new HashSet<int> { 1616 };

        Assert.True(WindowsProcessEvidenceProvider.IsServiceBacked(
            1616, dict, serviceHostPids, WindowsProcessEvidenceProvider.MaxAncestryDepth));
    }

    [Fact]
    public void IsServiceBacked_NoServiceAncestor_ReturnsFalse()
    {
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)100] = (200u, "node.exe"),
            [(uint)200] = (300u, "cmd.exe"),
            [(uint)300] = (0u, "explorer.exe"),
        };
        var serviceHostPids = new HashSet<int> { 1616 };

        Assert.False(WindowsProcessEvidenceProvider.IsServiceBacked(
            100, dict, serviceHostPids, WindowsProcessEvidenceProvider.MaxAncestryDepth));
    }

    [Fact]
    public void IsServiceBacked_CycleInDict_TerminatesAndReturnsFalse()
    {
        var dict = new Dictionary<uint, (uint, string)>
        {
            [(uint)100] = (200u, "a.exe"),
            [(uint)200] = (100u, "b.exe"),  // cycle back to start
        };
        var serviceHostPids = new HashSet<int> { 9999 };

        Assert.False(WindowsProcessEvidenceProvider.IsServiceBacked(
            100, dict, serviceHostPids, WindowsProcessEvidenceProvider.MaxAncestryDepth));
    }

    [Fact]
    public void IsServiceBacked_DepthCap_StopsBeforeDistantServiceAncestor()
    {
        // Build a long chain; place the service host PID just beyond the depth cap so the walk
        // must stop before reaching it.
        var dict = new Dictionary<uint, (uint, string)>();
        for (int i = 1000; i < 1000 + WindowsProcessEvidenceProvider.MaxAncestryDepth + 5; i++)
            dict[(uint)i] = ((uint)(i + 1), $"p{i}.exe");
        // Service host PID sits far up the chain, beyond maxDepth hops from 1000.
        int distantServicePid = 1000 + WindowsProcessEvidenceProvider.MaxAncestryDepth + 3;
        var serviceHostPids = new HashSet<int> { distantServicePid };

        Assert.False(WindowsProcessEvidenceProvider.IsServiceBacked(
            1000, dict, serviceHostPids, WindowsProcessEvidenceProvider.MaxAncestryDepth));
    }

    [Fact]
    public void Scan_AncestryFlowsThroughReport()
    {
        var apps = new[] { App("api", 8000) };
        var listeners = new List<(int, int, AddressFamily)> { (8000, 100, AddressFamily.InterNetwork) };
        var evidence = new FakeEvidence { ParentAliveByPid = { [100] = false } };
        evidence.AncestryByPid[100] = new[]
        {
            new AncestorInfo(200, "npm", false, null, null)
        }.ToList().AsReadOnly();
        var doctor = new PortDoctor(() => listeners, evidence);

        var reports = doctor.Scan(apps, managedPids: new HashSet<int>());

        var report = reports.Single(r => r.Port == 8000);
        Assert.NotNull(report.Evidence.Ancestry);
        Assert.Equal(200, report.Evidence.Ancestry![0].Pid);
        Assert.False(report.Evidence.Ancestry![0].Alive);
    }
}
