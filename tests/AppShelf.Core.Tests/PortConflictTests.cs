using System.Collections.Generic;
using AppShelf.Core.Process;
using Xunit;

namespace AppShelf.Core.Tests;

public class PortConflictTests
{
    [Fact]
    public void NullListenerPid_ReturnsFalse()
    {
        Assert.False(PortConflict.IsConflict(null, new HashSet<int> { 1, 2, 3 }));
    }

    [Fact]
    public void ListenerPidInManagedSet_ReturnsFalse()
    {
        Assert.False(PortConflict.IsConflict(42, new HashSet<int> { 42 }));
    }

    [Fact]
    public void ListenerPidNotInManagedSet_ReturnsTrue()
    {
        Assert.True(PortConflict.IsConflict(99, new HashSet<int> { 1, 2, 3 }));
    }
}
