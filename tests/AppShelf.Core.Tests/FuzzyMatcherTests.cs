using AppShelf.Core.Models;
using AppShelf.Core.Search;

namespace AppShelf.Core.Tests;

public class FuzzyMatcherTests
{
    private static AppEntry App(
        string name,
        bool favorite = false,
        DateTimeOffset? lastLaunched = null,
        params string[] tags) =>
        new()
        {
            Id = name.ToLowerInvariant().Replace(' ', '-'),
            Name = name,
            Url = "http://localhost:1234",
            Favorite = favorite,
            LastLaunchedAt = lastLaunched,
            Tags = tags.ToList(),
        };

    [Fact]
    public void ExactPrefixMatch_ScoresHigherThanScattered()
    {
        var prefix = FuzzyMatcher.Score("vite", "Vite App");
        var scattered = FuzzyMatcher.Score("vite", "Various items tested eventually");

        Assert.NotNull(prefix);
        Assert.NotNull(scattered);
        Assert.True(prefix > scattered);
    }

    [Fact]
    public void PrefixBonus_BeatsScatteredMatch()
    {
        var atStart = FuzzyMatcher.Score("sn", "Snapshot Tool");
        var scattered = FuzzyMatcher.Score("sn", "Assistant Notes"); // s...n scattered, not prefix

        Assert.NotNull(atStart);
        Assert.NotNull(scattered);
        Assert.True(atStart > scattered);
    }

    [Fact]
    public void Score_IsCaseInsensitive()
    {
        var upper = FuzzyMatcher.Score("VITE", "vite dev server");
        var lower = FuzzyMatcher.Score("vite", "vite dev server");

        Assert.NotNull(upper);
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        Assert.Null(FuzzyMatcher.Score("xyz", "react app"));
    }

    [Fact]
    public void EmptyQuery_Rank_ReturnsAllSortedByFavoriteThenRecency()
    {
        var older = DateTimeOffset.Now.AddHours(-2);
        var newer = DateTimeOffset.Now.AddMinutes(-5);

        var plainOld = App("Alpha", favorite: false, lastLaunched: older);
        var plainNew = App("Beta", favorite: false, lastLaunched: newer);
        var fav = App("Zeta", favorite: true, lastLaunched: older);

        var ranked = FuzzyMatcher.Rank("", new[] { plainOld, plainNew, fav });

        Assert.Equal(3, ranked.Count);
        Assert.Equal("Zeta", ranked[0].Name);  // favorite first
        Assert.Equal("Beta", ranked[1].Name);  // newer non-favorite
        Assert.Equal("Alpha", ranked[2].Name); // older non-favorite
    }

    [Fact]
    public void TagMatch_IsFound()
    {
        var entry = App("portal", tags: "backend");

        var ranked = FuzzyMatcher.Rank("back", new[] { entry });

        Assert.Single(ranked);
        Assert.Equal("portal", ranked[0].Name);
    }

    [Fact]
    public void NameMatch_ScoresHigherThanTagMatch_AtSamePosition()
    {
        // Same query matched as a prefix in both: name weight is 2×, so the name match must win.
        var nameMatch = App("backend service", tags: "frontend");
        var tagMatch = App("portal", tags: "backend");

        var ranked = FuzzyMatcher.Rank("back", new[] { tagMatch, nameMatch });

        Assert.Equal(2, ranked.Count);
        Assert.Equal("backend service", ranked[0].Name); // name (weighted) outranks tag
    }

    [Fact]
    public void Rank_OrdersByScoreThenTiebreaks()
    {
        var older = DateTimeOffset.Now.AddHours(-3);
        var newer = DateTimeOffset.Now.AddMinutes(-1);

        var strong = App("api server", lastLaunched: older);   // "api" prefix → high score
        var weakA = App("rapid", lastLaunched: older);         // "api" scattered → lower score
        var weakB = App("snapi", favorite: true, lastLaunched: newer); // also lower (not prefix)

        var ranked = FuzzyMatcher.Rank("api", new[] { weakA, weakB, strong });

        Assert.Equal(3, ranked.Count);
        Assert.Equal("api server", ranked[0].Name); // prefix wins on raw score
    }

    [Fact]
    public void Rank_ExcludesNonMatchingApps()
    {
        var match = App("vite app");
        var noMatch = App("django backend");

        var ranked = FuzzyMatcher.Rank("vite", new[] { match, noMatch });

        Assert.Single(ranked);
        Assert.Equal("vite app", ranked[0].Name);
        Assert.DoesNotContain(ranked, e => e.Name == "django backend");
    }

    [Fact]
    public void ContiguousMatch_BeatsScatteredMatch()
    {
        var contiguous = FuzzyMatcher.Score("abc", "abcdef");
        var scattered = FuzzyMatcher.Score("abc", "axbxcx");

        Assert.NotNull(contiguous);
        Assert.NotNull(scattered);
        Assert.True(contiguous > scattered);
    }

    [Fact]
    public void EmptyQuery_NullLastLaunched_SortsAfterNonNull()
    {
        var never = App("Never", favorite: false, lastLaunched: null);
        var recent = App("Recent", favorite: false, lastLaunched: DateTimeOffset.Now.AddMinutes(-1));

        var ranked = FuzzyMatcher.Rank("   ", new[] { never, recent });

        Assert.Equal("Recent", ranked[0].Name); // non-null recency first
        Assert.Equal("Never", ranked[1].Name);  // null recency last
    }
}
