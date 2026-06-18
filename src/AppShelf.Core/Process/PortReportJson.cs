using System.Net.Sockets;
using System.Text.Json;

namespace AppShelf.Core.Process;

/// <summary>
/// Flat, scriptable projection of a <see cref="PortReport"/> for `appshelf doctor --json`.
/// camelCase JSON. <c>Family</c> is normalized to "ipv4"/"ipv6" and <c>Tier</c> to the enum name.
/// The ancestry chain is deliberately excluded (a future `--full` flag could add it).
/// </summary>
internal sealed record PortReportJsonDto(
    int Port,
    string Family,
    string Tier,
    int Pid,
    string ProcessName,
    bool IsService,
    string? ServiceName,
    bool ParentAlive,
    string? OwnerAppId,
    string? OwnerAppName,
    string? ExePath,
    string? CommandLine,
    DateTimeOffset? StartedAt);

/// <summary>Pure JSON projection for port scans. No Console, no OS calls — unit-testable.</summary>
public static class PortReportJson
{
    public static string ToJson(IReadOnlyList<PortReport> reports, bool indented)
    {
        var dtos = reports.Select(r => new PortReportJsonDto(
            Port: r.Port,
            Family: r.Family == AddressFamily.InterNetworkV6 ? "ipv6" : "ipv4",
            Tier: r.Tier.ToString(),
            Pid: r.Evidence.Pid,
            ProcessName: r.Evidence.ProcessName,
            IsService: r.Evidence.IsService,
            ServiceName: r.Evidence.ServiceName,
            ParentAlive: r.Evidence.ParentAlive,
            OwnerAppId: r.OwnerAppId,
            OwnerAppName: r.OwnerAppName,
            ExePath: r.Evidence.ExePath,
            CommandLine: r.Evidence.CommandLine,
            StartedAt: r.Evidence.StartedAt)).ToList();

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
        };

        return JsonSerializer.Serialize(dtos, options);
    }
}
