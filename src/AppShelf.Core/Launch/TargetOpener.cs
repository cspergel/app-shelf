using AppShelf.Core.Models;

namespace AppShelf.Core.Launch;

/// <summary>Opens an app's target (its URL). The only seam between v0 (browser) and v1.1 (webview).</summary>
public interface ITargetOpener
{
    void Open(AppEntry entry);
}

/// <summary>
/// v0 target opener (spec §3.4 / §6): launches the default browser at the app's URL.
///
/// The <c>open == "webview"</c> branch is the single v1.1 seam — in v0 it gracefully falls
/// back to the browser. When WebView2 lands, only <see cref="OpenWebView"/> changes.
/// </summary>
public sealed class TargetOpener : ITargetOpener
{
    public void Open(AppEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Url))
            throw new InvalidOperationException($"App '{entry.Id}' has no URL to open.");

        if (entry.Open == OpenTargets.WebView)
        {
            OpenWebView(entry);
            return;
        }

        OpenInBrowser(entry.Url);
    }

    private static void OpenInBrowser(string url)
    {
        // Only navigate http/https. With UseShellExecute=true the OS hands ANY registered URI
        // scheme to its handler — file:, ms-settings:, steam:, a bare exe path — so an unguarded
        // value could launch an app instead of opening a browser. Reject anything that isn't web.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Refusing to open '{url}': only http:// and https:// URLs are supported.");
        }

        // UseShellExecute=true lets the OS pick the default browser.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    // v1.1 seam: embedded WebView2 window. Stubbed for v0 — falls back to the browser.
    private static void OpenWebView(AppEntry entry) => OpenInBrowser(entry.Url);
}
