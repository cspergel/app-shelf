using AppShelf.Core.Models;

namespace AppShelf.Core.Search;

/// <summary>
/// Pure subsequence fuzzy matcher + ranker for the Spotlight overlay (spec §5.3). No UI deps —
/// lives in Core so it is unit-testable and so the "one source of truth" rule holds: scoring is
/// defined once here, not in the WPF layer.
/// </summary>
public static class FuzzyMatcher
{
    // Word-boundary characters: a matched char following one of these earns a boundary bonus.
    private static readonly char[] BoundaryChars = { ' ', '-', '.', '/', '_' };

    private const int BaseScore = 10;
    private const int ContiguousRunFactor = 5;   // per char in a contiguous run
    private const int PrefixBonus = 30;          // first matched char is at index 0
    private const int WordBoundaryBonus = 10;    // per matched char that follows a boundary char

    /// <summary>
    /// Subsequence score of <paramref name="query"/> against <paramref name="text"/>
    /// (case-insensitive). Returns null when query has no subsequence match. A matched score is
    /// always ≥ 1; higher is better.
    /// </summary>
    public static int? Score(string query, string text)
    {
        if (string.IsNullOrEmpty(query))
            return null; // empty query = "show all" is handled by Rank, never a per-text score
        if (string.IsNullOrEmpty(text))
            return null;

        var q = query.ToLowerInvariant();
        var t = text.ToLowerInvariant();

        // Subsequence walk: collect the index in t where each query char matched.
        var matchedIndices = new List<int>(q.Length);
        var qi = 0;
        for (var ti = 0; ti < t.Length && qi < q.Length; ti++)
        {
            if (t[ti] == q[qi])
            {
                matchedIndices.Add(ti);
                qi++;
            }
        }

        if (qi < q.Length)
            return null; // not all query chars consumed → no match

        var score = BaseScore;

        // Contiguous run bonus: each run of consecutive matched indices adds run_length² * factor.
        var runLength = 1;
        for (var i = 1; i < matchedIndices.Count; i++)
        {
            if (matchedIndices[i] == matchedIndices[i - 1] + 1)
            {
                runLength++;
            }
            else
            {
                // Superlinear (run_length²) so contiguous runs beat the same chars scattered apart.
                score += runLength * runLength * ContiguousRunFactor;
                runLength = 1;
            }
        }
        // Superlinear (run_length²) so contiguous runs beat the same chars scattered apart.
        score += runLength * runLength * ContiguousRunFactor; // flush the final run

        // Prefix bonus: first matched char is at the very start of the text.
        if (matchedIndices[0] == 0)
            score += PrefixBonus;

        // Word-boundary bonus: matched char (not at index 0) follows a boundary char.
        foreach (var idx in matchedIndices)
        {
            if (idx > 0 && Array.IndexOf(BoundaryChars, t[idx - 1]) >= 0)
                score += WordBoundaryBonus;
        }

        return score;
    }

    /// <summary>
    /// Rank entries against a query. Empty/whitespace query → all entries, sorted by
    /// Favorite desc, LastLaunchedAt desc (nulls last), Name asc. Non-empty query → entries whose
    /// best score (Name weighted 2×, Tags weighted 1×) is &gt; 0, sorted by score desc, then the
    /// same tiebreak chain.
    /// </summary>
    public static IReadOnlyList<AppEntry> Rank(string query, IEnumerable<AppEntry> entries)
    {
        var list = entries as IReadOnlyList<AppEntry> ?? entries.ToList();

        if (string.IsNullOrWhiteSpace(query))
        {
            return list
                .OrderByDescending(e => e.Favorite)
                .ThenByDescending(e => e.LastLaunchedAt ?? DateTimeOffset.MinValue)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var scored = new List<(AppEntry Entry, int Score)>(list.Count);
        foreach (var entry in list)
        {
            var best = BestScore(query, entry);
            if (best > 0)
                scored.Add((entry, best));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.Favorite)
            .ThenByDescending(x => x.Entry.LastLaunchedAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Entry)
            .ToList();
    }

    /// <summary>Best match score for an entry: Name matches count double, any tag match counts once.</summary>
    private static int BestScore(string query, AppEntry entry)
    {
        var nameScore = Score(query, entry.Name);
        var weightedName = nameScore.HasValue ? nameScore.Value * 2 : 0;

        var tagScore = 0;
        if (entry.Tags is { Count: > 0 })
        {
            foreach (var tag in entry.Tags)
            {
                var s = Score(query, tag);
                if (s.HasValue && s.Value > tagScore)
                    tagScore = s.Value;
            }
        }

        return Math.Max(weightedName, tagScore);
    }
}
