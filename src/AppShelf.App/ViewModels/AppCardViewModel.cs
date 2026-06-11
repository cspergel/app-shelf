using System.Windows.Input;
using AppShelf.App.Services;
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

    private LaunchStatus _status;
    private bool _showLogs;
    private string _logs = "";

    public AppCardViewModel(
        AppEntry entry,
        GuiAppService service,
        Action<AppCardViewModel> editRequested,
        Action<AppCardViewModel> removeRequested,
        Action<AppCardViewModel> favoriteToggled)
    {
        Entry = entry;
        _service = service;
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

    public ICommand LaunchCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand ToggleLogsCommand { get; }

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
        Status = LaunchStatus.Starting;
        var result = await _service.LaunchAsync(Entry);
        Status = result.Status;
        OnPropertyChanged(nameof(LastLaunchedText));
        if (result.Status == LaunchStatus.Error)
        {
            ShowLogs = true;
            Logs = result.LogTail.Count == 0 ? "(failed to start; no output captured)" : string.Join(Environment.NewLine, result.LogTail);
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
