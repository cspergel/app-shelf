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
    /// Windows Terminal (<c>wt.exe -d dir</c>) → PowerShell (<c>-NoExit -Command Set-Location</c>)
    /// → <c>cmd.exe /K cd /d dir</c>. The first that launches wins.
    ///
    /// (A future settings UI could let the user pick a preferred shell instead of this order.)
    /// </summary>
    public static ActionResult OpenTerminal(string? dir)
    {
        if (!ValidateDir(dir, out var reason))
            return ActionResult.Fail(reason);

        // Each candidate is (fileName, args). UseShellExecute=false so a missing exe throws
        // (Win32Exception) and we fall through to the next candidate.
        var candidates = new (string File, string Args)[]
        {
            ("wt.exe", $"-d \"{dir}\""),
            ("powershell.exe", $"-NoExit -Command \"Set-Location -LiteralPath '{dir}'\""),
            ("cmd.exe", $"/K cd /d \"{dir}\""),
        };

        foreach (var (file, args) in candidates)
        {
            if (TryStart(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                WorkingDirectory = dir,
            }))
            {
                return ActionResult.Ok();
            }
        }

        return ActionResult.Fail("Couldn't open a terminal (no Windows Terminal, PowerShell, or cmd found).");
    }

    /// <summary>
    /// Open the configured editor on <paramref name="dir"/>. Defaults to VS Code (<c>code</c>).
    /// <paramref name="editorCmd"/> overrides the executable name when supplied (still launched
    /// the same way). <c>code</c> is a <c>.cmd</c> shim, so we go through <c>cmd /c</c> for PATH
    /// resolution.
    ///
    /// (A future settings UI could persist a preferred editor; for now <c>code</c> is the default.)
    /// </summary>
    public static ActionResult OpenEditor(string? dir, string? editorCmd)
    {
        if (!ValidateDir(dir, out var reason))
            return ActionResult.Fail(reason);

        var editor = string.IsNullOrWhiteSpace(editorCmd) ? "code" : editorCmd.Trim();

        // cmd /c lets PATH-resolved shims (code.cmd) and bare exe names both work. The trailing
        // exit keeps no console window lingering.
        if (TryStart(new ProcessStartInfo("cmd.exe", $"/c {editor} \"{dir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        }))
        {
            return ActionResult.Ok();
        }

        return ActionResult.Fail($"Couldn't launch editor '{editor}'. Is it installed and on PATH?");
    }

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
