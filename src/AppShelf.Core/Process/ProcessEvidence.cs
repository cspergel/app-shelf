using System.Net.Sockets;

namespace AppShelf.Core.Process;

/// <summary>What we can observe about a process holding a port (shown so the user decides).</summary>
public sealed record ProcessEvidence(
    int Pid,
    string ProcessName,
    string? ExePath,
    string? CommandLine,
    string? WorkingDir,
    DateTimeOffset? StartedAt,
    bool ParentAlive,
    bool IsService = false);

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
