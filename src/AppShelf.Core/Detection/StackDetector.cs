using System.Text.Json;
using System.Text.RegularExpressions;

namespace AppShelf.Core.Detection;

/// <summary>
/// Best-effort inference of how to launch a project from its folder (spec §3.2). Node takes
/// priority when a package.json is present; otherwise Python signals are checked. When the
/// top level yields nothing, common subfolders (frontend/, backend/, etc.) are scanned one
/// level down — many projects split into a browser-facing app and an API. Returns null when
/// nothing matches — the caller then prompts for a manual cmd/url. Detection is never
/// authoritative; the user always confirms/edits.
/// </summary>
public sealed class StackDetector
{
    // Browser-facing dirs first so a full-stack repo detects the frontend (the thing you open).
    private static readonly string[] CommonSubdirs =
        { "frontend", "web", "client", "ui", "app", "src", "backend", "api", "server" };

    private static readonly string[] ViteConfigNames =
        { "vite.config.ts", "vite.config.js", "vite.config.mjs", "vite.config.cjs" };

    private static readonly Regex PortRegex =
        new(@"port\s*[:=]\s*(\d{2,5})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DetectionResult? Detect(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return null;

        var top = DetectIn(dir);
        if (top is not null)
            return top;

        foreach (var sub in CommonSubdirs)
        {
            var subPath = Path.Combine(dir, sub);
            if (!Directory.Exists(subPath))
                continue;
            var found = DetectIn(subPath);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static DetectionResult? DetectIn(string dir)
    {
        var packageJson = Path.Combine(dir, "package.json");
        if (File.Exists(packageJson))
        {
            var node = DetectNode(dir, packageJson);
            if (node is not null)
                return node;
        }

        return DetectPython(dir);
    }

    private static DetectionResult? DetectNode(string dir, string packageJsonPath)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        }
        catch (JsonException)
        {
            return null; // malformed package.json — best-effort: bail, caller prompts
        }

        using (doc)
        {
            var root = doc.RootElement;

            var script = PickScript(root);
            if (script is null)
                return null; // no launchable dev script

            var deps = CollectDependencyNames(root);
            var rule = FrameworkRules.Node.FirstOrDefault(r => deps.Contains(r.Dependency));
            if (rule is null)
                return null; // unknown framework — caller prompts

            var port = ResolveVitePort(dir) ?? rule.DefaultPort;

            return new DetectionResult(
                Cmd: $"npm run {script}",
                Url: $"http://localhost:{port}",
                InstallCmd: "npm install",
                Framework: rule.Framework,
                Dir: dir);
        }
    }

    private static string? PickScript(JsonElement root)
    {
        if (!root.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in FrameworkRules.NodeScriptPriority)
            if (scripts.TryGetProperty(name, out _))
                return name;

        return null;
    }

    private static HashSet<string> CollectDependencyNames(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (root.TryGetProperty(section, out var obj) && obj.ValueKind == JsonValueKind.Object)
                foreach (var prop in obj.EnumerateObject())
                    names.Add(prop.Name);
        }
        return names;
    }

    private static int? ResolveVitePort(string dir)
    {
        foreach (var name in ViteConfigNames)
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
                continue;

            try
            {
                var match = PortRegex.Match(File.ReadAllText(path));
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                    return port;
            }
            catch (IOException) { /* ignore unreadable config */ }
        }
        return null;
    }

    private static DetectionResult? DetectPython(string dir)
    {
        string[] pyFiles;
        try
        {
            pyFiles = Directory.GetFiles(dir, "*.py", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return null;
        }

        if (pyFiles.Length == 0)
            return null;

        Array.Sort(pyFiles, StringComparer.OrdinalIgnoreCase);

        var contents = new List<(string Name, string Text)>();
        foreach (var file in pyFiles)
        {
            try { contents.Add((Path.GetFileName(file), File.ReadAllText(file))); }
            catch (IOException) { /* skip unreadable */ }
        }

        foreach (var rule in FrameworkRules.Python)
        {
            var hit = contents.FirstOrDefault(c => c.Text.Contains(rule.Signal, StringComparison.Ordinal));
            if (hit.Name is null)
                continue;

            var workingDir = dir;
            var module = Path.GetFileNameWithoutExtension(hit.Name);

            // Module-path tools (uvicorn) import the app as a package. If the entry file sits inside
            // a Python package (its folder — or an ancestor — has __init__.py), run from the package
            // root's PARENT and use the dotted module path (e.g. `app.main:app` from backend\, not
            // `main:app` from backend\app\, which can't resolve `from app.routers...` imports).
            // File-path tools (streamlit/flask run the file directly) stay in the file's own folder.
            if (rule.CmdTemplate.Contains("{module}"))
            {
                var (root, prefix) = ResolvePackageRoot(dir);
                workingDir = root;
                if (prefix.Length > 0)
                    module = $"{prefix}.{module}";
            }

            // {file} → the entry filename (e.g. main.py); {module} → its dotted-import name.
            var cmd = rule.CmdTemplate
                .Replace("{file}", hit.Name)
                .Replace("{module}", module);

            // Offer a sensible install command so the GUI's one-click "Run setup" can fix a fresh
            // checkout whose dependencies aren't installed yet.
            var installCmd = File.Exists(Path.Combine(workingDir, "requirements.txt"))
                ? "pip install -r requirements.txt"
                : null;

            return new DetectionResult(
                Cmd: cmd,
                Url: $"http://localhost:{rule.DefaultPort}",
                InstallCmd: installCmd,
                Framework: rule.Framework,
                Dir: workingDir);
        }

        return null;
    }

    /// <summary>
    /// If <paramref name="dir"/> is inside a Python package — it, or an unbroken chain of ancestors,
    /// contains <c>__init__.py</c> — returns the package root's PARENT directory plus the dotted
    /// package prefix (e.g. <c>…\backend\app</c> with <c>app\__init__.py</c> → (<c>…\backend</c>,
    /// "app")). Otherwise returns (<paramref name="dir"/>, ""). Used to build a correct dotted
    /// module path for uvicorn-style launchers.
    /// </summary>
    private static (string Root, string Prefix) ResolvePackageRoot(string dir)
    {
        var parts = new List<string>();
        var current = dir;
        while (File.Exists(Path.Combine(current, "__init__.py")))
        {
            parts.Insert(0, Path.GetFileName(current));
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
                break;
            current = parent;
        }
        return (current, string.Join(".", parts));
    }
}
