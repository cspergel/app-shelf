using System.IO;

namespace AppShelf.Core.Models;

/// <summary>The kind of regroup gesture a drop resolves to.</summary>
public enum DropKind
{
    /// <summary>Nothing changes (self-drop, or drop onto current group/position).</summary>
    NoOp,

    /// <summary>Two ungrouped apps become a brand-new group (needs the name dialog).</summary>
    NewGroup,

    /// <summary>An app joins an existing group it was not already in.</summary>
    JoinGroup,

    /// <summary>A grouped app leaves its group and becomes standalone.</summary>
    LeaveGroup,

    /// <summary>A grouped app moves from one group to another.</summary>
    MoveGroup,

    /// <summary>Members within a group are reordered (Slice 2).</summary>
    Reorder,
}

/// <summary>A single field edit to persist for one app. A null <see cref="Group"/> means ungroup.</summary>
public sealed record RegroupChange(string AppId, string? Group, string Role, int? Order = null);

/// <summary>The computed result of a drop: the per-app edits plus metadata for the UI.</summary>
public sealed record RegroupPlan(
    DropKind Kind,
    IReadOnlyList<RegroupChange> Changes,
    bool DissolvesSourceGroup,
    string? SuggestedGroupName,   // NewGroup only
    bool NeedsNameDialog)         // true only for NewGroup
{
    /// <summary>A plan that changes nothing.</summary>
    public static RegroupPlan NoOp { get; } =
        new(DropKind.NoOp, Array.Empty<RegroupChange>(), false, null, false);
}

/// <summary>Where a card was dropped.</summary>
public abstract record DropTarget;

/// <summary>Dropped onto another card — standalone or a group member.</summary>
public sealed record OnApp(string AppId) : DropTarget;

/// <summary>Dropped onto a group card or its member area.</summary>
public sealed record OnGroup(string GroupName) : DropTarget;

/// <summary>Dropped onto the ungrouped canvas/background → leave group.</summary>
public sealed record OnCanvas : DropTarget;

/// <summary>Dropped between members of a group at a specific index → reorder (Slice 2).</summary>
public sealed record AtMemberIndex(string GroupName, int Index) : DropTarget;

/// <summary>
/// Pure brain of drag-to-group: given a dragged app, a drop target, and the current app list,
/// computes the <see cref="AppEntry"/> field edits to persist. No I/O, no side effects, fully
/// unit-tested. Regrouping is label-only — it never starts or stops a process.
/// </summary>
public static class RegroupPlanner
{
    /// <summary>Compute the plan for dropping <paramref name="draggedId"/> onto <paramref name="target"/>.</summary>
    public static RegroupPlan Plan(string draggedId, DropTarget target, IReadOnlyList<AppEntry> apps)
    {
        var dragged = apps.FirstOrDefault(a => a.Id == draggedId);
        if (dragged is null)
            return RegroupPlan.NoOp;

        return target switch
        {
            OnApp onApp => PlanOnApp(dragged, onApp.AppId, apps),
            OnGroup onGroup => PlanJoin(dragged, onGroup.GroupName, apps),
            OnCanvas => PlanLeave(dragged, apps),
            AtMemberIndex reorder => PlanReorder(dragged, reorder, apps),
            _ => RegroupPlan.NoOp,
        };
    }

    private static RegroupPlan PlanOnApp(AppEntry dragged, string targetId, IReadOnlyList<AppEntry> apps)
    {
        if (targetId == dragged.Id)
            return RegroupPlan.NoOp; // self-drop

        var target = apps.FirstOrDefault(a => a.Id == targetId);
        if (target is null)
            return RegroupPlan.NoOp;

        // Target is grouped → join (or move into) the target's group.
        if (!string.IsNullOrWhiteSpace(target.Group))
            return PlanJoin(dragged, target.Group!.Trim(), apps);

        // Target is standalone.
        if (!string.IsNullOrWhiteSpace(dragged.Group))
        {
            // INTENDED DIRECTION (Slice 1, confirmed): dropping an already-grouped card onto a
            // standalone card pulls the STANDALONE into the dragged card's existing group. The
            // dragged card stays put in its group; the standalone is the one that joins. This is
            // the intuitive "drop A onto B groups them" reading — do not invert this in Slice 2.
            return PlanJoin(dragged, dragged.Group!.Trim(), apps, joinerOverride: target);
        }

        // Both ungrouped → brand-new group.
        var suggested = SuggestName(dragged, target);
        return MaybeMergeNewGroup(dragged, target, suggested, apps);
    }

    /// <summary>Both apps ungrouped. If the suggested name matches an existing group, it's a merge
    /// (JoinGroup); otherwise a NewGroup needing the name dialog.</summary>
    private static RegroupPlan MaybeMergeNewGroup(
        AppEntry dragged, AppEntry target, string suggested, IReadOnlyList<AppEntry> apps)
    {
        var existing = ExistingGroupName(suggested, apps);
        if (existing is not null)
        {
            // Suggested name collides with a real group → merge both apps into it.
            var changes = new List<RegroupChange>
            {
                new(dragged.Id, existing, GuessRole(dragged.Framework)),
                new(target.Id, existing, GuessRole(target.Framework)),
            };
            return new RegroupPlan(DropKind.JoinGroup, changes, false, existing, NeedsNameDialog: false);
        }

        var newChanges = new List<RegroupChange>
        {
            new(dragged.Id, suggested, GuessRole(dragged.Framework)),
            new(target.Id, suggested, GuessRole(target.Framework)),
        };
        return new RegroupPlan(DropKind.NewGroup, newChanges, false, suggested, NeedsNameDialog: true);
    }

    /// <summary>The joiner moves into <paramref name="groupName"/>. If it left a source group that now
    /// has a single remaining member, that group dissolves (the lone member goes standalone).</summary>
    private static RegroupPlan PlanJoin(
        AppEntry dragged, string groupName, IReadOnlyList<AppEntry> apps,
        AppEntry? joinerOverride = null)
    {
        var joiner = joinerOverride ?? dragged;
        var sourceGroup = string.IsNullOrWhiteSpace(joiner.Group) ? null : joiner.Group!.Trim();

        // Already in this group → no-op.
        if (string.Equals(sourceGroup, groupName, StringComparison.OrdinalIgnoreCase))
            return RegroupPlan.NoOp;

        var changes = new List<RegroupChange>
        {
            new(joiner.Id, groupName, GuessRole(joiner.Framework)),
        };

        var kind = sourceGroup is null ? DropKind.JoinGroup : DropKind.MoveGroup;
        var dissolves = AddDissolveIfOrphaned(sourceGroup, joiner.Id, apps, changes);

        return new RegroupPlan(kind, changes, dissolves, groupName, NeedsNameDialog: false);
    }

    /// <summary>The dragged (grouped) app leaves its group and becomes standalone. Auto-dissolve
    /// the source if it's left with a single member.</summary>
    private static RegroupPlan PlanLeave(AppEntry dragged, IReadOnlyList<AppEntry> apps)
    {
        var sourceGroup = string.IsNullOrWhiteSpace(dragged.Group) ? null : dragged.Group!.Trim();
        if (sourceGroup is null)
            return RegroupPlan.NoOp; // already standalone

        var changes = new List<RegroupChange>
        {
            // Leave the group; keep the app's role label (it's harmless when ungrouped).
            new(dragged.Id, null, dragged.Role),
        };

        var dissolves = AddDissolveIfOrphaned(sourceGroup, dragged.Id, apps, changes);
        return new RegroupPlan(DropKind.LeaveGroup, changes, dissolves, null, NeedsNameDialog: false);
    }

    /// <summary>Reorder within a group: renumber the group's members (spread 0..n) placing the
    /// dragged member at the requested index, among its role peers (Slice 2).</summary>
    private static RegroupPlan PlanReorder(AppEntry dragged, AtMemberIndex target, IReadOnlyList<AppEntry> apps)
    {
        var sourceGroup = string.IsNullOrWhiteSpace(dragged.Group) ? null : dragged.Group!.Trim();
        if (!string.Equals(sourceGroup, target.GroupName, StringComparison.OrdinalIgnoreCase))
            return RegroupPlan.NoOp; // reorder only applies within the dragged's own group

        // Current members in display order (role → Order → name), filtered to the dragged's role
        // so reorder stays within the role band that Order actually controls.
        var peers = apps
            .Where(a => string.Equals(a.Group?.Trim(), target.GroupName, StringComparison.OrdinalIgnoreCase))
            .Where(a => a.Role == dragged.Role)
            .OrderBy(a => a.Order ?? int.MaxValue)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var without = peers.Where(a => a.Id != dragged.Id).ToList();
        var index = Math.Clamp(target.Index, 0, without.Count);
        without.Insert(index, dragged);

        // No-op if the order is unchanged.
        if (without.Select(a => a.Id).SequenceEqual(peers.Select(a => a.Id)))
            return RegroupPlan.NoOp;

        var changes = new List<RegroupChange>();
        for (var i = 0; i < without.Count; i++)
        {
            var a = without[i];
            changes.Add(new RegroupChange(a.Id, a.Group, a.Role, i));
        }

        return new RegroupPlan(DropKind.Reorder, changes, false, target.GroupName, NeedsNameDialog: false);
    }

    /// <summary>If <paramref name="sourceGroup"/> would be left with exactly one member after
    /// <paramref name="leavingId"/> departs, append a change clearing that lone member's group and
    /// return true (the group dissolves).</summary>
    private static bool AddDissolveIfOrphaned(
        string? sourceGroup, string leavingId, IReadOnlyList<AppEntry> apps, List<RegroupChange> changes)
    {
        if (sourceGroup is null)
            return false;

        var remaining = apps
            .Where(a => a.Id != leavingId)
            .Where(a => string.Equals(a.Group?.Trim(), sourceGroup, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (remaining.Count != 1)
            return false;

        var lone = remaining[0];
        changes.Add(new RegroupChange(lone.Id, null, lone.Role)); // keep role; just go standalone
        return true;
    }

    /// <summary>Returns the canonical existing group name matching <paramref name="name"/>
    /// (case-insensitive), or null when no such group exists.</summary>
    private static string? ExistingGroupName(string name, IReadOnlyList<AppEntry> apps) =>
        apps.Select(a => a.Group?.Trim())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .FirstOrDefault(g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase));

    // ---- Suggestion helpers (Task 3) ----

    /// <summary>Maps a detected framework to a group role. Case-insensitive substring match.</summary>
    public static string GuessRole(string? framework)
    {
        if (string.IsNullOrWhiteSpace(framework))
            return AppRoles.Other;

        var f = framework.ToLowerInvariant();

        string[] frontend = { "vite", "next", "react-scripts", "react", "astro", "svelte", "sveltekit", "angular" };
        string[] backend = { "fastapi", "flask", "uvicorn", "streamlit", "gradio", "django" };

        if (frontend.Any(t => f.Contains(t)))
            return AppRoles.Frontend;
        if (backend.Any(t => f.Contains(t)))
            return AppRoles.Backend;
        return AppRoles.Other;
    }

    /// <summary>Suggests a group name for two apps being grouped:
    /// (1) shared immediate parent folder of both <see cref="AppEntry.Dir"/>s;
    /// (2) common leading whitespace-delimited words of the two names;
    /// (3) fallback: first app's name + " group".</summary>
    public static string SuggestName(AppEntry a, AppEntry b)
    {
        var shared = SharedParentFolderName(a.Dir, b.Dir);
        if (shared is not null)
            return shared;

        var prefix = CommonLeadingWords(a.Name, b.Name);
        if (!string.IsNullOrWhiteSpace(prefix))
            return prefix;

        return $"{a.Name.Trim()} group";
    }

    /// <summary>If both dirs share an immediate common parent folder, returns that folder's name.
    /// Examples: <c>…\My Project\frontend</c> + <c>…\My Project\backend</c> →
    /// "My Project"; also the case where one dir IS the parent of the other.</summary>
    private static string? SharedParentFolderName(string? dirA, string? dirB)
    {
        if (string.IsNullOrWhiteSpace(dirA) || string.IsNullOrWhiteSpace(dirB))
            return null;

        var pa = NormalizeDir(dirA);
        var pb = NormalizeDir(dirB);

        // Same folder: use its own name.
        if (string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase))
            return SafeName(pa);

        var parentA = Path.GetDirectoryName(pa);
        var parentB = Path.GetDirectoryName(pb);

        // Sibling subfolders (…\X\frontend + …\X\backend) → "X".
        if (!string.IsNullOrEmpty(parentA) && string.Equals(parentA, parentB, StringComparison.OrdinalIgnoreCase))
            return SafeName(parentA);

        // One dir is the immediate parent of the other (…\X + …\X\backend) → "X".
        if (string.Equals(parentB, pa, StringComparison.OrdinalIgnoreCase))
            return SafeName(pa);
        if (string.Equals(parentA, pb, StringComparison.OrdinalIgnoreCase))
            return SafeName(pb);

        return null;
    }

    private static string NormalizeDir(string dir) =>
        dir.Replace('/', '\\').TrimEnd('\\');

    private static string? SafeName(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Common leading whitespace-delimited words of two names (trimmed). Returns empty
    /// when the first words differ.</summary>
    private static string CommonLeadingWords(string nameA, string nameB)
    {
        var wa = (nameA ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wb = (nameB ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var common = new List<string>();
        for (var i = 0; i < Math.Min(wa.Length, wb.Length); i++)
        {
            if (!string.Equals(wa[i], wb[i], StringComparison.OrdinalIgnoreCase))
                break;
            common.Add(wa[i]);
        }

        return string.Join(' ', common);
    }
}
