using System.Net.Sockets;
using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests.Process;

/// <summary>Mirrors the <c>PortRowViewModel.CanStopService</c> predicate. The WPF view-model is not
/// referenceable from this Core test assembly, so the pure predicate logic is verified here against
/// real <see cref="PortReport"/> values. The elevated Stop-Service spawn + UAC interaction in
/// <c>GuiAppService.StopServiceElevatedAsync</c> has no clean mock seam (real-OS elevation) and is
/// verified manually on screen.</summary>
public class ServiceStopEligibilityTests
{
    private static bool CanStopService(PortReport r) =>
        r.Evidence.IsService && !string.IsNullOrEmpty(r.Evidence.ServiceName);

    private static PortReport Report(bool isService, string? serviceName)
    {
        var evidence = new ProcessEvidence(
            Pid: 1234,
            ProcessName: "httpd",
            ExePath: null,
            CommandLine: null,
            ExeDir: null,
            StartedAt: null,
            ParentAlive: true,
            IsService: isService,
            Ancestry: null,
            ServiceName: serviceName);

        return new PortReport(
            Port: 443,
            Family: AddressFamily.InterNetwork,
            Evidence: evidence,
            OwnerAppId: null,
            OwnerAppName: null,
            Tier: PortTier.Unknown);
    }

    [Theory]
    [InlineData(true, "MyService", true)]
    [InlineData(true, null, false)]
    [InlineData(true, "", false)]
    [InlineData(false, "MyService", false)]
    [InlineData(false, null, false)]
    public void CanStopService_OnlyWhenServiceWithName(bool isService, string? serviceName, bool expected)
    {
        var report = Report(isService, serviceName);
        Assert.Equal(expected, CanStopService(report));
    }
}
