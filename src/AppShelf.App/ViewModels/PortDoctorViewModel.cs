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
    private bool _refreshing;
    private bool _refreshPending;

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

    public async void Refresh()
    {
        if (_refreshing)
        {
            // A scan is already in flight — mark that another one is needed (e.g. after a kill)
            // so the list is consistent once the current scan finishes.
            _refreshPending = true;
            return;
        }
        _refreshing = true;
        _refreshPending = false;
        try
        {
            Rows.Clear();
            var reports = await Task.Run(() => _service.ScanPorts().ToList());
            foreach (var report in reports)
                Rows.Add(new PortRowViewModel(report, OnKill));

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasOrphans));
            // The async scan resumes on the UI context, so the requery runs on the dispatcher.
            // RelayCommand has no RaiseCanExecuteChanged, so nudge CommandManager to re-evaluate
            // KillAllOrphansCommand.CanExecute promptly now that Rows/HasOrphans changed.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
        catch
        {
            // ScanPorts enumerates TCP tables and WMI — failures are degraded silently to an
            // empty list rather than crashing the app via an unhandled async-void exception.
            Rows.Clear();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasOrphans));
        }
        finally
        {
            _refreshing = false;
            // If a second Refresh() was requested while we were scanning (e.g. from OnKill),
            // run it now so the list reflects the post-kill state.
            if (_refreshPending)
                Refresh();
        }
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
            _dialogs.Info(BuildFailureMessage(row, outcome), $"Kill port {row.Port} failed");
        Refresh();
    }

    /// <summary>Composes a clean, human one-paragraph message from the structured outcome and the
    /// row's listener info — never the raw taskkill PID dump (spec: "readable errors only").</summary>
    private static string BuildFailureMessage(PortRowViewModel row, AppShelf.Core.Process.KillOutcome outcome)
    {
        if (outcome.AccessDenied && row.IsService)
            return $"Couldn't free port {row.Port}: {row.Process} (PID {row.Pid}) is a Windows " +
                   "service and can't be stopped without administrator rights.\n\n" +
                   "Stop it from an elevated terminal with 'Stop-Service', or run AppShelf as administrator.";

        if (outcome.AccessDenied)
            return $"Couldn't free port {row.Port}: access denied stopping {row.Process} (PID {row.Pid}). " +
                   "It may be a protected or elevated process — try running AppShelf as administrator.";

        return $"Couldn't free port {row.Port}: {row.Process} (PID {row.Pid}) could not be stopped.";
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
