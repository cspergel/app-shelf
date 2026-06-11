namespace AppShelf.Core.Models;

/// <summary>Small helpers for keeping an app's URL consistent with its reserved port.</summary>
public static class UrlPort
{
    /// <summary>The port from a URL (the actually-bound port), or null if unparseable.</summary>
    public static int? FromUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Port > 0 ? uri.Port : null;

    /// <summary>Rewrites a URL to use a new port, preserving scheme/host/path.</summary>
    public static string WithPort(string url, int port)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"http://localhost:{port}";
        var builder = new UriBuilder(uri) { Port = port };
        var result = builder.Uri.ToString();
        return result.EndsWith('/') && !url.EndsWith('/') ? result.TrimEnd('/') : result;
    }
}
