using System.Diagnostics;
using AppShelf.Core.Config;
using AppShelf.Core.Models;
using AppShelf.Core.Process;

namespace AppShelf.Core.Launch;

public sealed record LaunchResult(LaunchStatus Status, ProcessManager.RunningApp? Running, IReadOnlyList<string> LogTail)
{
    public static LaunchResult Ok(LaunchStatus status, ProcessManager.RunningApp? running = null) =>
        new(status, running, running?.LogTail ?? Array.Empty<string>());
}

/// <summary>
/// High-level launch orchestration (spec §3.4):
/// URL-only → open immediately; already-listening → open without spawning (the warm path);
/// otherwise spawn, poll the port, then open on success or report Error on timeout/exit.
/// </summary>
public sealed class Launcher
{
    private readonly ProcessManager _processes;
    private readonly ITargetOpener _opener;
    private readonly ConfigStore? _store;

    public int PollIntervalMs { get; init; } = 250;
    public int PollTimeoutMs { get; init; } = 30_000;

    public Launcher(ProcessManager processes, ITargetOpener opener, ConfigStore? store = null)
    {
        _processes = processes;
        _opener = opener;
        _store = store;
    }

    public async Task<LaunchResult> LaunchAsync(AppEntry entry, CancellationToken cancellationToken = default)
    {
        // 1. URL-only app → just open it.
        if (entry.IsUrlOnly)
        {
            _opener.Open(entry);
            StampLaunched(entry);
            return LaunchResult.Ok(LaunchStatus.Running);
        }

        // 2. Already listening → it's up; skip spawn and open (warm reopen / no double-spawn).
        if (ProcessManager.IsPortListening(entry.Url))
        {
            _opener.Open(entry);
            StampLaunched(entry);
            return LaunchResult.Ok(LaunchStatus.Running);
        }

        // 3. Spawn it.
        var running = _processes.Start(entry);

        // 4. Poll for the port to come up.
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < PollTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (running.HasExited)
            {
                _processes.Stop(entry.Id);
                return new LaunchResult(LaunchStatus.Error, null, running.LogTail);
            }

            if (ProcessManager.IsPortListening(entry.Url))
            {
                _opener.Open(entry);
                StampLaunched(entry);
                return LaunchResult.Ok(LaunchStatus.Running, running);
            }

            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        // 5. Timed out — leave the log tail available for the user.
        return new LaunchResult(LaunchStatus.Error, running, running.LogTail);
    }

    private void StampLaunched(AppEntry entry)
    {
        entry.LastLaunchedAt = DateTimeOffset.UtcNow;
        if (_store is null)
            return;

        try { _store.UpdateApp(entry); }
        catch (InvalidOperationException) { /* not persisted yet (e.g. transient entry) — ignore */ }
    }
}
