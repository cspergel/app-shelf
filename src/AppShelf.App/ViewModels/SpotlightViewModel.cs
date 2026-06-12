using AppShelf.App.Services;
using AppShelf.Core.Models;
using AppShelf.Core.Search;

namespace AppShelf.App.ViewModels;

/// <summary>
/// Drives the Spotlight overlay (spec §5.3). Reuses the single long-lived <see cref="GuiAppService"/>
/// so launch/open logic is never duplicated — the overlay is just another front door to Core.
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
        var apps = _service.LoadApps();
        var ranked = FuzzyMatcher.Rank(_query, apps);
        Results = ranked.Select(e => new ResultRow(e)).ToList();
        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Activate the highlighted result: probe its status off the UI thread, then Open (if running)
    /// or LaunchAsync (fire-and-forget). Returns true if an action was taken (so the window can hide).
    /// </summary>
    public async Task<bool> ActivateSelectedAsync()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
            return false;

        var entry = Results[SelectedIndex].Entry;
        var status = await Task.Run(() => _service.StatusOf(entry));
        if (status == LaunchStatus.Running)
            _service.Open(entry);
        else
            _ = _service.LaunchAsync(entry); // fire-and-forget; errors surface in error.log + main card

        return true;
    }

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
}

/// <summary>One row in the Spotlight result list.</summary>
public sealed class ResultRow
{
    public AppEntry Entry { get; }
    public string Name { get; }
    public string? Subtitle { get; } // group name (dim display), or null when ungrouped

    public ResultRow(AppEntry entry)
    {
        Entry = entry;
        Name = entry.Name;
        Subtitle = string.IsNullOrEmpty(entry.Group) ? null : entry.Group;
    }
}
