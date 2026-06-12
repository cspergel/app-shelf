using AppShelf.Core.Models;

namespace AppShelf.Core.Launch;

/// <summary>A plain-English reason a launch would fail before we spawn, plus whether the app's
/// configured install command could plausibly fix it (so the GUI can offer one-click "Run setup").</summary>
public sealed record PreflightIssue(string Reason, bool CanRunSetup);

/// <summary>
/// Pure pre-flight check (spec: catch fixable launch failures with a plain-English reason instead
/// of spawning a doomed process or dumping a cryptic log). Returns the FIRST blocking issue, or
/// null when the launch looks viable. No I/O beyond cheap existence checks; never throws.
/// </summary>
public static class LaunchPreflight
{
    /// <summary>Returns the first blocking issue, or null when the app looks launchable.</summary>
    public static PreflightIssue? Check(AppEntry entry)
    {
        // URL-only apps have nothing to launch locally — never a pre-flight problem.
        if (entry.IsUrlOnly)
            return null;

        // Working folder must exist (project moved/deleted is not setup-fixable).
        if (string.IsNullOrWhiteSpace(entry.Dir) || !Directory.Exists(entry.Dir))
            return new($"Working folder not found: {entry.Dir}. Was the project moved or deleted?", CanRunSetup: false);

        var dir = entry.Dir;
        var hasInstall = !string.IsNullOrWhiteSpace(entry.InstallCmd);
        var token = FirstToken(entry.Cmd);

        if (token.Length > 0 && LooksLikePath(token))
        {
            // An explicit interpreter/binary path (e.g. venv\Scripts\python.exe) — must exist on disk.
            var resolved = Path.IsPathRooted(token) ? token : Path.Combine(dir, token);
            if (!File.Exists(resolved))
                return new($"Not found: {token}. The project's dependencies may not be installed yet.", CanRunSetup: hasInstall);
        }
        else if (IsNodeCommand(token) && !Directory.Exists(Path.Combine(dir, "node_modules")))
        {
            // A Node command with no installed packages can't run until deps are pulled.
            return new("Dependencies aren't installed yet (no node_modules).", CanRunSetup: hasInstall);
        }

        return null;
    }

    /// <summary>First whitespace-delimited token of a command, or "" when blank.</summary>
    private static string FirstToken(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd))
            return "";
        var parts = cmd.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts[0];
    }

    /// <summary>Heuristic: the token names a file/path (has a separator) or an explicit .exe.</summary>
    private static bool LooksLikePath(string token) =>
        token.Contains('\\') || token.Contains('/') || token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] NodeCommands = { "npm", "npx", "node", "yarn", "pnpm" };

    /// <summary>True when the command's first token is a Node-ecosystem launcher.</summary>
    private static bool IsNodeCommand(string token) =>
        NodeCommands.Any(c => string.Equals(c, token, StringComparison.OrdinalIgnoreCase));
}
