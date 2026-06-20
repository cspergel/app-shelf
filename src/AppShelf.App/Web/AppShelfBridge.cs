using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using AppShelf.App.Services;
using AppShelf.Core.Detection;
using AppShelf.Core.Launch;
using AppShelf.Core.Models;
using AppShelf.Core.Process;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

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

    /// <summary>Card-grid row: an <see cref="AppEntry"/> projection + its live status.
    /// <c>Dir</c> is included so the web UI can hide dir-dependent quick actions (open
    /// terminal/editor/folder/.env, copy path) for URL-only apps.</summary>
    private sealed record AppView(
        string Id, string Name, string Url, string? Framework, bool Favorite,
        string? Group, string Role, int? Port, string Status, IReadOnlyList<string> Tags,
        string? Dir);

    /// <summary>Port Doctor row: a rich <see cref="PortReport"/> projection. Unlike the CLI's
    /// <see cref="AppShelf.Core.Process.PortReportJson"/> (flat, drops ancestry) this keeps the full
    /// ancestry chain the GUI's expand-row needs.</summary>
    private sealed record PortRowView(
        int Port, string Family, string Tier, int Pid, string ProcessName, bool IsService,
        string? ServiceName, bool ParentAlive, string? OwnerAppId, string? OwnerAppName,
        string? ExePath, string? CommandLine, string? ExeDir, DateTimeOffset? StartedAt,
        IReadOnlyList<AncestorView> Ancestry);

    /// <summary>One node in the process parent chain, projected for the expand-row.</summary>
    private sealed record AncestorView(
        int Pid, string ProcessName, bool Alive, string? CommandLine, string? ExeDir);

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

            // ── Quick dev actions (per-app ⋯ menu) ───────────────────────────
            case "openTerminal":
                return QuickResult(QuickActions.OpenTerminal(ResolveApp(args).Dir));

            case "openEditor":
                return QuickResult(QuickActions.OpenEditor(ResolveApp(args).Dir, null));

            case "openFolder":
                return QuickResult(QuickActions.OpenFolder(ResolveApp(args).Dir));

            case "openEnv":
            {
                var entry = ResolveApp(args);
                if (string.IsNullOrWhiteSpace(entry.Dir))
                    return QuickResult(QuickActions.ActionResult.Fail("This app has no folder (it's URL-only)."));

                var envPath = Path.Combine(entry.Dir, ".env");
                if (!File.Exists(envPath))
                    return QuickResult(QuickActions.ActionResult.Fail("No .env in this folder."));

                return QuickResult(QuickActions.OpenFile(envPath));
            }

            case "openDocs":
            {
                var entry = ResolveApp(args);
                if (string.IsNullOrWhiteSpace(entry.Url))
                    return QuickResult(QuickActions.ActionResult.Fail("This app has no URL."));

                // FastAPI serves interactive docs at /docs; everyone else just opens the app URL.
                // (A per-app docsPath field is a future add.)
                var url = IsFastApi(entry.Framework)
                    ? CombineUrl(entry.Url, "/docs")
                    : entry.Url;
                return QuickResult(QuickActions.OpenUrl(url));
            }

            // ── Port Doctor ─────────────────────────────────────────────────
            case "scanPorts":
                return ScanPorts();

            case "killPort":
            {
                var port = RequireInt(args, "port");
                var outcome = _service.KillPort(port);
                return new { success = outcome.Success, reason = outcome.Reason, accessDenied = outcome.AccessDenied };
            }

            case "reclaimPort":
            {
                var ownerAppId = RequireString(args, "ownerAppId");
                var port = RequireInt(args, "port");
                var entry = _service.LoadApps().FirstOrDefault(a => a.Id == ownerAppId)
                    ?? throw new InvalidOperationException($"No app with id '{ownerAppId}'.");
                var result = await _service.ReclaimAsync(entry, port);
                return new
                {
                    kill = new { success = result.Kill.Success, reason = result.Kill.Reason, accessDenied = result.Kill.AccessDenied },
                    launch = result.Launch is { } l
                        ? new { status = l.Status.ToString(), reason = l.Reason }
                        : null,
                };
            }

            case "stopService":
            {
                var serviceName = RequireString(args, "serviceName");
                var port = RequireInt(args, "port");
                var outcome = await _service.StopServiceElevatedAsync(serviceName, port);
                return new { started = outcome.Started, cancelled = outcome.Cancelled, portFreed = outcome.PortFreed, reason = outcome.Reason };
            }

            // ── Add / Edit dialog ───────────────────────────────────────────
            case "pickFolder":
                return PickFolder(OptionalString(args, "current"));

            case "detectStack":
                return DetectStack(RequireString(args, "dir"));

            case "addApp":
                return AddApp(args);

            case "updateApp":
                return UpdateApp(args);

            case "setFavorite":
                return SetFavorite(args);

            case "removeApp":
            {
                var entry = ResolveApp(args);
                _service.Remove(entry);
                return null;
            }

            case "listGroups":
                return ListGroups();

            case "reservedPorts":
                return _service.ReservedPorts(OptionalString(args, "excludeId")).ToArray();

            default:
                throw new InvalidOperationException($"Unknown bridge method '{method}'.");
        }
    }

    private IReadOnlyList<AppView> ListApps() =>
        _service.LoadApps()
            .Select(a => new AppView(
                a.Id, a.Name, a.Url, a.Framework, a.Favorite,
                a.Group, a.Role, a.Port, StatusFor(a), a.Tags, a.Dir))
            .ToList();

    /// <summary>Scan live listeners and project each <see cref="PortReport"/> to the rich row the
    /// Port Doctor view renders (one row per report, ancestry included).</summary>
    private IReadOnlyList<PortRowView> ScanPorts() =>
        _service.ScanPorts()
            .Select(r => new PortRowView(
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
                ExeDir: r.Evidence.ExeDir,
                StartedAt: r.Evidence.StartedAt,
                Ancestry: (r.Evidence.Ancestry ?? Array.Empty<AncestorInfo>())
                    .Select(a => new AncestorView(a.Pid, a.ProcessName, a.Alive, a.CommandLine, a.ExeDir))
                    .ToList()))
            .ToList();

    // ── Add / Edit dialog support ────────────────────────────────────────────

    /// <summary>Open a native folder picker and return the chosen path (or null when cancelled).
    /// A WinForms <see cref="Forms.FolderBrowserDialog"/> must run on an STA UI thread, so this is
    /// marshalled onto the WPF dispatcher (NOT the background <see cref="Task.Run"/> the rest of
    /// dispatch runs on). Returns <c>{ path }</c> (path null on cancel).</summary>
    private object PickFolder(string? current)
    {
        string? chosen = _dispatcher.Invoke(() =>
        {
            using var dialog = new Forms.FolderBrowserDialog();
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                dialog.SelectedPath = current;

            return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
        });

        return new { path = chosen };
    }

    /// <summary>Run the Core <see cref="StackDetector"/> on a folder and return its suggestion
    /// (any field may be null). An invalid/empty dir yields a friendly error so the JS side can
    /// toast it rather than reject hard. <c>dir</c> in the result is the matched run-folder (a
    /// subfolder like <c>frontend/</c> when the project is split), so the dialog can update it.</summary>
    private object DetectStack(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            throw new InvalidOperationException("Choose an existing folder first.");

        var detected = new StackDetector().Detect(dir);
        if (detected is null)
        {
            // No throw — a "nothing detected" result is a normal outcome the dialog handles by
            // leaving fields for manual entry. Null fields tell the UI nothing was inferred.
            return new
            {
                framework = (string?)null,
                cmd = (string?)null,
                url = (string?)null,
                port = (int?)null,
                installCmd = (string?)null,
                dir = (string?)null,
                detected = false,
            };
        }

        return new
        {
            framework = detected.Framework,
            cmd = detected.Cmd,
            url = detected.Url,
            port = UrlPort.FromUrl(detected.Url),
            installCmd = detected.InstallCmd,
            dir = detected.Dir,
            detected = true,
        };
    }

    /// <summary>Build a new <see cref="AppEntry"/> from the dialog payload and persist it. The id
    /// is left blank so <see cref="AppShelf.Core.Config.ConfigStore.AddApp"/> slugs it from the
    /// name. Returns the created entry projected to the grid <see cref="AppView"/> shape.</summary>
    private object AddApp(JsonElement args)
    {
        var entry = BuildEntryFromArgs(args, existingId: null);
        var created = _service.Add(entry);
        return ProjectApp(created);
    }

    /// <summary>Resolve an existing app by id, apply the edited fields, and persist. Id/CreatedAt/
    /// LastLaunchedAt are preserved from the existing entry (they are not user-editable).</summary>
    private object UpdateApp(JsonElement args)
    {
        var id = RequireString(args, "id");
        var existing = _service.LoadApps().FirstOrDefault(a => a.Id == id)
            ?? throw new InvalidOperationException($"No app with id '{id}'.");

        var entry = BuildEntryFromArgs(args, existingId: id);
        entry.CreatedAt = existing.CreatedAt;
        entry.LastLaunchedAt = existing.LastLaunchedAt;
        _service.Update(entry);
        return ProjectApp(entry);
    }

    /// <summary>Toggle/set the favorite flag for an app and persist it via the engine. A thin front
    /// door over <see cref="GuiAppService.Update"/> — resolves the live entry by id, sets
    /// <see cref="AppEntry.Favorite"/>, preserves id/timestamps, and returns the updated grid row so
    /// the JS side can re-sort. Used by the one-click favorite star on cards and group-member rows.</summary>
    private object SetFavorite(JsonElement args)
    {
        var id = RequireString(args, "id");
        var favorite = OptionalBool(args, "favorite");
        var existing = _service.LoadApps().FirstOrDefault(a => a.Id == id)
            ?? throw new InvalidOperationException($"No app with id '{id}'.");

        existing.Favorite = favorite;
        _service.Update(existing);
        return ProjectApp(existing);
    }

    /// <summary>Map a dialog payload to an <see cref="AppEntry"/>, mirroring the WPF dialog's
    /// build/validation rules (name required; URL-only when cmd is blank; readable errors;
    /// auto-allocate a free port for local apps when none was given; keep the URL's port in sync).</summary>
    private AppEntry BuildEntryFromArgs(JsonElement args, string? existingId)
    {
        var name = OptionalString(args, "name")?.Trim() ?? "";
        var cmd = OptionalString(args, "cmd")?.Trim();
        var url = OptionalString(args, "url")?.Trim() ?? "";
        var dir = OptionalString(args, "dir")?.Trim();
        var installCmd = OptionalString(args, "installCmd")?.Trim();
        var framework = OptionalString(args, "framework")?.Trim();
        var group = OptionalString(args, "group")?.Trim();
        var role = NormalizeRole(OptionalString(args, "role"));
        var open = OptionalString(args, "open") == OpenTargets.WebView ? OpenTargets.WebView : OpenTargets.Browser;
        var favorite = OptionalBool(args, "favorite");
        var tags = ParseTags(args);

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Name is required.");

        var urlOnly = string.IsNullOrWhiteSpace(cmd);

        if (urlOnly)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("A URL-only app needs a URL.");

            return new AppEntry
            {
                Id = existingId ?? "",
                Name = name,
                Url = url,
                Cmd = null,
                Dir = null,
                InstallCmd = null,
                Framework = string.IsNullOrWhiteSpace(framework) ? null : framework,
                Open = open,
                Tags = tags,
                Favorite = favorite,
                Group = string.IsNullOrWhiteSpace(group) ? null : group,
                Role = role,
            };
        }

        // Local app: dir + cmd + url required (matches the WPF dialog).
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException("Folder is required for a local app.");
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("URL is required.");

        var (port, portFixed) = ResolvePort(args, url, existingId);
        var syncedUrl = UrlPort.WithPort(url, port);

        return new AppEntry
        {
            Id = existingId ?? "",
            Name = name,
            Dir = Path.GetFullPath(dir),
            Cmd = cmd,
            Url = syncedUrl,
            InstallCmd = string.IsNullOrWhiteSpace(installCmd) ? null : installCmd,
            Framework = string.IsNullOrWhiteSpace(framework) ? null : framework,
            Port = port,
            PortFixed = portFixed,
            Open = open,
            Tags = tags,
            Favorite = favorite,
            Group = string.IsNullOrWhiteSpace(group) ? null : group,
            Role = role,
        };
    }

    /// <summary>Resolve the reserved port for a local app: an explicit port is validated and
    /// checked for a clash with another app; a blank port auto-allocates a free one (preferring
    /// the URL's own port). Mirrors the WPF dialog's ResolvePort.</summary>
    private (int Port, bool Fixed) ResolvePort(JsonElement args, string url, string? existingId)
    {
        var portText = OptionalString(args, "port")?.Trim();
        if (!string.IsNullOrWhiteSpace(portText))
        {
            if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("Port must be a number between 1 and 65535.");

            var clash = _service.LoadApps().FirstOrDefault(a => a.Port == port && a.Id != existingId);
            if (clash is not null)
                throw new InvalidOperationException($"Port {port} is already reserved by '{clash.Name}'.");

            return (port, OptionalBool(args, "portFixed"));
        }

        var allocated = new PortAllocator().Allocate(_service.ReservedPorts(existingId), UrlPort.FromUrl(url));
        return (allocated, false);
    }

    /// <summary>Distinct existing non-empty group names (case-insensitive, sorted) for the dialog's
    /// Group datalist.</summary>
    private IReadOnlyList<string> ListGroups() =>
        _service.LoadApps()
            .Select(a => a.Group)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Project a single entry to the grid <see cref="AppView"/> shape (same fields the
    /// list returns) so the JS side can splice the created/updated row in if it wants.</summary>
    private AppView ProjectApp(AppEntry a) =>
        new(a.Id, a.Name, a.Url, a.Framework, a.Favorite,
            a.Group, a.Role, a.Port, StatusFor(a), a.Tags, a.Dir);

    /// <summary>Normalize a role string to one of the allowed <see cref="AppRoles"/> values,
    /// defaulting to "other".</summary>
    private static string NormalizeRole(string? role) => role switch
    {
        AppRoles.Backend => AppRoles.Backend,
        AppRoles.Frontend => AppRoles.Frontend,
        _ => AppRoles.Other,
    };

    /// <summary>Parse the <c>tags</c> arg: accepts a JSON array of strings or a comma-separated
    /// string. Trims, drops blanks. Returns an empty list when absent.</summary>
    private static List<string> ParseTags(JsonElement args)
    {
        if (!args.TryGetProperty("tags", out var prop))
            return new List<string>();

        if (prop.ValueKind == JsonValueKind.Array)
            return prop.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim())
                .Where(s => s.Length > 0)
                .ToList();

        if (prop.ValueKind == JsonValueKind.String)
            return (prop.GetString() ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        return new List<string>();
    }

    /// <summary>Read an optional string arg (null when absent or not a string).</summary>
    private static string? OptionalString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    /// <summary>Read an optional bool arg (false when absent or not a bool).</summary>
    private static bool OptionalBool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var prop) &&
        (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False) &&
        prop.GetBoolean();

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

    /// <summary>Project a <see cref="QuickActions.ActionResult"/> to the JS <c>{ ok, reason }</c>
    /// shape (camelCased by the serializer). Quick actions never throw — a failure is a friendly
    /// reason the UI toasts, not a rejected promise.</summary>
    private static object QuickResult(QuickActions.ActionResult result) =>
        new { ok = result.Success, reason = result.Reason };

    /// <summary>True when the app's detected framework is FastAPI (so /docs is meaningful).</summary>
    private static bool IsFastApi(string? framework) =>
        framework is not null &&
        framework.Contains("FastAPI", StringComparison.OrdinalIgnoreCase);

    /// <summary>Join a base URL with a path segment, collapsing a double slash at the seam.</summary>
    private static string CombineUrl(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

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

    /// <summary>Read a required int arg (tolerates a JSON number or a numeric string).</summary>
    private static int RequireInt(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop))
            throw new ArgumentException($"Missing '{name}' argument.");

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n))
            return n;
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var s))
            return s;

        throw new ArgumentException($"Argument '{name}' must be an integer.");
    }

    /// <summary>Read a required non-empty string arg.</summary>
    private static string RequireString(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"Missing '{name}' argument.");

        var value = prop.GetString();
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException($"Argument '{name}' must be a non-empty string.");
        return value;
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
