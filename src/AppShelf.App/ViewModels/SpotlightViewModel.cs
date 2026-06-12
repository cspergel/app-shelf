using AppShelf.App.Services;
using AppShelf.Core.Models;
using AppShelf.Core.Search;

namespace AppShelf.App.ViewModels;

/// <summary>
/// Drives the Spotlight overlay (spec §5.3). Reuses the single long-lived <see cref="GuiAppService"/>
/// so launch/open logic is never duplicated — the overlay is just another front door to Core.
///
/// Results mirror the main grid's tiles: a project group is ONE entry (Enter starts the whole
/// group, backend→frontend), not its members scattered as separate rows. A group still matches when
/// you type a member's name or tag.
/// </summary>
public sealed class SpotlightViewModel : ObservableObject
{
    private readonly GuiAppService _service;
    private string _query = "";
    private IReadOnlyList<ResultRow> _results = Array.Empty<ResultRow>();
    private int _selectedIndex = -1;

    /// <param name="service">The SAME instance used by <see cref="MainViewModel"/>.</param>
    public SpotlightViewModel(GuiAppService service)
    {
        _service = service;
    }

    /// <summary>Bound two-way to the search TextBox. Setting a new value repopulates <see cref="Results"/>.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetField(ref _query, value))
                RefreshResults();
        }
    }

    /// <summary>Ranked result rows, rebuilt on every Query change.</summary>
    public IReadOnlyList<ResultRow> Results
    {
        get => _results;
        private set
        {
            if (SetField(ref _results, value))
                OnPropertyChanged(nameof(HasResults));
        }
    }

    /// <summary>Index of the highlighted row (-1 = none). Bound two-way to the ListBox.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetField(ref _selectedIndex, value);
    }

    /// <summary>True when there is at least one result — drives separator + list visibility.</summary>
    public bool HasResults => _results.Count > 0;

    /// <summary>Called when the overlay becomes visible: clears the query, reloads apps, resets selection.</summary>
    public void Reset()
    {
        _query = ""; // bypass the setter to avoid a double RefreshResults
        OnPropertyChanged(nameof(Query));
        RefreshResults();
    }

    private void RefreshResults()
    {
        var (groups, standalones) = AppGrouping.Organize(_service.LoadApps());

        var items = new List<SpotlightItem>(groups.Count + standalones.Count);
        foreach (var g in groups)
            items.Add(SpotlightItem.ForGroup(g.Name, g.Members));
        foreach (var a in standalones)
            items.Add(SpotlightItem.ForApp(a));

        Results = Rank(_query, items).Select(i => new ResultRow(i)).ToList();
        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Activate the highlighted result (probing status off the UI thread). For a single app: Open if
    /// running, else launch. For a group: if every member is up just open the group's frontend, else
    /// Start-all (backend→frontend, skipping members already running). Fire-and-forget; errors surface
    /// in error.log + the main card. Returns true if an action was taken (so the window can hide).
    /// </summary>
    public async Task<bool> ActivateSelectedAsync()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
            return false;

        var item = Results[SelectedIndex].Item;

        if (item.IsGroup)
        {
            var allRunning = await Task.Run(() =>
                item.Members.All(m => _service.StatusOf(m) == LaunchStatus.Running));
            if (allRunning)
            {
                var target = PickOpenTarget(item.Members);
                if (target is not null)
                    _service.Open(target);
            }
            else
            {
                _ = _service.StartGroupAsync(item.Members);
            }
            return true;
        }

        var entry = item.App!;
        var status = await Task.Run(() => _service.StatusOf(entry));
        if (status == LaunchStatus.Running)
            _service.Open(entry);
        else
            _ = _service.LaunchAsync(entry);

        return true;
    }

    /// <summary>The member to open for a group: its frontend (with a URL), else the first member with a URL.</summary>
    private static AppEntry? PickOpenTarget(IReadOnlyList<AppEntry> members) =>
        members.FirstOrDefault(m => m.Role == AppRoles.Frontend && !string.IsNullOrWhiteSpace(m.Url))
        ?? members.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Url));

    /// <summary>Move the selection by <paramref name="delta"/> (±1), wrapping at boundaries.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
            return;

        var next = SelectedIndex + delta;
        if (next < 0)
            next = Results.Count - 1;
        else if (next >= Results.Count)
            next = 0;
        SelectedIndex = next;
    }

    // ---- Ranking over mixed group/app items (scoring itself stays in Core's FuzzyMatcher) ----

    private static IReadOnlyList<SpotlightItem> Rank(string query, List<SpotlightItem> items)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return items
                .OrderByDescending(i => i.Favorite)
                .ThenByDescending(i => i.Recency)
                .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var scored = new List<(SpotlightItem Item, int Score)>(items.Count);
        foreach (var item in items)
        {
            var best = BestScore(query, item);
            if (best > 0)
                scored.Add((item, best));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.Favorite)
            .ThenByDescending(x => x.Item.Recency)
            .ThenBy(x => x.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Item)
            .ToList();
    }

    /// <summary>Best score for an item: its display name counts double; member names / tags count once
    /// (so typing a member's name surfaces its group).</summary>
    private static int BestScore(string query, SpotlightItem item)
    {
        var nameScore = FuzzyMatcher.Score(query, item.DisplayName);
        var weightedName = nameScore.HasValue ? nameScore.Value * 2 : 0;

        var secondary = 0;
        foreach (var text in item.SearchTexts)
        {
            var s = FuzzyMatcher.Score(query, text);
            if (s.HasValue && s.Value > secondary)
                secondary = s.Value;
        }

        return Math.Max(weightedName, secondary);
    }
}

/// <summary>A Spotlight candidate: either a single app or a whole group (mirrors a grid tile).</summary>
public sealed class SpotlightItem
{
    public bool IsGroup { get; private init; }
    public string DisplayName { get; private init; } = "";
    public string? Subtitle { get; private init; }
    public bool Favorite { get; private init; }
    public DateTimeOffset Recency { get; private init; }

    /// <summary>The app to act on (single-app items only).</summary>
    public AppEntry? App { get; private init; }

    /// <summary>The group's members in start order (group items only); empty for app items.</summary>
    public IReadOnlyList<AppEntry> Members { get; private init; } = Array.Empty<AppEntry>();

    /// <summary>Secondary strings to fuzzy-match besides the display name (member names + tags).</summary>
    public IReadOnlyList<string> SearchTexts { get; private init; } = Array.Empty<string>();

    public static SpotlightItem ForApp(AppEntry entry) => new()
    {
        IsGroup = false,
        DisplayName = entry.Name,
        Subtitle = null,
        Favorite = entry.Favorite,
        Recency = entry.LastLaunchedAt ?? DateTimeOffset.MinValue,
        App = entry,
        SearchTexts = entry.Tags?.ToList() ?? new List<string>(),
    };

    public static SpotlightItem ForGroup(string name, IReadOnlyList<AppEntry> members) => new()
    {
        IsGroup = true,
        DisplayName = name,
        Subtitle = members.Count == 1 ? "1 app" : $"{members.Count} apps",
        Favorite = members.Any(m => m.Favorite),
        Recency = members.Select(m => m.LastLaunchedAt ?? DateTimeOffset.MinValue)
                         .DefaultIfEmpty(DateTimeOffset.MinValue).Max(),
        Members = members,
        SearchTexts = members.Select(m => m.Name)
                             .Concat(members.SelectMany(m => m.Tags ?? new List<string>()))
                             .ToList(),
    };
}

/// <summary>One row in the Spotlight result list (display projection over a <see cref="SpotlightItem"/>).</summary>
public sealed class ResultRow
{
    public SpotlightItem Item { get; }
    public string Name => Item.DisplayName;
    public string? Subtitle => Item.Subtitle;

    public ResultRow(SpotlightItem item) => Item = item;
}
