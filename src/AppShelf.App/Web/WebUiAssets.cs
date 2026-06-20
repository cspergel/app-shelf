using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace AppShelf.App.Web;

/// <summary>
/// In RELEASE builds the web UI is shipped INSIDE the single-file exe as an embedded
/// <c>webui.zip</c> resource (the contents of <c>src/AppShelf.Web/dist/</c>). Since a single-file
/// exe cannot serve loose files from inside itself, the zip is extracted once at startup to a
/// writable, version-stamped folder under <c>%LOCALAPPDATA%/AppShelf/webui/&lt;version&gt;/</c>.
/// WebView2 then maps a virtual host to that folder so the page loads with no Vite dev server.
/// </summary>
internal static class WebUiAssets
{
    /// <summary>The embedded resource name (logical name set in the .csproj ItemGroup).</summary>
    private const string ResourceName = "AppShelf.App.webui.zip";

    /// <summary>The virtual host the extracted web UI is served from in RELEASE.</summary>
    public const string VirtualHost = "appshelf.local";

    /// <summary>A sentinel file written after a clean extraction so a partial/aborted extraction
    /// (e.g. the app was killed mid-unzip) is re-done instead of being trusted.</summary>
    private const string CompleteMarker = ".extracted";

    /// <summary>
    /// Ensures the embedded web UI is extracted to a version-stamped folder and returns that
    /// folder's absolute path. Extraction is skipped when the folder already exists and carries
    /// the completion marker. Throws if the embedded resource is missing or the unzip fails — the
    /// caller surfaces that to the user.
    /// </summary>
    public static string EnsureExtracted()
    {
        var version = (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0)).ToString();
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AppShelf", "webui", version);
        var marker = Path.Combine(root, CompleteMarker);

        // Fast path: this exact version was already extracted cleanly AND the exe
        // has not been rebuilt since (guards against a rebuild with the same version number
        // producing a stale extraction — the exe write-time is always >= the embedded zip).
        if (File.Exists(marker) && File.Exists(Path.Combine(root, "index.html")))
        {
            var exePath = Environment.ProcessPath;
            if (exePath is not null)
            {
                var exeWritten = File.GetLastWriteTimeUtc(exePath);
                if (DateTime.TryParse(File.ReadAllText(marker), out var markerTime)
                    && markerTime >= exeWritten)
                    return root;
                // Exe is newer than marker → embedded zip was updated; re-extract.
            }
            else
            {
                // Can't determine exe path (single-file self-extract edge case); trust marker.
                return root;
            }
        }

        // Re-extract from scratch (covers an aborted previous run). Remove any partial folder first.
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
        {
            if (stream is null)
                throw new InvalidOperationException(
                    $"Embedded web UI resource '{ResourceName}' was not found in the assembly. " +
                    "The Release build did not bundle the web assets.");

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(root, overwriteFiles: true);
        }

        if (!File.Exists(Path.Combine(root, "index.html")))
            throw new InvalidOperationException(
                "The embedded web UI was extracted but no index.html was found.");

        File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
        return root;
    }
}
