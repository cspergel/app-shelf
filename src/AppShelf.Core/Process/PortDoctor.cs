using System.Net.Sockets;
using AppShelf.Core.Models;

namespace AppShelf.Core.Process;

/// <summary>
/// Scans relevant ports (registered app ports + common dev ports), maps each live listener
/// back to a registered app, and classifies it. Sources are injected so it is unit-testable.
/// </summary>
public sealed class PortDoctor
{
    private readonly Func<IReadOnlyList<(int Port, int Pid, AddressFamily Family)>> _listeners;
    private readonly IProcessEvidenceProvider _evidence;

    public PortDoctor(
        Func<IReadOnlyList<(int Port, int Pid, AddressFamily Family)>>? listeners = null,
        IProcessEvidenceProvider? evidence = null)
    {
        _listeners = listeners ?? PortProcessFinder.ListListeners;
        _evidence = evidence ?? new WindowsProcessEvidenceProvider();
    }

    public IReadOnlyList<PortReport> Scan(IReadOnlyList<AppEntry> apps, IReadOnlySet<int> managedPids)
    {
        var byPort = apps.Where(a => a.Port.HasValue)
                         .GroupBy(a => a.Port!.Value)
                         .ToDictionary(g => g.Key, g => g.First());
        var registeredPorts = byPort.Keys.ToHashSet();
        var candidates = registeredPorts.Concat(DevPorts.Common).ToHashSet();

        // A server listening on both IPv4 and IPv6 yields one listener row per family for the
        // same port. Dedup to at most one report per port, preferring the IPv4 row for display.
        var deduped = _listeners()
            .Where(l => candidates.Contains(l.Port))
            .GroupBy(l => l.Port)
            .Select(g => g.OrderBy(l => l.Family == AddressFamily.InterNetwork ? 0 : 1).First());

        var reports = new List<PortReport>();
        foreach (var (port, pid, family) in deduped)
        {
            var ev = _evidence.ForPid(pid);
            var tier = PortClassifier.Classify(port, pid, ev.ParentAlive, registeredPorts, managedPids);
            byPort.TryGetValue(port, out var owner);
            reports.Add(new PortReport(port, family, ev, owner?.Id, owner?.Name, tier));
        }
        return reports.OrderBy(r => r.Port).ToList();
    }
}
