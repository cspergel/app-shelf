using System.Collections.ObjectModel;
using System.Windows.Input;
using AppShelf.App.Services;
using AppShelf.Core.Models;

namespace AppShelf.App.ViewModels;

/// <summary>Backs the main window: a tile collection (group cards + standalone app cards) plus
/// search/favorite filtering, recent-first sorting, and add/edit/remove orchestration
/// (spec §5.2, Slice 3 grouping).</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly GuiAppService _service;
    private readonly IAppDialogs _dialogs;

    // All standalone (ungrouped) cards and all group cards, unfiltered.
    private readonly List<AppCardViewModel> _standalones = new();
    private readonly List<GroupCardViewModel> _groups = new();

    private string _searchText = "";
    private bool _favoritesOnly;

    public MainViewModel(GuiAppService service, IAppDialogs dialogs, Action? showPorts = null)
    {
        _service = service;
        _dialogs = dialogs;

        AddCommand = new RelayCommand(AddApp);
        RefreshCommand = new RelayCommand(LoadAll);
        PortsCommand = new RelayCommand(() => showPorts?.Invoke(), () => showPorts is not null);

        LoadAll();
    }

    /// <summary>Mixed tiles: <see cref="GroupCardViewModel"/> and <see cref="AppCardViewModel"/>.</summary>
    public ObservableCollection<object> Tiles { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PortsCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value)) ApplyFilter(); }
    }

    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set { if (SetField(ref _favoritesOnly, value)) ApplyFilter(); }
    }

    public bool IsEmpty => Tiles.Count == 0;

    public string EmptyHint =>
        (_standalones.Count == 0 && _groups.Count == 0)
            ? "No apps yet — click “+ Add App” to register your first project."
            : "No apps match your filter.";

    public void LoadAll()
    {
        // Carry UI state across the rebuild so Edit/Regroup/Refresh don't collapse expanded
        // groups or close open log panels.
        var expandedGroups = _groups.Where(g => g.IsExpanded).Select(g => g.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var openLogIds = _standalones.Concat(_groups.SelectMany(g => g.Members))
            .Where(c => c.ShowLogs).Select(c => c.Entry.Id).ToHashSet();

        _standalones.Clear();
        _groups.Clear();

        var (groups, standalones) = AppGrouping.Organize(_service.LoadApps());

        foreach (var entry in standalones)
            _standalones.Add(MakeCard(entry));

        foreach (var group in groups)
        {
            var members = group.Members.Select(MakeCard).ToList();
            _groups.Add(new GroupCardViewModel(group.Name, members, _service, _dialogs)
            {
                IsExpanded = expandedGroups.Contains(group.Name),
            });
        }

        foreach (var card in _standalones.Concat(_groups.SelectMany(g => g.Members)))
            card.ShowLogs = openLogIds.Contains(card.Entry.Id);

        ApplyFilter();
        RefreshStatuses(); // populate live status off the UI thread (no blocking probe during load)
    }

    private AppCardViewModel MakeCard(AppEntry entry) =>
        new(entry, _service, OnEdit, OnRemove, OnFavoriteToggled);

    private bool _refreshing;

    /// <summary>Poll live status for every card OFF the UI thread — the port probes do blocking
    /// TCP connects, and running them on the dispatcher (the old behavior) froze the UI on every
    /// 2-second tick. Compute statuses in parallel on the thread pool, then apply them and
    /// recompute group pills back on the UI thread. A re-entrancy guard skips a tick if the
    /// previous probe round is still in flight (e.g. a slow/firewalled URL).</summary>
    public async void RefreshStatuses()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var cards = _standalones.Concat(_groups.SelectMany(g => g.Members)).ToList();
            if (cards.Count == 0)
                return;

            var entries = cards.Select(c => c.Entry).ToList();
            var statuses = await Task.Run(() =>
                entries.AsParallel().AsOrdered().Select(e => _service.StatusOf(e)).ToList());

            for (int i = 0; i < cards.Count; i++)
                cards[i].ApplyStatus(statuses[i]);

            foreach (var group in _groups)
                group.RecomputeAggregate();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ApplyFilter()
    {
        var needle = _searchText.Trim();
        bool HasNeedle = !string.IsNullOrWhiteSpace(needle);

        // Filter group cards: show if any member matches the filter.
        IEnumerable<GroupCardViewModel> groupQuery = _groups;
        if (_favoritesOnly)
            groupQuery = groupQuery.Where(g => g.HasFavorite);
        if (HasNeedle)
            groupQuery = groupQuery.Where(g => g.Matches(needle));

        // Filter standalone cards.
        IEnumerable<AppCardViewModel> cardQuery = _standalones;
        if (_favoritesOnly)
            cardQuery = cardQuery.Where(c => c.IsFavorite);
        if (HasNeedle)
            cardQuery = cardQuery.Where(c =>
                c.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                c.Entry.Tags.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase)));

        // Project to a uniform sort key: (isFavorite, recency, name).
        var tiles = groupQuery
            .Select(g => (Tile: (object)g, Fav: g.HasFavorite, Recency: g.Recency, Name: g.Name))
            .Concat(cardQuery.Select(c =>
                (Tile: (object)c, Fav: c.IsFavorite,
                 Recency: c.Entry.LastLaunchedAt ?? DateTimeOffset.MinValue, Name: c.Name)))
            .OrderByDescending(t => t.Fav)
            .ThenByDescending(t => t.Recency)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);

        Tiles.Clear();
        foreach (var t in tiles)
            Tiles.Add(t.Tile);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyHint));
    }

    /// <summary>Apply a drag/menu regroup: compute the plan, prompt for a name on new-group
    /// creation (cancel aborts), persist the label-only edits, then re-render. Never starts or
    /// stops a process.</summary>
    public void Regroup(string draggedId, DropTarget target)
    {
        var plan = RegroupPlanner.Plan(draggedId, target, _service.LoadApps());
        if (plan.Kind == DropKind.NoOp)
            return;

        if (plan.NeedsNameDialog)
        {
            plan = PromptForNewGroup(plan);
            if (plan is null)
                return; // user cancelled
        }

        _service.ApplyRegroup(plan);
        LoadAll();
    }

    /// <summary>Show the create-group dialog seeded from the plan; return a plan rewritten with the
    /// chosen name + roles, or null when cancelled.</summary>
    private RegroupPlan? PromptForNewGroup(RegroupPlan plan)
    {
        var apps = _service.LoadApps();
        var seeds = plan.Changes
            .Select(c =>
            {
                var name = apps.FirstOrDefault(a => a.Id == c.AppId)?.Name ?? c.AppId;
                return new GroupMemberSeed(c.AppId, name, c.Role);
            })
            .ToList();

        var result = _dialogs.PromptGroupCreate(plan.SuggestedGroupName ?? "New group", seeds);
        if (result is null)
            return null;

        var changes = plan.Changes
            .Select(c => c with
            {
                Group = result.Name,
                Role = result.RolesByAppId.TryGetValue(c.AppId, out var role) ? role : c.Role,
            })
            .ToList();

        return plan with { Changes = changes, SuggestedGroupName = result.Name };
    }

    private void AddApp()
    {
        if (_dialogs.ShowAddDialog())
            LoadAll();
    }

    private void OnEdit(AppCardViewModel card)
    {
        if (_dialogs.ShowEditDialog(card.Entry))
            LoadAll();
    }

    /// <summary>Persist a flipped favorite flag (atomic ConfigStore save, same door Edit uses)
    /// and re-filter the existing tiles so the favorites-first sort, the favorites filter, and
    /// group cards' <see cref="GroupCardViewModel.HasFavorite"/> all reflect the change immediately
    /// — without rebuilding the VMs (keeps expanded groups and open log panels). On a failed save,
    /// undo the optimistic flip and surface a readable error instead of crashing.</summary>
    private void OnFavoriteToggled(AppCardViewModel card)
    {
        try
        {
            _service.Update(card.Entry);
        }
        catch (Exception ex)
        {
            card.RevertFavorite();
            _dialogs.Info($"Could not save favorite for '{card.Name}': {ex.Message}", "Save failed");
            return;
        }

        ApplyFilter();
    }

    private void OnRemove(AppCardViewModel card)
    {
        if (_dialogs.Confirm($"Remove '{card.Name}' from AppShelf? This stops it if running.", "Remove app"))
        {
            _service.Remove(card.Entry);
            LoadAll();
        }
    }
}
