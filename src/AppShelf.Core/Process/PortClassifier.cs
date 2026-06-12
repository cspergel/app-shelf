namespace AppShelf.Core.Process;

/// <summary>
/// Honest, evidence-based classification of a live port. We never guess intent: the only
/// "orphan" signal we trust is a dead parent process. Unknown ports are never auto-killed.
/// </summary>
public static class PortClassifier
{
    public static PortTier Classify(
        int port,
        int pid,
        bool parentAlive,
        IReadOnlySet<int> registeredPorts,
        IReadOnlySet<int> managedPids)
    {
        if (managedPids.Contains(pid))
            return PortTier.Managed;
        if (registeredPorts.Contains(port))
            return parentAlive ? PortTier.Registered : PortTier.LikelyOrphaned;
        return PortTier.Unknown;
    }
}
