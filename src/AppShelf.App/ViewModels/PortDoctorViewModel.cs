using System.Collections.ObjectModel;
using System.Windows.Input;
using AppShelf.App.Services;

namespace AppShelf.App.ViewModels;

/// <summary>Backs the Ports panel: scans live dev-server ports, classifies each, and offers
/// evidence-backed confirm kills (one row, or all Likely-orphaned rows). All real work lives in
/// <see cref="GuiAppService"/> / Core.</summary>
public sealed class PortDoctorViewModel : ObservableObject
{
    private readonly GuiAppService _service;
    private readonly IAppDialogs _dialogs;

    public PortDoctorViewModel(GuiAppService service, IAppDialogs dialogs)
    {
        _service = service;
        _dialogs = dialogs;

        RefreshCommand = new RelayCommand(Refresh);
        KillAllOrphansCommand = new RelayCommand(KillAllOrphans, () => HasOrphans);

        Refresh();
    }

    public ObservableCollection<PortRowViewModel> Rows { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand KillAllOrphansCommand { get; }

    public bool IsEmpty => Rows.Count == 0;
    public bool HasOrphans => Rows.Any(r => r.IsLikelyOrphaned);

    public void Refresh()
    {
        Rows.Clear();
        foreach (var report in _service.ScanPorts())
            Rows.Add(new PortRowViewModel(report, OnKill));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasOrphans));
    }

    private void OnKill(PortRowViewModel row)
    {
        var prompt = row.IsService
            ? $"{row.Process} (PID {row.Pid}) looks like a Windows service. Killing it needs " +
              $"administrator rights and will likely fail. Continue?\n\n{row.EvidenceText}"
            : $"Kill this process tree and free the port?\n\n{row.EvidenceText}";

        if (!_dialogs.Confirm(prompt, $"Kill port {row.Port}"))
            return;

        var outcome = _service.KillPort(row.Port);
        if (!outcome.Success)
            _dialogs.Info($"Could not free port {row.Port}: {outcome.Reason}" +
                          (row.IsService
                              ? "\n\nThis is a Windows service — stop it with an elevated 'Stop-Service', " +
                                "or run AppShelf as administrator."
                              : ""),
                          $"Kill port {row.Port} failed");
        Refresh();
    }

    private void KillAllOrphans()
    {
        var orphans = Rows.Where(r => r.IsLikelyOrphaned).ToList();
        if (orphans.Count == 0)
            return;

        var summary = string.Join("\n", orphans.Select(o => $"  • {o.Port}  {o.Process} (pid {o.Pid})  owner={o.Owner}"));
        if (!_dialogs.Confirm($"Kill {orphans.Count} likely-orphaned process tree(s)?\n\n{summary}",
                              "Kill all orphaned ports"))
            return;

        foreach (var orphan in orphans)
            _service.KillPort(orphan.Port);
        Refresh();
    }
}
