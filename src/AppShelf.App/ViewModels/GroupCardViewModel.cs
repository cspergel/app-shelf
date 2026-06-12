using System.Collections.ObjectModel;
using System.Windows.Input;
using AppShelf.App.Services;
using AppShelf.Core.Models;

namespace AppShelf.App.ViewModels;

/// <summary>A collapsible card representing one project group: an aggregate status pill plus
/// group-level Start-all / Stop-all / Open and a drill-in that hosts the member
/// <see cref="AppCardViewModel"/>s (Slice 3).</summary>
public sealed class GroupCardViewModel : ObservableObject
{
    private readonly GuiAppService _service;
    private readonly IAppDialogs _dialogs;
    private readonly Action? _reloadRequested;
    private bool _isExpanded;
    private GroupAggregateStatus _statusPill;

    public GroupCardViewModel(string name, IReadOnlyList<AppCardViewModel> members,
        GuiAppService service, IAppDialogs dialogs, Action? reloadRequested = null)
    {
        Name = name;
        _service = service;
        _dialogs = dialogs;
        _reloadRequested = reloadRequested;
        foreach (var m in members)
            Members.Add(m);

        StartAllCommand = new AsyncRelayCommand(StartAllAsync);
        StopAllCommand = new RelayCommand(StopAll);
        OpenCommand = new RelayCommand(Open);
        RenameCommand = new RelayCommand(Rename);
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);

        RecomputePill();
    }

    public string Name { get; }

    public ObservableCollection<AppCardViewModel> Members { get; } = new();

    /// <summary>Members in role order (backend → frontend → other) for start/stop sequencing.</summary>
    private IReadOnlyList<AppEntry> OrderedEntries =>
        Members.Select(m => m.Entry)
               .OrderBy(e => AppRoles.Order(e.Role))
               .ToList();

    public int MemberCount => Members.Count;
    public string MemberCountText => Members.Count == 1 ? "1 app" : $"{Members.Count} apps";

    public GroupAggregateStatus StatusPill
    {
        get => _statusPill;
        private set
        {
            if (SetField(ref _statusPill, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => StatusPill switch
    {
        GroupAggregateStatus.Running => "running",
        GroupAggregateStatus.Starting => "starting…",
        GroupAggregateStatus.Partial => "partial",
        _ => "stopped",
    };

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
                OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public string ExpandGlyph => IsExpanded ? "⌃" : "⌄";

    public ICommand StartAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand ToggleExpandCommand { get; }

    /// <summary>Most-recent member launch — used by the main view's recency sort.</summary>
    public DateTimeOffset Recency =>
        Members.Select(m => m.Entry.LastLaunchedAt ?? DateTimeOffset.MinValue)
               .DefaultIfEmpty(DateTimeOffset.MinValue)
               .Max();

    public bool HasFavorite => Members.Any(m => m.IsFavorite);

    /// <summary>True when any member matches the given search needle (name or tag).</summary>
    public bool Matches(string needle) =>
        Members.Any(m =>
            m.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            m.Entry.Tags.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Refresh each member's live status, then recompute the aggregate pill.</summary>
    public void RefreshStatuses()
    {
        foreach (var m in Members)
            m.RefreshStatus();
        RecomputePill();
    }

    /// <summary>Recompute the aggregate pill from members' already-updated statuses, without
    /// probing ports. Called by the background poll after it has applied member statuses.</summary>
    public void RecomputeAggregate() => RecomputePill();

    private void RecomputePill() =>
        StatusPill = GroupStatusCalculator.Aggregate(Members.Select(m => m.Status));

    private async Task StartAllAsync()
    {
        await _service.StartGroupAsync(OrderedEntries);
        RefreshStatuses();
    }

    private void StopAll()
    {
        var ordered = OrderedEntries;
        var unmanaged = _service.UnmanagedLivePorts(ordered);
        if (unmanaged.Count > 0)
        {
            var portList = string.Join(", ", unmanaged);
            var msg = $"Stop all in '{Name}'? This includes {unmanaged.Count} server(s) " +
                      $"AppShelf didn't launch (ports {portList}).";
            if (!_dialogs.Confirm(msg, "Stop all"))
                return;
        }

        _service.StopGroup(ordered);
        RefreshStatuses();
    }

    /// <summary>Prompt for a new group name and re-label every member (label-only, via
    /// <see cref="GuiAppService.RenameGroup"/>), then ask the host to reload so the grid re-renders.
    /// No-op on cancel, blank, or unchanged name.</summary>
    private void Rename()
    {
        var name = _dialogs.PromptRename("Rename group", "Group name", Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, Name, StringComparison.Ordinal))
            return;
        _service.RenameGroup(Name, name);
        _reloadRequested?.Invoke();
    }

    /// <summary>Open the group's frontend (fallback: first member with a URL).</summary>
    private void Open()
    {
        var target = Members.FirstOrDefault(m => m.Entry.Role == AppRoles.Frontend
                                                 && !string.IsNullOrWhiteSpace(m.Entry.Url))
                     ?? Members.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Entry.Url));
        if (target is null) return;
        _service.Open(target.Entry);
        RefreshStatuses();
    }
}
