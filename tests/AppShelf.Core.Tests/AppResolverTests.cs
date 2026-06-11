using AppShelf.Core.Config;
using AppShelf.Core.Models;

namespace AppShelf.Core.Tests;

public class AppResolverTests
{
    private static readonly List<AppEntry> Apps = new()
    {
        new AppEntry { Id = "dashboard", Name = "dashboard", Url = "http://x" },
        new AppEntry { Id = "planner", Name = "Planner", Url = "http://y" },
        new AppEntry { Id = "dup-1", Name = "Demo", Url = "http://a" },
        new AppEntry { Id = "dup-2", Name = "Demo", Url = "http://b" },
    };

    [Fact]
    public void Resolve_ExactId_ReturnsSingle()
    {
        var r = AppResolver.Resolve(Apps, "planner");

        Assert.Equal(ResolveOutcome.Single, r.Outcome);
        Assert.Equal("planner", r.Match!.Id);
    }

    [Fact]
    public void Resolve_NameCaseInsensitive_ReturnsSingle()
    {
        var r = AppResolver.Resolve(Apps, "planner");
        var r2 = AppResolver.Resolve(Apps, "PLANNER");

        Assert.Equal(ResolveOutcome.Single, r2.Outcome);
        Assert.Equal("planner", r2.Match!.Id);
    }

    [Fact]
    public void Resolve_AmbiguousName_ReturnsAmbiguousWithCandidates()
    {
        var r = AppResolver.Resolve(Apps, "demo");

        Assert.Equal(ResolveOutcome.Ambiguous, r.Outcome);
        Assert.Null(r.Match);
        Assert.Equal(2, r.Candidates.Count);
    }

    [Fact]
    public void Resolve_Unknown_ReturnsNone()
    {
        var r = AppResolver.Resolve(Apps, "nope");

        Assert.Equal(ResolveOutcome.None, r.Outcome);
    }

    [Fact]
    public void Resolve_IdMatchTakesPrecedenceOverName()
    {
        var apps = new List<AppEntry>
        {
            new() { Id = "demo", Name = "Something", Url = "http://x" },
            new() { Id = "other", Name = "demo", Url = "http://y" },
        };

        var r = AppResolver.Resolve(apps, "demo");

        Assert.Equal(ResolveOutcome.Single, r.Outcome);
        Assert.Equal("demo", r.Match!.Id); // matched by id, not the other's name
    }
}
