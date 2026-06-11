using AppShelf.Core.Models;
using Xunit;

namespace AppShelf.Core.Tests;

public class GroupStatusCalculatorTests
{
    [Theory]
    [InlineData(new[] { LaunchStatus.Running, LaunchStatus.Running }, GroupAggregateStatus.Running)]
    [InlineData(new[] { LaunchStatus.Running, LaunchStatus.Stopped }, GroupAggregateStatus.Partial)]
    [InlineData(new[] { LaunchStatus.Stopped, LaunchStatus.Stopped }, GroupAggregateStatus.Stopped)]
    [InlineData(new[] { LaunchStatus.Starting, LaunchStatus.Stopped }, GroupAggregateStatus.Starting)]
    [InlineData(new[] { LaunchStatus.Running, LaunchStatus.Error }, GroupAggregateStatus.Partial)]
    public void Aggregate_FollowsRules(LaunchStatus[] members, GroupAggregateStatus expected)
    {
        Assert.Equal(expected, GroupStatusCalculator.Aggregate(members));
    }

    [Fact]
    public void Aggregate_NoMembers_IsStopped()
        => Assert.Equal(GroupAggregateStatus.Stopped, GroupStatusCalculator.Aggregate(Array.Empty<LaunchStatus>()));
}
