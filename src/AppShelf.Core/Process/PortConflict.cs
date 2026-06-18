namespace AppShelf.Core.Process;

/// <summary>A foreign process holding a registered port: the port, the owning PID, and the
/// process name (best-effort; "unknown" when it could not be resolved).</summary>
public sealed record PortConflictInfo(int Port, int Pid, string ProcessName);

/// <summary>Pure decision helper for port-conflict pre-flight (no OS calls): a conflict exists
/// when something is listening on the port and that listener is NOT one of our managed PIDs.</summary>
public static class PortConflict
{
    public static bool IsConflict(int? listenerPid, IReadOnlySet<int> managedPids)
        => listenerPid.HasValue && !managedPids.Contains(listenerPid.Value);
}
