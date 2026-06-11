using AppShelf.Core.Process;

namespace AppShelf.Core.Tests;

public class PortAllocatorTests
{
    [Fact]
    public void Allocate_PreferredFree_ReturnsPreferred()
    {
        var alloc = new PortAllocator(4000, 4099);

        var port = alloc.Allocate(reserved: new[] { 3000 }, preferred: 5173);

        Assert.Equal(5173, port);
    }

    [Fact]
    public void Allocate_PreferredTaken_ReturnsFirstFreeInRange()
    {
        var alloc = new PortAllocator(4000, 4099);

        var port = alloc.Allocate(reserved: new[] { 5173 }, preferred: 5173);

        Assert.Equal(4000, port);
    }

    [Fact]
    public void Allocate_NoPreferred_ReturnsFirstFreeInRange()
    {
        var alloc = new PortAllocator(4000, 4099);

        var port = alloc.Allocate(reserved: new[] { 4000, 4001 }, preferred: null);

        Assert.Equal(4002, port);
    }

    [Fact]
    public void Allocate_SkipsReservedHolesInRange()
    {
        var alloc = new PortAllocator(4000, 4099);

        var port = alloc.Allocate(reserved: new[] { 4000, 4002 }, preferred: null);

        Assert.Equal(4001, port);
    }

    [Fact]
    public void Allocate_RangeExhausted_Throws()
    {
        var alloc = new PortAllocator(4000, 4001);

        Assert.Throws<InvalidOperationException>(() =>
            alloc.Allocate(reserved: new[] { 4000, 4001 }, preferred: null));
    }

    [Fact]
    public void Allocate_PreferredTakenAndRangePartlyFull_ReturnsNextHole()
    {
        var alloc = new PortAllocator(4000, 4099);

        var port = alloc.Allocate(reserved: new[] { 3000, 4000, 4001, 4002 }, preferred: 3000);

        Assert.Equal(4003, port);
    }

    [Fact]
    public void Allocate_IgnoresDuplicateReservations()
    {
        var alloc = new PortAllocator(4000, 4099);

        var port = alloc.Allocate(reserved: new[] { 4000, 4000, 4000 }, preferred: null);

        Assert.Equal(4001, port);
    }

    [Fact]
    public void Constructor_InvalidRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PortAllocator(5000, 4000));
    }
}
