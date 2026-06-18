using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text.Json;
using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests;

public class PortReportJsonTests
{
    private static PortReport MakeReport(
        int port = 5173,
        AddressFamily family = AddressFamily.InterNetwork,
        PortTier tier = PortTier.Registered,
        int pid = 4321,
        string processName = "node",
        bool isService = false,
        string? serviceName = null,
        bool parentAlive = true,
        string? ownerAppId = "app-1",
        string? ownerAppName = "My App",
        string? exePath = @"C:\tools\node.exe",
        string? commandLine = "node server.js",
        DateTimeOffset? startedAt = null,
        IReadOnlyList<AncestorInfo>? ancestry = null)
    {
        startedAt ??= new DateTimeOffset(2026, 6, 18, 10, 30, 0, TimeSpan.Zero);
        var evidence = new ProcessEvidence(
            Pid: pid,
            ProcessName: processName,
            ExePath: exePath,
            CommandLine: commandLine,
            ExeDir: @"C:\tools",
            StartedAt: startedAt,
            ParentAlive: parentAlive,
            IsService: isService,
            Ancestry: ancestry,
            ServiceName: serviceName);
        return new PortReport(port, family, evidence, ownerAppId, ownerAppName, tier);
    }

    [Fact]
    public void ToJson_MapsAllScalarFields()
    {
        var report = MakeReport(
            port: 5173,
            family: AddressFamily.InterNetwork,
            tier: PortTier.Registered,
            pid: 4321,
            processName: "node",
            isService: false,
            serviceName: null,
            parentAlive: true,
            ownerAppId: "app-1",
            ownerAppName: "My App",
            exePath: @"C:\tools\node.exe",
            commandLine: "node server.js",
            startedAt: new DateTimeOffset(2026, 6, 18, 10, 30, 0, TimeSpan.Zero));

        var json = PortReportJson.ToJson(new List<PortReport> { report }, indented: false);
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement[0];

        Assert.Equal(5173, el.GetProperty("port").GetInt32());
        Assert.Equal("ipv4", el.GetProperty("family").GetString());
        Assert.Equal("Registered", el.GetProperty("tier").GetString());
        Assert.Equal(4321, el.GetProperty("pid").GetInt32());
        Assert.Equal("node", el.GetProperty("processName").GetString());
        Assert.False(el.GetProperty("isService").GetBoolean());
        Assert.Equal(JsonValueKind.Null, el.GetProperty("serviceName").ValueKind);
        Assert.True(el.GetProperty("parentAlive").GetBoolean());
        Assert.Equal("app-1", el.GetProperty("ownerAppId").GetString());
        Assert.Equal("My App", el.GetProperty("ownerAppName").GetString());
        Assert.Equal(@"C:\tools\node.exe", el.GetProperty("exePath").GetString());
        Assert.Equal("node server.js", el.GetProperty("commandLine").GetString());
        Assert.Equal(
            new DateTimeOffset(2026, 6, 18, 10, 30, 0, TimeSpan.Zero),
            el.GetProperty("startedAt").GetDateTimeOffset());
    }

    [Fact]
    public void ToJson_Ipv6Family_MapsToIpv6String()
    {
        var report = MakeReport(family: AddressFamily.InterNetworkV6);
        var json = PortReportJson.ToJson(new List<PortReport> { report }, indented: false);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ipv6", doc.RootElement[0].GetProperty("family").GetString());
    }

    [Fact]
    public void ToJson_Ipv4Family_MapsToIpv4String()
    {
        var report = MakeReport(family: AddressFamily.InterNetwork);
        var json = PortReportJson.ToJson(new List<PortReport> { report }, indented: false);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ipv4", doc.RootElement[0].GetProperty("family").GetString());
    }

    [Theory]
    [InlineData(PortTier.Managed, "Managed")]
    [InlineData(PortTier.Registered, "Registered")]
    [InlineData(PortTier.LikelyOrphaned, "LikelyOrphaned")]
    [InlineData(PortTier.Unknown, "Unknown")]
    public void ToJson_TierName_EmittedAsString(PortTier tier, string expected)
    {
        var report = MakeReport(tier: tier);
        var json = PortReportJson.ToJson(new List<PortReport> { report }, indented: false);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, doc.RootElement[0].GetProperty("tier").GetString());
    }

    [Fact]
    public void ToJson_AncestryNotPresent()
    {
        var ancestry = new List<AncestorInfo>
        {
            new(Pid: 99, ProcessName: "explorer", Alive: true, CommandLine: "explorer.exe", ExeDir: @"C:\Windows"),
        };
        var report = MakeReport(ancestry: ancestry);
        var json = PortReportJson.ToJson(new List<PortReport> { report }, indented: false);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement[0].TryGetProperty("ancestry", out _));
    }

    [Fact]
    public void ToJson_EmptyList_ReturnsJsonArray()
    {
        var json = PortReportJson.ToJson(new List<PortReport>(), indented: false);
        Assert.Equal("[]", json);
    }

    [Fact]
    public void ToJson_IndentedVsCompact_Differ()
    {
        var report = MakeReport();
        var indented = PortReportJson.ToJson(new List<PortReport> { report }, indented: true);
        var compact = PortReportJson.ToJson(new List<PortReport> { report }, indented: false);

        Assert.Contains("\n", indented);
        Assert.DoesNotContain("\n", compact);
    }
}
