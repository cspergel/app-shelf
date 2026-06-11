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
        public ProcessEvidence ForPid(int pid) => new(
            pid, "node.exe", @"C:\node.exe", "vite", @"C:\proj", null,
            ParentAliveByPid.TryGetValue(pid, out var a) && a);
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
}
