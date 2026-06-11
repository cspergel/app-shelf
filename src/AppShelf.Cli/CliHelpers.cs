using AppShelf.Core.Config;
using AppShelf.Core.Models;

namespace AppShelf.Cli;

/// <summary>Shared CLI helpers: app resolution, port/url utilities, and consistent output.</summary>
public static class CliHelpers
{
    public static void Error(string message) => Console.Error.WriteLine($"appshelf: {message}");

    public static void Info(string message) => Console.WriteLine(message);

    /// <summary>
    /// Resolves a token to a single app, printing a clear message and returning null when the
    /// token matches nothing or is ambiguous (spec §4).
    /// </summary>
    public static AppEntry? ResolveOrReport(AppConfig config, string token)
    {
        var result = AppResolver.Resolve(config.Apps, token);
        switch (result.Outcome)
        {
            case ResolveOutcome.Single:
                return result.Match;
            case ResolveOutcome.None:
                Error($"no app matches '{token}'. Run 'appshelf list' to see your apps.");
                return null;
            default:
                Error($"'{token}' is ambiguous. Matches: {string.Join(", ", result.Candidates.Select(a => a.Id))}. " +
                      "Use the id instead.");
                return null;
        }
    }

    /// <summary>The port from an app's URL (the actually-bound port), or null if unparseable.</summary>
    public static int? PortFromUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Port > 0 ? uri.Port : null;

    /// <summary>The effective port: the reserved port if set, otherwise the URL's port.</summary>
    public static int? EffectivePort(AppEntry entry) => entry.Port ?? PortFromUrl(entry.Url);

    /// <summary>Rewrites a localhost-style URL to use a new port, preserving scheme/host/path.</summary>
    public static string WithPort(string url, int port)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"http://localhost:{port}";
        var builder = new UriBuilder(uri) { Port = port };
        var result = builder.Uri.ToString();
        return result.EndsWith('/') && !url.EndsWith('/') ? result.TrimEnd('/') : result;
    }

    /// <summary>Ports already reserved by other apps (excluding the given id), for the allocator.</summary>
    public static IEnumerable<int> ReservedPorts(AppConfig config, string? excludeId = null) =>
        config.Apps
            .Where(a => a.Port.HasValue && !string.Equals(a.Id, excludeId, StringComparison.Ordinal))
            .Select(a => a.Port!.Value);
}
