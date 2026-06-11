using System.Windows.Input;
using AppShelf.Core.Process;

namespace AppShelf.App.ViewModels;

/// <summary>One row in the Ports panel: a live listener with its evidence and tier. The Kill
/// button delegates back to <see cref="PortDoctorViewModel"/> so the confirm + refresh live in
/// one place.</summary>
public sealed class PortRowViewModel : ObservableObject
{
    public PortRowViewModel(PortReport report, Action<PortRowViewModel> onKill)
    {
        Report = report;
        KillCommand = new RelayCommand(() => onKill(this));
    }

    public PortReport Report { get; }

    public int Port => Report.Port;
    public int Pid => Report.Evidence.Pid;
    public string Process => Report.Evidence.ProcessName;
    public string Tier => Report.Tier.ToString();
    public string Owner => Report.OwnerAppName ?? "-";
    public string ParentAlive => Report.Evidence.ParentAlive ? "yes" : "no";
    public string Family => Report.Family == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
    public bool IsLikelyOrphaned => Report.Tier == PortTier.LikelyOrphaned;
    public bool IsService => Report.Evidence.IsService;
    public string ServiceBadge => Report.Evidence.IsService ? "⚙ Service" : "";

    public ICommand KillCommand { get; }

    /// <summary>Full evidence shown in the confirm dialog before a kill.</summary>
    public string EvidenceText =>
        $"Port {Port} ({Family})\n" +
        $"Process: {Process} (pid {Pid})\n" +
        $"Tier: {Tier}\n" +
        $"Owner: {Owner}\n" +
        $"Parent alive: {ParentAlive}\n" +
        (IsService ? "Service: looks like a Windows service\n" : "") +
        $"Exe: {Report.Evidence.ExePath ?? "(unknown)"}\n" +
        $"Started: {(Report.Evidence.StartedAt is { } s ? s.LocalDateTime.ToString() : "(unknown)")}";
}
