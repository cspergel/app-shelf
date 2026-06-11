namespace AppShelf.Core.Models;

/// <summary>Splits a flat app list into groups (by <see cref="AppEntry.Group"/>) and standalones.</summary>
public static class AppGrouping
{
    public static (IReadOnlyList<AppGroup> Groups, IReadOnlyList<AppEntry> Standalones) Organize(
        IEnumerable<AppEntry> apps)
    {
        var list = apps.ToList();
        var standalones = list.Where(a => string.IsNullOrWhiteSpace(a.Group)).ToList();
        var groups = list
            .Where(a => !string.IsNullOrWhiteSpace(a.Group))
            .GroupBy(a => a.Group!.Trim())
            .Select(g => new AppGroup(
                g.Key,
                g.OrderBy(a => AppRoles.Order(a.Role))
                 .ThenBy(a => a.Order ?? int.MaxValue)
                 .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                 .ToList()))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (groups, standalones);
    }
}
