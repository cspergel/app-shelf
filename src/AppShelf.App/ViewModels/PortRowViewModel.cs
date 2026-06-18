using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AppShelf.Core.Process;

namespace AppShelf.App.ViewModels;

/// <summary>One row in the Ports panel: a live listener with its evidence and tier. The Kill
/// button delegates back to <see cref="PortDoctorViewModel"/> so the confirm + refresh live in
/// one place.</summary>
public sealed class PortRowViewModel : ObservableObject
{
    public PortRowViewModel(
        PortReport report,
        Action<PortRowViewModel> onKill,
        Action<PortRowViewModel>? onReclaim = null,
        Action<PortRowViewModel>? onStopService = null)
    {
        Report = report;
        KillCommand = new RelayCommand(() => onKill(this));
        ReclaimCommand = new RelayCommand(() => onReclaim?.Invoke(this));
        StopServiceCommand = new RelayCommand(() => onStopService?.Invoke(this));
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        CopyAncestryCommand = new RelayCommand(CopyAncestry);
    }

    public PortReport Report { get; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
                OnPropertyChanged(nameof(ExpandLabel));
        }
    }

    public string ExpandLabel => IsExpanded ? "▲" : "▼";

    public ICommand ToggleExpandCommand { get; }

    /// <summary>Lines rendered in the row-detail panel. One entry per chain node.
    /// Returns a single placeholder entry when no ancestry data is available.</summary>
    public IReadOnlyList<string> AncestryLines
    {
        get
        {
            var chain = Report.Evidence.Ancestry;
            if (chain is null || chain.Count == 0)
                return new[] { "(no ancestry data)" };

            var lines = new List<string>();
            // Root process (the port owner)
            lines.Add(FormatNode(
                Report.Evidence.Pid,
                Report.Evidence.ProcessName,
                alive: true,
                Report.Evidence.CommandLine,
                Report.Evidence.ExeDir));

            foreach (var a in chain)
                lines.Add(FormatNode(a.Pid, a.ProcessName, a.Alive, a.CommandLine, a.ExeDir));

            return lines.AsReadOnly();
        }
    }

    /// <summary>The full ancestry chain joined into one selectable/copyable block.
    /// Single source used by both the read-only TextBox and the copy command.</summary>
    public string AncestryText => string.Join(Environment.NewLine, AncestryLines);

    public ICommand CopyAncestryCommand { get; }

    private string _copyLabel = "Copy";
    /// <summary>Button text — flips to "Copied!" briefly after a successful copy.</summary>
    public string CopyLabel
    {
        get => _copyLabel;
        private set => SetField(ref _copyLabel, value);
    }

    private DispatcherTimer? _copyResetTimer;

    private void CopyAncestry()
    {
        try
        {
            var text = AncestryText;
            if (string.IsNullOrEmpty(text))
                return;

            Clipboard.SetText(text);

            // Brief feedback: "Copied!" reverting to "Copy" after ~1.5s.
            CopyLabel = "Copied!";
            _copyResetTimer ??= CreateCopyResetTimer();
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
        catch
        {
            // Clipboard can be locked by another process (COMException/ExternalException).
            // Fail silently — copying is a convenience, not worth crashing over.
        }
    }

    private DispatcherTimer CreateCopyResetTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CopyLabel = "Copy";
        };
        return timer;
    }

    private static string FormatNode(int pid, string name, bool alive, string? cmd, string? exeDir)
    {
        var aliveTag = alive ? "" : " [dead]";
        var cmdPart = cmd is not null ? $"\n        cmd: {cmd}" : "";
        // Label as "exe dir:" to accurately reflect that this is the executable directory,
        // not the process working directory (Win32_Process has no CWD field).
        var dirPart = exeDir is not null ? $"\n        exe dir: {exeDir}" : "";
        return $"  {name}{aliveTag} (pid {pid}){cmdPart}{dirPart}";
    }

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

    /// <summary>The SCM internal service name (for an elevated Stop-Service), or null when not
    /// service-backed / unresolved.</summary>
    public string? ServiceName => Report.Evidence.ServiceName;

    /// <summary>True only when this row is a Windows service AND we resolved its name — gates the
    /// "Stop service" button so we never offer it without a name to act on.</summary>
    public bool CanStopService =>
        Report.Evidence.IsService && !string.IsNullOrEmpty(Report.Evidence.ServiceName);

    public ICommand KillCommand { get; }

    public ICommand ReclaimCommand { get; }

    public ICommand StopServiceCommand { get; }

    public string? OwnerAppId => Report.OwnerAppId;

    /// <summary>Reclaim is offered only for a registered app whose launcher is dead (the orphan
    /// case) — never for Managed, Registered (parent alive), or Unknown rows, and never when the
    /// port could not be mapped back to a registered app.</summary>
    public bool CanReclaim =>
        Report.Tier == PortTier.LikelyOrphaned && Report.OwnerAppId is not null;

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
