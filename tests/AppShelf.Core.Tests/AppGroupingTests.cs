using AppShelf.Core.Models;
using Xunit;

namespace AppShelf.Core.Tests;

public class AppGroupingTests
{
    private static AppEntry A(string id, string? group = null, string role = "other", int? order = null) =>
        new() { Id = id, Name = id, Url = "http://localhost:3000", Group = group, Role = role, Order = order };

    [Fact]
    public void Organize_SplitsGroupedFromStandalone()
    {
        var apps = new[]
        {
            A("be", "Acme", AppRoles.Backend),
            A("fe", "Acme", AppRoles.Frontend),
            A("solo"),
        };

        var (groups, standalones) = AppGrouping.Organize(apps);

        var group = Assert.Single(groups);
        Assert.Equal("Acme", group.Name);
        Assert.Equal(new[] { "be", "fe" }, group.Members.Select(m => m.Id)); // backend before frontend
        Assert.Equal(new[] { "solo" }, standalones.Select(m => m.Id));
    }

    [Fact]
    public void Organize_OrdersMembers_BackendFirst_RegardlessOfInputOrder()
    {
        var apps = new[] { A("fe", "G", AppRoles.Frontend), A("be", "G", AppRoles.Backend) };
        var (groups, _) = AppGrouping.Organize(apps);
        Assert.Equal(new[] { "be", "fe" }, groups.Single().Members.Select(m => m.Id));
    }

    [Fact]
    public void Organize_OrderBreaksTies_AmongSameRoleMembers()
    {
        // Two backends. Name order ("apple" < "zebra") would put apple first, but Order
        // overrides: zebra has Order=1, apple has Order=2 → zebra comes first.
        var apps = new[]
        {
            A("apple", "G", AppRoles.Backend, order: 2),
            A("zebra", "G", AppRoles.Backend, order: 1),
        };
        var (groups, _) = AppGrouping.Organize(apps);
        Assert.Equal(new[] { "zebra", "apple" }, groups.Single().Members.Select(m => m.Id));
    }

    [Fact]
    public void Organize_RoleStillDominates_OverOrder()
    {
        // A frontend with a tiny Order does NOT jump ahead of a backend (role wins first).
        var apps = new[]
        {
            A("fe", "G", AppRoles.Frontend, order: 0),
            A("be", "G", AppRoles.Backend, order: 99),
        };
        var (groups, _) = AppGrouping.Organize(apps);
        Assert.Equal(new[] { "be", "fe" }, groups.Single().Members.Select(m => m.Id));
    }

    [Fact]
    public void Organize_NullOrder_FallsBackToName()
    {
        // Same role, no Order on either → alphabetical by name.
        var apps = new[]
        {
            A("zeta", "G", AppRoles.Other),
            A("alpha", "G", AppRoles.Other),
        };
        var (groups, _) = AppGrouping.Organize(apps);
        Assert.Equal(new[] { "alpha", "zeta" }, groups.Single().Members.Select(m => m.Id));
    }

    [Fact]
    public void Organize_EmptyOrWhitespaceGroup_IsStandalone()
    {
        var apps = new[] { A("a", ""), A("b", "   ") };
        var (groups, standalones) = AppGrouping.Organize(apps);
        Assert.Empty(groups);
        Assert.Equal(2, standalones.Count);
    }
}
