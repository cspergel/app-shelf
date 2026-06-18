using System.Net.Sockets;

namespace AppShelf.Core.Process;

/// <summary>What we can observe about a process holding a port (shown so the user decides).</summary>
public sealed record ProcessEvidence(
    int Pid,
    string ProcessName,
    string? ExePath,
    string? CommandLine,
    string? ExeDir,
    DateTimeOffset? StartedAt,
    bool ParentAlive,
    bool IsService = false,
    IReadOnlyList<AncestorInfo>? Ancestry = null,
    string? ServiceName = null);

/// <summary>One node in a process's parent chain (parent, grandparent, …). <c>Alive</c> is false
/// when the snapshot-time liveness lookup returned no start time (dead/inaccessible). <c>ExeDir</c>
/// is the directory of the executable from WMI (Win32_Process has no working-directory field) —
/// render it as "exe dir:", not "working dir:".</summary>
public sealed record AncestorInfo(
    int Pid,
    string ProcessName,
    bool Alive,
    string? CommandLine,
    string? ExeDir);

/// <summary>Supplies evidence for a PID. Windows impl uses System.Diagnostics + P/Invoke.</summary>
public interface IProcessEvidenceProvider
{
    ProcessEvidence ForPid(int pid);
}

/// <summary>One scanned listener with ownership mapping and tier.</summary>
public sealed record PortReport(
    int Port,
    AddressFamily Family,
    ProcessEvidence Evidence,
    string? OwnerAppId,
    string? OwnerAppName,
    PortTier Tier);
