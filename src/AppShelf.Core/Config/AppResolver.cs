using AppShelf.Core.Models;

namespace AppShelf.Core.Config;

public enum ResolveOutcome { None, Single, Ambiguous }

public sealed record ResolveResult(ResolveOutcome Outcome, AppEntry? Match, IReadOnlyList<AppEntry> Candidates);

/// <summary>
/// Resolves a user-supplied token to an app: an exact id match, else a case-insensitive name
/// match (spec §4). Ambiguous name matches are reported so the caller can list them and exit.
/// Implemented once in Core so the CLI and GUI resolve identically.
/// </summary>
public static class AppResolver
{
    public static ResolveResult Resolve(IEnumerable<AppEntry> apps, string token)
    {
        var list = apps as IReadOnlyList<AppEntry> ?? apps.ToList();

        var byId = list.FirstOrDefault(a => string.Equals(a.Id, token, StringComparison.Ordinal));
        if (byId is not null)
            return new ResolveResult(ResolveOutcome.Single, byId, new[] { byId });

        var byName = list.Where(a => string.Equals(a.Name, token, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count switch
        {
            0 => new ResolveResult(ResolveOutcome.None, null, Array.Empty<AppEntry>()),
            1 => new ResolveResult(ResolveOutcome.Single, byName[0], byName),
            _ => new ResolveResult(ResolveOutcome.Ambiguous, null, byName),
        };
    }
}
