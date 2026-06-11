using System.Collections.Generic;
using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests;

public class PortClassifierTests
{
    private static readonly HashSet<int> RegisteredPorts = new() { 8000 };

    [Fact]
    public void Managed_WhenPidIsManaged()
    {
        var tier = PortClassifier.Classify(port: 8000, pid: 100, parentAlive: true,
            registeredPorts: RegisteredPorts, managedPids: new HashSet<int> { 100 });
        Assert.Equal(PortTier.Managed, tier);
    }

    [Fact]
    public void Registered_WhenPortKnown_AndParentAlive()
    {
        var tier = PortClassifier.Classify(8000, 100, true, RegisteredPorts, new HashSet<int>());
        Assert.Equal(PortTier.Registered, tier);
    }

    [Fact]
    public void LikelyOrphaned_WhenPortKnown_ButParentDead()
    {
        var tier = PortClassifier.Classify(8000, 100, false, RegisteredPorts, new HashSet<int>());
        Assert.Equal(PortTier.LikelyOrphaned, tier);
    }

    [Fact]
    public void Unknown_WhenPortNotRegistered()
    {
        var tier = PortClassifier.Classify(3000, 100, false, RegisteredPorts, new HashSet<int>());
        Assert.Equal(PortTier.Unknown, tier);
    }

    [Fact]
    public void Managed_TakesPrecedence_OverRegistered()
    {
        var tier = PortClassifier.Classify(8000, 100, false, RegisteredPorts, new HashSet<int> { 100 });
        Assert.Equal(PortTier.Managed, tier);
    }
}
