namespace AppShelf.Core.Process;

/// <summary>Result of a kill attempt: whether the port/tree was freed, and why not if it failed.
/// <paramref name="AccessDenied"/> is set when the OS refused the kill (taskkill "Access is denied"
/// / Win32 error 5) — typically a Windows service or an elevated/protected process. The raw
/// <paramref name="Reason"/> is kept for diagnostics, but callers should prefer building a friendly
/// message from <paramref name="AccessDenied"/> rather than surfacing the raw text.</summary>
public sealed record KillOutcome(bool Success, string? Reason, bool AccessDenied = false)
{
    public static readonly KillOutcome Ok = new(true, null);
    public static KillOutcome Fail(string reason, bool accessDenied = false) => new(false, reason, accessDenied);
}
