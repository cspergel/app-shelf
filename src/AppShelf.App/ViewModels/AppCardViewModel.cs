using System.Windows.Input;
using AppShelf.App.Services;
using AppShelf.Core.Launch;
using AppShelf.Core.Models;

namespace AppShelf.App.ViewModels;

/// <summary>One card in the grid: a bound view over an <see cref="AppEntry"/> plus its
/// live status, log tail, and state-dependent actions (spec §5.2).</summary>
public sealed class AppCardViewModel : ObservableObject
{
    private readonly GuiAppService _service;
    private readonly Action<AppCardViewModel> _editRequested;
    private readonly Action<AppCardViewModel> _removeRequested;
    private readonly Action<AppCardViewModel> _favoriteToggled;
    private readonly IAppDialogs _dialogs;

    private LaunchStatus _status;
    private bool _showLogs;
    private string _logs = "";
    private string _reason = "";
    private bool _canRunSetup;
    private bool _isSettingUp;

    public AppCardViewModel(
        AppEntry entry,
        GuiAppService service,
        IAppDialogs dialogs,
        Action<AppCardViewModel> editRequested,
        Action<AppCardViewModel> removeRequested,
        Action<AppCardViewModel> favoriteToggled)
    {
        Entry = entry;
        _service = service;
        _dialogs = dialogs;
        _editRequested = editRequested;
        _removeRequested = removeRequested;
        _favoriteToggled = favoriteToggled;

        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => !IsRunning);
        OpenCommand = new RelayCommand(Open);
        StopCommand = new RelayCommand(Stop, () => IsRunning || IsStarting);
        RestartCommand = new AsyncRelayCommand(RestartAsync);
        EditCommand = new RelayCommand(() => _editRequested(this));
        RemoveCommand = new RelayCommand(() => _removeRequested(this));
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        ToggleLogsCommand = new RelayCommand(() => ShowLogs = !ShowLogs);
        RunSetupCommand = new AsyncRelayCommand(RunSetupAsync, () => CanRunSetup && !IsSettingUp);

        // Initial status is Stopped (the enum default); the owner kicks off an async poll right
        // after building the cards, so we never block the UI thread probing ports during load.
    }

    public AppEntry Entry { get; }

    public string Name => Entry.Name;
    public string Url => Entry.Url;
    public string TypeBadge => Entry.IsUrlOnly ? "URL" : "Local";
    public string TagsText => Entry.Tags.Count == 0 ? "" : string.Join("  ", Entry.Tags.Select(t => $"#{t}"));
    public bool HasTags => Entry.Tags.Count > 0;
    public string PortText => Entry.Port is int p ? $":{p}" : "";
    public bool IsFavorite => Entry.Favorite;

    public string LastLaunchedText => Entry.LastLaunchedAt is { } t
        ? $"last launched {t.ToLocalTime():g}"
        : "never launched";

    public LaunchStatus Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsStopped));
                OnPropertyChanged(nameof(IsStarting));
            }
        }
    }

    public string StatusText => Status switch
    {
        LaunchStatus.Running => "running",
        LaunchStatus.Starting => "starting…",
        LaunchStatus.Error => "error",
        LaunchStatus.PortInUse => "port in use",
        _ => "stopped",
    };

    public bool IsRunning => Status == LaunchStatus.Running;
    public bool IsStopped => Status is LaunchStatus.Stopped or LaunchStatus.Error or LaunchStatus.PortInUse;
    public bool IsStarting => Status == LaunchStatus.Starting;

    public bool ShowLogs
    {
        get => _showLogs;
        set
        {
            if (SetField(ref _showLogs, value) && value)
                RefreshLogs();
        }
    }

    public string Logs
    {
        get => _logs;
        private set => SetField(ref _logs, value);
    }

    /// <summary>Plain-English reason the last launch was blocked (pre-flight), or "" when none.</summary>
    public string Reason
    {
        get => _reason;
        private set => SetField(ref _reason, value);
    }

    /// <summary>True when the blocking reason is plausibly fixable by running the install command.</summary>
    public bool CanRunSetup
    {
        get => _canRunSetup;
        private set => SetField(ref _canRunSetup, value);
    }

    /// <summary>True while the install command is running (disables the Run setup button).</summary>
    public bool IsSettingUp
    {
        get => _isSettingUp;
        private set => SetField(ref _isSettingUp, value);
    }

    public ICommand LaunchCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand ToggleLogsCommand { get; }
    public ICommand RunSetupCommand { get; }

    /// <summary>One-click favorite: flip the flag, notify the binding, then hand off to the
    /// owner (MainViewModel) to persist via ConfigStore and re-sort the grid.</summary>
    private void ToggleFavorite()
    {
        Entry.Favorite = !Entry.Favorite;
        OnPropertyChanged(nameof(IsFavorite));
        _favoriteToggled(this);
    }

    /// <summary>Undo an optimistic favorite flip after a failed save: flip the flag back and
    /// re-notify the star binding.</summary>
    public void RevertFavorite()
    {
        Entry.Favorite = !Entry.Favorite;
        OnPropertyChanged(nameof(IsFavorite));
    }

    /// <summary>Re-reads live status (and logs, when the panel is open). Synchronous — used by
    /// one-off, user-initiated actions (Open/Stop). The periodic poll uses <see cref="ApplyStatus"/>
    /// with a status computed off the UI thread (see <c>MainViewModel.RefreshStatuses</c>).</summary>
    public void RefreshStatus()
    {
        Status = _service.StatusOf(Entry);
        if (ShowLogs)
            RefreshLogs();
    }

    /// <summary>Apply an already-computed status (from the background poll) on the UI thread,
    /// refreshing the log tail if the panel is open.</summary>
    public void ApplyStatus(LaunchStatus status)
    {
        Status = status;
        if (ShowLogs)
            RefreshLogs();
    }

    private void RefreshLogs()
    {
        var tail = _service.LogTail(Entry);
        Logs = tail.Count == 0 ? "(no captured output)" : string.Join(Environment.NewLine, tail);
    }

    private async Task LaunchAsync()
    {
        // Pre-gate: a foreign process holds the registered port. The affirmative action RECLAIMs —
        // stop that process and launch this app — rather than warm-opening the browser to whatever
        // currently holds the port.
        var conflict = _service.CheckPortConflict(Entry);
        if (conflict != null)
        {
            bool proceed = _dialogs.Confirm(
                $"Port {conflict.Port} is in use by {conflict.ProcessName} (PID {conflict.Pid}).\n\n" +
                $"Reclaim it — stop that process and start {Entry.Name}?\n\n" +
                "(No leaves it alone; you can also free it from the Ports panel.)",
                "Port in use");

            if (!proceed)
            {
                Status = LaunchStatus.PortInUse;
                return;
            }

            // User chose Yes — reclaim: kill the port holder, then launch this app.
            Status = LaunchStatus.Starting;
            var reclaim = await _service.ReclaimAsync(Entry, conflict.Port);

            if (!reclaim.Kill.Success)
            {
                // The conflicting process couldn't be stopped (e.g. a Windows service / access
                // denied). Surface the reason and leave the port flagged — do NOT launch.
                _dialogs.Info($"Couldn't free port {conflict.Port}: {reclaim.Kill.Reason}", "Reclaim failed");
                Status = LaunchStatus.PortInUse;
                return;
            }

            ApplyLaunchResult(reclaim.Launch ?? new LaunchResult(LaunchStatus.Stopped, null, Array.Empty<string>()));
            return;
        }

        Status = LaunchStatus.Starting;
        var result = await _service.LaunchAsync(Entry);
        ApplyLaunchResult(result);
    }

    /// <summary>Apply a launch outcome to card state: status, last-launched stamp, the plain-English
    /// reason / "Run setup" affordance, and the error log panel. Shared by the normal launch path
    /// and the reclaim-on-conflict path so both behave identically.</summary>
    private void ApplyLaunchResult(LaunchResult result)
    {
        Status = result.Status;
        OnPropertyChanged(nameof(LastLaunchedText));

        Reason = result.Reason ?? "";
        CanRunSetup = result.CanRunSetup;

        if (result.Status == LaunchStatus.Error)
        {
            // A pre-flight block carries a plain-English Reason but no captured output — keep the
            // log panel closed so the reason (and any "Run setup" button) is the focus. A real
            // spawn failure has a log tail worth showing.
            if (result.LogTail.Count > 0)
            {
                ShowLogs = true;
                Logs = string.Join(Environment.NewLine, result.LogTail);
            }
            else if (string.IsNullOrEmpty(Reason))
            {
                ShowLogs = true;
                Logs = "(failed to start; no output captured)";
            }
        }
        else
        {
            // Cleared on any non-error outcome (running / starting).
            Reason = "";
            CanRunSetup = false;
        }
    }

    /// <summary>One-click "Run setup": run the app's install command, stream its output into the
    /// log panel, and on success auto-retry the launch.</summary>
    private async Task RunSetupAsync()
    {
        IsSettingUp = true;
        ShowLogs = true;
        Logs = "Running setup…";
        try
        {
            var outcome = await _service.RunSetupAsync(Entry);
            Logs = outcome.Output.Count == 0
                ? "(setup produced no output)"
                : string.Join(Environment.NewLine, outcome.Output);

            if (outcome.Ok)
            {
                Reason = "";
                CanRunSetup = false;
                IsSettingUp = false; // clear before retry so the launch flow owns state
                await LaunchAsync();
                return;
            }
        }
        finally
        {
            IsSettingUp = false;
        }
    }

    private void Open()
    {
        _service.Open(Entry);
        RefreshStatus();
    }

    private void Stop()
    {
        _service.Stop(Entry);
        RefreshStatus();
    }

    private async Task RestartAsync()
    {
        Status = LaunchStatus.Starting;
        var result = await _service.RestartAsync(Entry);
        Status = result.Status;
        OnPropertyChanged(nameof(LastLaunchedText));
    }
}
