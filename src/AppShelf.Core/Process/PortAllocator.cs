namespace AppShelf.Core.Process;

/// <summary>
/// Pure port-reservation allocator (port registry). Given the set of ports already reserved
/// by other apps and an optional preferred port (e.g. a framework default), returns a port
/// that does not clash:
///
/// - if the preferred port is free, keep it (even when outside the configured range — it is
///   the project's natural port);
/// - otherwise hand out the first free port in [<see cref="RangeStart"/>, <see cref="RangeEnd"/>].
///
/// No I/O — callers pass in the reserved set (typically from config.json) so this stays
/// deterministic and unit-testable.
/// </summary>
public sealed class PortAllocator
{
    public int RangeStart { get; }
    public int RangeEnd { get; }

    public PortAllocator(int rangeStart = 4000, int rangeEnd = 4099)
    {
        if (rangeStart > rangeEnd)
            throw new ArgumentException($"Invalid port range: start {rangeStart} > end {rangeEnd}.", nameof(rangeStart));

        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
    }

    public int Allocate(IEnumerable<int> reserved, int? preferred = null)
    {
        var taken = new HashSet<int>(reserved);

        if (preferred is int p && !taken.Contains(p))
            return p;

        for (var port = RangeStart; port <= RangeEnd; port++)
            if (!taken.Contains(port))
                return port;

        throw new InvalidOperationException(
            $"No free port available in range {RangeStart}-{RangeEnd}. " +
            "Free up a reserved port or widen the range.");
    }
}
