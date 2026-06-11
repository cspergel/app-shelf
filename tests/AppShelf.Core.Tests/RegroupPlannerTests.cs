using AppShelf.Core.Models;
using Xunit;

namespace AppShelf.Core.Tests;

public class RegroupPlannerTests
{
    private static AppEntry A(
        string id, string? group = null, string role = AppRoles.Other,
        string? framework = null, string? dir = null, string? name = null, int? order = null) =>
        new()
        {
            Id = id,
            Name = name ?? id,
            Url = "http://localhost:3000",
            Group = group,
            Role = role,
            Framework = framework,
            Dir = dir,
            Order = order,
        };

    // ---- Gesture rules ----

    [Fact]
    public void SelfDrop_IsNoOp()
    {
        var apps = new[] { A("a"), A("b") };
        var plan = RegroupPlanner.Plan("a", new OnApp("a"), apps);
        Assert.Equal(DropKind.NoOp, plan.Kind);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void DropOnApp_BothUngrouped_CreatesNewGroup_NeedsDialog()
    {
        var apps = new[]
        {
            A("be", framework: "FastAPI", name: "API"),
            A("fe", framework: "Vite", name: "Web"),
        };
        var plan = RegroupPlanner.Plan("be", new OnApp("fe"), apps);

        Assert.Equal(DropKind.NewGroup, plan.Kind);
        Assert.True(plan.NeedsNameDialog);
        Assert.False(plan.DissolvesSourceGroup);
        Assert.NotNull(plan.SuggestedGroupName);

        // Both apps set to the suggested group, with guessed roles.
        var beChange = Assert.Single(plan.Changes, c => c.AppId == "be");
        var feChange = Assert.Single(plan.Changes, c => c.AppId == "fe");
        Assert.Equal(plan.SuggestedGroupName, beChange.Group);
        Assert.Equal(AppRoles.Backend, beChange.Role);
        Assert.Equal(AppRoles.Frontend, feChange.Role);
    }

    [Fact]
    public void DropOnApp_DraggedGrouped_TargetStandalone_PullsTargetIntoDraggedsGroup()
    {
        var apps = new[] {
            A("d1", group: "G", role: AppRoles.Backend),
            A("d2", group: "G", role: AppRoles.Frontend),   // keeps G alive (no dissolve)
            A("dragged", group: "G", role: AppRoles.Backend),
            A("solo", framework: "Vite"),
        };
        var plan = RegroupPlanner.Plan("dragged", new OnApp("solo"), apps);
        Assert.Equal(DropKind.JoinGroup, plan.Kind);
        var ch = Assert.Single(plan.Changes);
        Assert.Equal("solo", ch.AppId);   // the STANDALONE joins, not the dragged
        Assert.Equal("G", ch.Group);
        Assert.False(plan.DissolvesSourceGroup);
    }

    [Fact]
    public void DropOnApp_TargetIsGrouped_JoinsTargetsGroup()
    {
        var apps = new[]
        {
            A("member", group: "Acme", role: AppRoles.Backend),
            A("dragged", framework: "Vite"),
        };
        var plan = RegroupPlanner.Plan("dragged", new OnApp("member"), apps);

        Assert.Equal(DropKind.JoinGroup, plan.Kind);
        Assert.False(plan.NeedsNameDialog);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("dragged", change.AppId);
        Assert.Equal("Acme", change.Group);
        Assert.Equal(AppRoles.Frontend, change.Role);
    }

    [Fact]
    public void DropOnGroup_FromUngrouped_Joins()
    {
        var apps = new[]
        {
            A("m1", group: "Acme", role: AppRoles.Backend),
            A("m2", group: "Acme", role: AppRoles.Frontend),
            A("dragged", framework: "Streamlit"),
        };
        var plan = RegroupPlanner.Plan("dragged", new OnGroup("Acme"), apps);

        Assert.Equal(DropKind.JoinGroup, plan.Kind);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("Acme", change.Group);
        Assert.Equal(AppRoles.Backend, change.Role); // streamlit → backend
        Assert.False(plan.DissolvesSourceGroup);
    }

    [Fact]
    public void DropOnGroup_AlreadyInThatGroup_IsNoOp()
    {
        var apps = new[]
        {
            A("m1", group: "Acme", role: AppRoles.Backend),
            A("m2", group: "Acme", role: AppRoles.Frontend),
        };
        var plan = RegroupPlanner.Plan("m1", new OnGroup("Acme"), apps);
        Assert.Equal(DropKind.NoOp, plan.Kind);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Move_BetweenGroups_NoDissolve_WhenSourceKeepsTwoPlus()
    {
        var apps = new[]
        {
            A("a", group: "Src", role: AppRoles.Backend),
            A("b", group: "Src", role: AppRoles.Frontend),
            A("c", group: "Src", role: AppRoles.Other),
            A("d", group: "Dst", role: AppRoles.Backend),
        };
        var plan = RegroupPlanner.Plan("a", new OnGroup("Dst"), apps);

        Assert.Equal(DropKind.MoveGroup, plan.Kind);
        Assert.False(plan.DissolvesSourceGroup); // Src still has b, c
        var change = Assert.Single(plan.Changes);
        Assert.Equal("Dst", change.Group);
    }

    [Fact]
    public void Move_BetweenGroups_DissolvesSource_WhenLeftWithOne()
    {
        var apps = new[]
        {
            A("a", group: "Src", role: AppRoles.Backend),
            A("b", group: "Src", role: AppRoles.Frontend),
            A("d", group: "Dst", role: AppRoles.Backend),
        };
        var plan = RegroupPlanner.Plan("a", new OnGroup("Dst"), apps);

        Assert.Equal(DropKind.MoveGroup, plan.Kind);
        Assert.True(plan.DissolvesSourceGroup);

        var moved = Assert.Single(plan.Changes, c => c.AppId == "a");
        Assert.Equal("Dst", moved.Group);

        var loner = Assert.Single(plan.Changes, c => c.AppId == "b");
        Assert.Null(loner.Group); // b becomes standalone
        Assert.Equal(AppRoles.Frontend, loner.Role); // role preserved
    }

    [Fact]
    public void Leave_ToCanvas_NoDissolve_WhenSourceKeepsTwoPlus()
    {
        var apps = new[]
        {
            A("a", group: "G", role: AppRoles.Backend),
            A("b", group: "G", role: AppRoles.Frontend),
            A("c", group: "G", role: AppRoles.Other),
        };
        var plan = RegroupPlanner.Plan("a", new OnCanvas(), apps);

        Assert.Equal(DropKind.LeaveGroup, plan.Kind);
        Assert.False(plan.DissolvesSourceGroup);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("a", change.AppId);
        Assert.Null(change.Group);
    }

    [Fact]
    public void Leave_ToCanvas_DissolvesSource_WhenLeftWithOne()
    {
        var apps = new[]
        {
            A("a", group: "G", role: AppRoles.Backend),
            A("b", group: "G", role: AppRoles.Frontend),
        };
        var plan = RegroupPlanner.Plan("a", new OnCanvas(), apps);

        Assert.Equal(DropKind.LeaveGroup, plan.Kind);
        Assert.True(plan.DissolvesSourceGroup);
        Assert.Equal(2, plan.Changes.Count);
        Assert.All(plan.Changes, c => Assert.Null(c.Group)); // both end standalone
    }

    [Fact]
    public void Leave_ToCanvas_AlreadyStandalone_IsNoOp()
    {
        var apps = new[] { A("a"), A("b", group: "G", role: AppRoles.Backend), A("c", group: "G") };
        var plan = RegroupPlanner.Plan("a", new OnCanvas(), apps);
        Assert.Equal(DropKind.NoOp, plan.Kind);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void DuplicateName_Merge_BothUngroupedIntoExistingGroup()
    {
        // Two apps under …\Proj\ → suggested name "Proj", which already exists as a group.
        var apps = new[]
        {
            A("existing", group: "Proj", role: AppRoles.Backend),
            A("a", dir: @"C:\code\Proj\frontend", name: "Web"),
            A("b", dir: @"C:\code\Proj\backend", name: "Api"),
        };
        var plan = RegroupPlanner.Plan("a", new OnApp("b"), apps);

        Assert.Equal(DropKind.JoinGroup, plan.Kind); // merge, not new group
        Assert.False(plan.NeedsNameDialog);
        Assert.Equal("Proj", plan.SuggestedGroupName);
        Assert.Equal(2, plan.Changes.Count);
        Assert.All(plan.Changes, c => Assert.Equal("Proj", c.Group));
    }

    [Fact]
    public void DropOnMissingApp_IsNoOp()
    {
        var apps = new[] { A("a") };
        Assert.Equal(DropKind.NoOp, RegroupPlanner.Plan("a", new OnApp("ghost"), apps).Kind);
        Assert.Equal(DropKind.NoOp, RegroupPlanner.Plan("ghost", new OnApp("a"), apps).Kind);
    }

    // ---- Reorder (used by Slice 2; logic verified here) ----

    [Fact]
    public void Reorder_RenumbersRolePeers_AndMovesToIndex()
    {
        var apps = new[]
        {
            A("x", group: "G", role: AppRoles.Backend, name: "apple"),
            A("y", group: "G", role: AppRoles.Backend, name: "mango"),
            A("z", group: "G", role: AppRoles.Backend, name: "zebra"),
        };
        // Move "zebra" (currently last by name) to index 0.
        var plan = RegroupPlanner.Plan("z", new AtMemberIndex("G", 0), apps);

        Assert.Equal(DropKind.Reorder, plan.Kind);
        // z first, then apple(x), then mango(y) → orders 0,1,2 (full renumbering).
        Assert.Equal(new[]{ ("z",0),("x",1),("y",2) },
            plan.Changes.Select(c => (c.AppId, c.Order!.Value)).ToArray());
    }

    // ---- Role guessing ----

    [Theory]
    [InlineData("Vite", AppRoles.Frontend)]
    [InlineData("Next.js", AppRoles.Frontend)]
    [InlineData("react-scripts", AppRoles.Frontend)]
    [InlineData("Astro", AppRoles.Frontend)]
    [InlineData("@sveltejs/kit", AppRoles.Frontend)]
    [InlineData("@angular/core", AppRoles.Frontend)]
    [InlineData("FastAPI", AppRoles.Backend)]
    [InlineData("Flask", AppRoles.Backend)]
    [InlineData("uvicorn", AppRoles.Backend)]
    [InlineData("Streamlit", AppRoles.Backend)]
    [InlineData("gradio", AppRoles.Backend)]
    [InlineData("SomethingElse", AppRoles.Other)]
    [InlineData(null, AppRoles.Other)]
    [InlineData("", AppRoles.Other)]
    public void GuessRole_MapsFrameworks(string? framework, string expected) =>
        Assert.Equal(expected, RegroupPlanner.GuessRole(framework));

    // ---- Name guessing ----

    [Fact]
    public void SuggestName_SharedParentFolder_Wins()
    {
        var a = A("a", dir: @"C:\code\My Project\frontend", name: "Web");
        var b = A("b", dir: @"C:\code\My Project\backend", name: "Api");
        Assert.Equal("My Project", RegroupPlanner.SuggestName(a, b));
    }

    [Fact]
    public void SuggestName_CommonLeadingWords_WhenNoSharedFolder()
    {
        var a = A("a", name: "Acme Frontend");
        var b = A("b", name: "Acme Backend");
        Assert.Equal("Acme", RegroupPlanner.SuggestName(a, b));
    }

    [Fact]
    public void SuggestName_Fallback_WhenNothingShared()
    {
        var a = A("a", name: "Alpha");
        var b = A("b", name: "Beta");
        Assert.Equal("Alpha group", RegroupPlanner.SuggestName(a, b));
    }

    [Fact]
    public void SuggestName_ToleratesForwardSlashes()
    {
        var a = A("a", dir: "C:/code/Proj/frontend", name: "Web");
        var b = A("b", dir: "C:/code/Proj/backend", name: "Api");
        Assert.Equal("Proj", RegroupPlanner.SuggestName(a, b));
    }
}
