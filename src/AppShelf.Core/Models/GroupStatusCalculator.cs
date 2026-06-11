namespace AppShelf.Core.Models;

/// <summary>Rolls member statuses into one group pill: Starting wins; then all-up = Running,
/// none-up = Stopped, mixed = Partial.</summary>
public static class GroupStatusCalculator
{
    public static GroupAggregateStatus Aggregate(IEnumerable<LaunchStatus> members)
    {
        var list = members.ToList();
        if (list.Count == 0) return GroupAggregateStatus.Stopped;
        if (list.Any(s => s == LaunchStatus.Starting)) return GroupAggregateStatus.Starting;

        var running = list.Count(s => s == LaunchStatus.Running);
        if (running == 0) return GroupAggregateStatus.Stopped;
        return running == list.Count ? GroupAggregateStatus.Running : GroupAggregateStatus.Partial;
    }
}
