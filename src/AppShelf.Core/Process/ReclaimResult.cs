using AppShelf.Core.Launch;

namespace AppShelf.Core.Process;

/// <summary>Outcome of a Port Doctor "Reclaim" (kill the orphan, then relaunch the owning app).
/// <paramref name="Launch"/> is null when the kill failed and the relaunch was skipped.</summary>
public sealed record ReclaimResult(KillOutcome Kill, LaunchResult? Launch);
