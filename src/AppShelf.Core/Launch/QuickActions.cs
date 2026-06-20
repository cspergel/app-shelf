using System.Diagnostics;

namespace AppShelf.Core.Launch;

/// <summary>
/// Thin OS-launch primitives for the per-app "quick dev actions" (open a terminal / editor /
/// folder / file / URL at a project). Deliberately tiny <see cref="Process.Start"/> wrappers so
/// the CLI and the GUI bridge can share one implementation.
///
/// Defensive by contract: every method validates its input where it can, swallows launch
/// failures, and returns an <see cref="ActionResult"/> (never throws to the caller). The shells
/// and the editor are sensible hard-coded defaults — a settings UI for preferred shell/editor is
/// a future add (see <see cref="OpenTerminal"/> / <see cref="OpenEditor"/>).
/// </summary>
public static class QuickActions
{
    /// <summary>Outcome of a quick action: did it launch, and if not, a friendly reason.</summary>
    public readonly record struct ActionResult(bool Success, string? Reason)
    {
        public static ActionResult Ok() => new(true, null);
        public static ActionResult Fail(string reason) => new(false, reason);
    }

    /// <summary>
    /// Open a terminal already cd'd into <paramref name="dir"/>. Tries, in order:
    /// Windows Terminal (<c>wt.exe -d dir</c>) → PowerShell → <c>cmd.exe</c>. The first that
    /// launches wins.
    ///
    /// Injection-safe: <paramref name="dir"/> is never interpolated into a shell command string.
    /// For <c>wt.exe</c> the directory is passed as a discrete <see cref="ProcessStartInfo.ArgumentList"/>
    /// token (quoted by the CLR's Win32 argument rules); for PowerShell and cmd it is supplied only as
    /// the process <see cref="ProcessStartInfo.WorkingDirectory"/> with no shell command — so a
    /// crafted directory value can never escape a quote and inject commands.
    ///
    /// (A future settings UI could let the user pick a preferred shell instead of this order.)
    /// </summary>
    public static ActionResult OpenTerminal(string? dir)
    {
        if (!ValidateDir(dir, out var reason))
            return ActionResult.Fail(reason);

        foreach (var psi in BuildTerminalCandidates(dir!))
        {
            if (TryStart(psi))
                return ActionResult.Ok();
        }

        return ActionResult.Fail("Couldn't open a terminal (no Windows Terminal, PowerShell, or cmd found).");
    }

    /// <summary>
    /// Build the ordered list of terminal-launch candidates for <paramref name="dir"/>
    /// (Windows Terminal → PowerShell → cmd). Extracted as an injection-safe, side-effect-free
    /// helper so the candidate construction can be unit-tested without spawning real processes:
    /// no candidate interpolates <paramref name="dir"/> into a shell command string. <c>wt.exe</c>
    /// receives the directory via <see cref="ProcessStartInfo.ArgumentList"/> (<c>-d</c> then the
    /// raw value); PowerShell and cmd receive it ONLY as <see cref="ProcessStartInfo.WorkingDirectory"/>
    /// with empty arguments (they open interactively in their working directory).
    /// </summary>
    internal static IReadOnlyList<ProcessStartInfo> BuildTerminalCandidates(string dir)
    {
        // wt.exe: pass -d and the directory as discrete ArgumentList entries so the runtime applies
        // correct Win32 quoting — the value is never spliced into a command string.
        var wt = new ProcessStartInfo("wt.exe")
        {
            UseShellExecute = false,
            WorkingDirectory = dir,
        };
        wt.ArgumentList.Add("-d");
        wt.ArgumentList.Add(dir);

        // powershell.exe / cmd.exe: just open them in the directory — no Set-Location / cd command,
        // hence no injection surface. Empty Arguments => interactive shell in WorkingDirectory.
        var powershell = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            WorkingDirectory = dir,
        };

        var cmd = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            WorkingDirectory = dir,
        };

        return new[] { wt, powershell, cmd };
    }

    /// <summary>
    /// Open the configured editor on <paramref name="dir"/>. Defaults to VS Code (<c>code</c>).
    /// <paramref name="editorCmd"/> overrides the executable name when supplied. <c>code</c> is a
    /// <c>.cmd</c> shim, so we go through <c>cmd /c</c> for PATH resolution.
    ///
    /// Injection-safe: <paramref name="dir"/> is never interpolated into the command — the editor
    /// is invoked with <c>"."</c> (the current directory) and <see cref="ProcessStartInfo.WorkingDirectory"/>
    /// set to <paramref name="dir"/>. The editor NAME is validated (no shell metacharacters) before
    /// it is placed into the <c>cmd /c</c> argument string.
    ///
    /// (A future settings UI could persist a preferred editor; for now <c>code</c> is the default.)
    /// </summary>
    public static ActionResult OpenEditor(string? dir, string? editorCmd)
    {
        if (!ValidateDir(dir, out var reason))
            return ActionResult.Fail(reason);

        var editor = string.IsNullOrWhiteSpace(editorCmd) ? "code" : editorCmd.Trim();

        if (!IsSafeEditorName(editor))
            return ActionResult.Fail($"Refusing to launch editor '{editor}': the editor name contains characters that aren't allowed.");

        if (TryStart(BuildEditorStartInfo(dir!, editor)))
            return ActionResult.Ok();

        return ActionResult.Fail($"Couldn't launch editor '{editor}'. Is it installed and on PATH?");
    }

    /// <summary>
    /// Build the editor-launch <see cref="ProcessStartInfo"/> for <paramref name="dir"/>. Extracted
    /// as an injection-safe, side-effect-free helper for unit testing. The directory is supplied as
    /// <see cref="ProcessStartInfo.WorkingDirectory"/> and the editor is told to open <c>"."</c> —
    /// the directory value is never interpolated into the <c>cmd /c</c> argument string. The caller
    /// must have already validated <paramref name="editor"/> via <see cref="IsSafeEditorName"/>.
    /// </summary>
    internal static ProcessStartInfo BuildEditorStartInfo(string dir, string editor) =>
        new("cmd.exe", $"/c {editor} .")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };

    /// <summary>Reject editor names carrying shell metacharacters that could break out of the
    /// <c>cmd /c {editor} .</c> argument string (quotes, command separators, backtick, redirection).
    /// The editor name comes from config (not a web attacker), but this guards a shared/imported
    /// config (OSS) from smuggling a command through the editor field.</summary>
    internal static bool IsSafeEditorName(string editor) =>
        !string.IsNullOrWhiteSpace(editor) &&
        editor.IndexOfAny(new[] { '"', '\'', '`', '&', '|', ';', '<', '>', '^', '%', '\n', '\r' }) < 0;

    /// <summary>Open <paramref name="dir"/> in Explorer.</summary>
    public static ActionResult OpenFolder(string? dir)
    {
        if (!ValidateDir(dir, out var reason))
            return ActionResult.Fail(reason);

        if (TryStart(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }))
            return ActionResult.Ok();

        return ActionResult.Fail("Couldn't open the folder in Explorer.");
    }

    /// <summary>Open a file with its default app (ShellExecute). Used for <c>.env</c>.</summary>
    public static ActionResult OpenFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ActionResult.Fail("No file path.");
        if (!File.Exists(path))
            return ActionResult.Fail($"File not found: {path}");

        if (TryStart(new ProcessStartInfo(path) { UseShellExecute = true }))
            return ActionResult.Ok();

        return ActionResult.Fail($"Couldn't open '{path}' with its default app.");
    }

    /// <summary>
    /// Open an http/https URL in the default browser. Mirrors <see cref="TargetOpener"/>'s
    /// scheme guard so a non-web URI can never be handed to an arbitrary protocol handler.
    /// </summary>
    public static ActionResult OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ActionResult.Fail("No URL.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ActionResult.Fail($"Refusing to open '{url}': only http:// and https:// are supported.");
        }

        if (TryStart(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }))
            return ActionResult.Ok();

        return ActionResult.Fail($"Couldn't open '{url}' in the browser.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool ValidateDir(string? dir, out string reason)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            reason = "This app has no folder (it's URL-only).";
            return false;
        }
        if (!Directory.Exists(dir))
        {
            reason = $"Folder not found: {dir}";
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>Try to start a process; true on success, false (swallowed) on any launch failure.</summary>
    private static bool TryStart(ProcessStartInfo psi)
    {
        try
        {
            return System.Diagnostics.Process.Start(psi) is not null;
        }
        catch
        {
            return false;
        }
    }
}
