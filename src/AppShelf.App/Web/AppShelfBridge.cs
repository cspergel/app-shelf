using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using AppShelf.App.Services;
using AppShelf.Core.Models;
using Microsoft.Web.WebView2.Wpf;

namespace AppShelf.App.Web;

/// <summary>
/// JS &lt;-&gt; C# message bridge for the WebView2-hosted web UI (spike).
///
/// Protocol (request/response, requestId-correlated):
///   JS  -&gt; C#:  { requestId, method, args }
///   C#  -&gt; JS:  { requestId, ok, result?, error? }
///
/// <see cref="GuiAppService"/> calls run on a background thread (the engine is thread-safe —
/// <see cref="AppShelf.Core.Process.ProcessManager"/> uses a ConcurrentDictionary and the port
/// probes are static) so blocking work never freezes the UI thread; responses are posted back via
/// <see cref="Microsoft.Web.WebView2.Core.CoreWebView2.PostWebMessageAsJson"/>, marshalled to the
/// UI thread in <see cref="PostResponse"/>. This is a thin front door — it adds NO logic.
/// </summary>
public sealed class AppShelfBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly WebView2 _webView;
    private readonly GuiAppService _service;
    private readonly Dispatcher _dispatcher;

    public AppShelfBridge(WebView2 webView, GuiAppService service)
    {
        _webView = webView;
        _service = service;
        _dispatcher = webView.Dispatcher;
        _webView.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>What JS posts: a method name, optional args, and a correlation id.</summary>
    private sealed record Request(string RequestId, string Method, JsonElement Args);

    /// <summary>Card-grid row: an <see cref="AppEntry"/> projection + its live status.</summary>
    private sealed record AppView(
        string Id, string Name, string Url, string? Framework, bool Favorite,
        string? Group, string Role, int? Port, string Status, IReadOnlyList<string> Tags);

    /// <summary>The web UI's "crashed" state. The Core <see cref="LaunchStatus"/> enum has no
    /// such member (crash detection is GUI-only), so the bridge emits this string directly when a
    /// managed app exited unexpectedly. Mirrors the WPF crash watcher's intent without replicating
    /// its debounce state (the web UI polls every ~2s; one managed-and-exited signal is enough).</summary>
    private const string StoppedUnexpectedly = "StoppedUnexpectedly";

    private async void OnWebMessageReceived(
        object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        Request? request = null;
        try
        {
            request = JsonSerializer.Deserialize<Request>(e.WebMessageAsJson, JsonOptions);
        }
        catch
        {
            // Not a request shape we recognise — ignore (could be a stray devtools message).
            return;
        }

        if (request is null || string.IsNullOrEmpty(request.RequestId))
            return;

        try
        {
            // Run the engine work OFF the UI thread: StatusOf/IsPortListening do blocking TCP
            // probes (~1s each) and the web UI polls every 2s — doing this on the dispatcher
            // thread freezes the window (no move/min/max/focus). The engine is thread-safe
            // (ProcessManager uses a ConcurrentDictionary; port checks are static). Only the
            // PostWebMessageAsJson response is marshalled back to the UI thread (see PostResponse).
            var result = await Task.Run(() => DispatchAsync(request.Method, request.Args));
            PostResponse(request.RequestId, ok: true, result: result);
        }
        catch (Exception ex)
        {
            PostResponse(request.RequestId, ok: false, error: ex.Message);
        }
    }

    /// <summary>Map a method name to a <see cref="GuiAppService"/> call. Returns the value to
    /// serialize back as <c>result</c> (or null for void methods).</summary>
    private async Task<object?> DispatchAsync(string method, JsonElement args)
    {
        switch (method)
        {
            case "listApps":
                return ListApps();

            case "launch":
            {
                var entry = ResolveApp(args);
                await _service.LaunchAsync(entry);
                return null;
            }

            case "stop":
            {
                var entry = ResolveApp(args);
                _service.Stop(entry);
                return null;
            }

            default:
                throw new InvalidOperationException($"Unknown bridge method '{method}'.");
        }
    }

    private IReadOnlyList<AppView> ListApps() =>
        _service.LoadApps()
            .Select(a => new AppView(
                a.Id, a.Name, a.Url, a.Framework, a.Favorite,
                a.Group, a.Role, a.Port, StatusFor(a), a.Tags))
            .ToList();

    /// <summary>Live status name for the grid. A managed app whose process exited unexpectedly is
    /// reported as <see cref="StoppedUnexpectedly"/> (the engine would otherwise call it Stopped),
    /// so the web UI's "crashed" treatment lights up.
    ///
    /// PortInUse is re-projected to "Running" for this resting card-grid display: the engine's
    /// owner-aware status calls a listening-but-not-managed-by-this-session port PortInUse (red),
    /// but for the grid a serving registered port means the app is up — regardless of who launched
    /// it. Real port conflicts are surfaced at LAUNCH time (pre-flight), not in the resting status.
    /// This projection is web-bridge-only; AppShelf.Core and GuiAppService.StatusOf keep the
    /// owner-aware behavior for the WPF path and launch pre-flight.
    ///
    /// All other statuses (Stopped/Starting/Running/Error) pass through verbatim — identical to what
    /// the enum-string serializer would have produced.</summary>
    private string StatusFor(AppEntry entry)
    {
        if (_service.DidManagedAppExit(entry))
            return StoppedUnexpectedly;

        var status = _service.StatusOf(entry);
        if (status == LaunchStatus.PortInUse)
            return LaunchStatus.Running.ToString();

        return status.ToString();
    }

    /// <summary>Resolve an <c>{ id }</c> arg to a live <see cref="AppEntry"/> via the config store
    /// (so we never trust a stale entry the JS side might have cached).</summary>
    private AppEntry ResolveApp(JsonElement args)
    {
        if (!args.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Missing 'id' argument.");

        var id = idProp.GetString();
        var entry = _service.LoadApps().FirstOrDefault(a => a.Id == id)
            ?? throw new InvalidOperationException($"No app with id '{id}'.");
        return entry;
    }

    private void PostResponse(string requestId, bool ok, object? result = null, string? error = null)
    {
        void Post()
        {
            // The WebView2 may have torn down (window closed) between request and response.
            if (_webView.CoreWebView2 is null)
                return;

            var payload = JsonSerializer.Serialize(
                new { requestId, ok, result, error }, JsonOptions);
            _webView.CoreWebView2.PostWebMessageAsJson(payload);
        }

        if (_dispatcher.CheckAccess())
            Post();
        else
            _dispatcher.BeginInvoke(Post);
    }
}
