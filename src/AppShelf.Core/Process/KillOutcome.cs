namespace AppShelf.Core.Process;

/// <summary>Result of a kill attempt: whether the port/tree was freed, and why not if it failed.</summary>
public sealed record KillOutcome(bool Success, string? Reason)
{
    public static readonly KillOutcome Ok = new(true, null);
    public static KillOutcome Fail(string reason) => new(false, reason);
}
