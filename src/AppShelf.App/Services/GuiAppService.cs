using AppShelf.Core.Config;
using AppShelf.Core.Launch;
using AppShelf.Core.Models;
using AppShelf.Core.Process;

namespace AppShelf.App.Services;

/// <summary>
/// The GUI's single door into Core (spec: one source of truth). Owns the long-lived
/// <see cref="ProcessManager"/>, so launched dev servers are job-owned and the warm/hard
/// lifecycle works: hiding the window leaves them running; Stop or app-exit kills them and
/// frees their ports (KILL_ON_JOB_CLOSE).
/// </summary>
public sealed class GuiAppService : IDisposable
{
    private readonly ConfigStore _store = new();
    private readonly ProcessManager _processes = new();
    private readonly TargetOpener _opener = new();
    private readonly Launcher _launcher;

    public GuiAppService()
    {
        _launcher = new Launcher(_processes, _opener, _store);
    }

    public IReadOnlyList<AppEntry> LoadApps() => _store.Load().Apps;

    public Task<LaunchResult> LaunchAsync(AppEntry entry) => _launcher.LaunchAsync(entry);

    /// <summary>Run the app's install command to completion (one-click "Run setup" after a
    /// fixable pre-flight failure), capturing its output for the log panel.</summary>
    public Task<ProcessManager.SetupOutcome> RunSetupAsync(AppEntry entry) =>
        ProcessManager.RunSetupAsync(entry.Dir ?? "", entry.InstallCmd ?? "");

    /// <summary>Open the target without launching (warm reopen / already-running).</summary>
    public void Open(AppEntry entry) => _opener.Open(entry);

    /// <summary>Hard stop: kill the process tree and free the port.</summary>
    public bool Stop(AppEntry entry) => _processes.Stop(entry.Id);

    public async Task<LaunchResult> RestartAsync(AppEntry entry)
    {
        _processes.Stop(entry.Id);
        // Give the OS a moment to release the port before relaunching.
        await Task.Delay(300);
        return await _launcher.LaunchAsync(entry);
    }

    /// <summary>Kill the orphan on <paramref name="port"/> and relaunch <paramref name="entry"/>.
    /// Polls for the port to free (up to ~3 s) before relaunching so the warm-path in
    /// <see cref="Launcher.LaunchAsync"/> does not reopen a zombie browser. When the kill fails,
    /// returns immediately without relaunching.</summary>
    public async Task<ReclaimResult> ReclaimAsync(AppEntry entry, int port)
    {
        var kill = KillPort(port);
        if (!kill.Success)
            return new ReclaimResult(kill, null);

        // Clear any managed tracking for the owner (same door RestartAsync uses) so the relaunch
        // can't hit ProcessManager.Start's "already running in this session" guard from a stale or
        // live entry left over earlier this session. Stop is a safe no-op when the id isn't tracked
        // (ProcessManager.Stop -> _running.TryRemove returns false).
        _processes.Stop(entry.Id);

        // Poll until the port is free (max ~3 s / 20 × 150 ms).
        for (var i = 0; i < 20 && ProcessManager.IsPortInUse(port); i++)
            await Task.Delay(150);

        // The kill already succeeded — never let a relaunch exception bubble out as an unhandled
        // async-void crash in the VM. Map any failure to a readable Error result the VM messages on.
        LaunchResult launch;
        try
        {
            launch = await _launcher.LaunchAsync(entry);
        }
        catch (Exception ex)
        {
            launch = new LaunchResult(LaunchStatus.Error, null, Array.Empty<string>(), ex.Message);
        }
        return new ReclaimResult(kill, launch);
    }

    /// <summary>Live status for the grid: owner-aware. If the port is not listening, Starting when we
    /// are managing it (not up yet) else Stopped. If it is listening, Running only when the listener
    /// is one of our managed processes (or the app is URL-only / has no port to check); a registered
    /// port held by a foreign process is reported as PortInUse so the pre-flight conflict gate becomes
    /// reachable from the card.
    ///
    /// <paramref name="probeTimeoutMs"/> controls the per-address TCP connect timeout forwarded to
    /// <see cref="ProcessManager.IsPortListening"/>. The default (500 ms) is appropriate for WPF
    /// card-grid refreshes and launch pre-flight. Pass a shorter value (e.g. 150 ms) for the web
    /// bridge's high-frequency status poll — localhost responds in &lt;10 ms when live, and the
    /// concurrent dual-family probe collapses to ≤ probeTimeoutMs when stopped.</summary>
    public LaunchStatus StatusOf(AppEntry entry, int probeTimeoutMs = 500)
    {
        bool listening = ProcessManager.IsPortListening(entry.Url, probeTimeoutMs);
        if (!listening)
            return _processes.IsManaged(entry.Id) ? LaunchStatus.Starting : LaunchStatus.Stopped;

        // Listening: CheckPortConflict returns null when the listener is ours, or when there is no
        // port to check (URL-only / port-less apps) — both keep their current Running behavior.
        return CheckPortConflict(entry) is null ? LaunchStatus.Running : LaunchStatus.PortInUse;
    }

    public IReadOnlyList<string> LogTail(AppEntry entry) => _processes.GetLogTail(entry.Id);

    /// <summary>True when the entry was launched this session and its process has since exited
    /// without going through the AppShelf Stop path. Used by the per-card crash watcher.</summary>
    public bool DidManagedAppExit(AppEntry entry) =>
        _processes.IsManagedAndExited(entry.Id);

    public AppEntry Add(AppEntry entry) => _store.AddApp(entry);
    public void Update(AppEntry entry) => _store.UpdateApp(entry);

    /// <summary>The configured Spotlight hotkey string, or null/empty when none is set (use the
    /// built-in default chain).</summary>
    public string? GetHotkey() => _store.Load().Hotkey;

    /// <summary>Persist the Spotlight hotkey string. Pass null/empty to clear it (revert to the
    /// default chain). Writes through the same atomic ConfigStore save path as everything else.</summary>
    public void SetHotkey(string? combo)
    {
        var config = _store.Load();
        config.Hotkey = string.IsNullOrWhiteSpace(combo) ? null : combo.Trim();
        _store.Save(config);
    }

    /// <summary>Apply a label-only regroup: for each change, load the app, set its
    /// <see cref="AppEntry.Group"/>/<see cref="AppEntry.Role"/>/<see cref="AppEntry.Order"/> and
    /// persist (atomic per app). Never starts or stops a process.</summary>
    public void ApplyRegroup(RegroupPlan plan)
    {
        if (plan.Kind == DropKind.NoOp || plan.Changes.Count == 0)
            return;

        foreach (var change in plan.Changes)
        {
            var app = LoadApps().FirstOrDefault(a => a.Id == change.AppId);
            if (app is null) continue; // app vanished underneath us — skip silently
            app.Group = change.Group;
            app.Role = change.Role;
            app.Order = change.Order;
            _store.UpdateApp(app);
        }
    }
    /// <summary>Rename a group: re-label every member whose <see cref="AppEntry.Group"/> matches
    /// <paramref name="oldName"/> (case-insensitive) to the trimmed <paramref name="newName"/> and
    /// persist (atomic per app, same door as <see cref="ApplyRegroup"/>). Label-only — never starts
    /// or stops a process. No-op when the new name is blank or unchanged.</summary>
    public void RenameGroup(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, oldName, StringComparison.Ordinal))
            return;

        var trimmed = newName.Trim();
        foreach (var app in LoadApps())
        {
            if (!string.Equals(app.Group, oldName, StringComparison.OrdinalIgnoreCase))
                continue;
            app.Group = trimmed;
            _store.UpdateApp(app);
        }
    }

    public void Remove(AppEntry entry)
    {
        _processes.Stop(entry.Id); // stop if running, then forget
        _store.RemoveApp(entry.Id);
    }

    public IEnumerable<int> ReservedPorts(string? excludeId = null) =>
        LoadApps().Where(a => a.Port.HasValue && a.Id != excludeId).Select(a => a.Port!.Value);

    /// <summary>Port Doctor scan: passes the live AppShelf-managed PIDs so our own servers
    /// show as Managed (the user's launched dev servers are job-owned by this process).</summary>
    public IReadOnlyList<PortReport> ScanPorts() =>
        new PortDoctor().Scan(LoadApps(), _processes.ManagedPids());

    /// <summary>Kill whatever is listening on a port (its whole tree) and free it. Returns the
    /// outcome so the UI can surface the reason when a kill fails (e.g. a Windows service).</summary>
    public KillOutcome KillPort(int port) => ProcessManager.KillByPort(port);

    /// <summary>Pre-flight: returns info about a foreign process holding the app's registered port,
    /// or null when there is no conflict. Null when the entry has no port, is URL-only, nothing is
    /// listening, or the listener is one of our own managed PIDs. Composes the existing
    /// <see cref="PortProcessFinder.FindListenerPid"/> + <see cref="ProcessManager.ManagedPids"/>
    /// primitives via the pure <see cref="PortConflict.IsConflict"/> decision.</summary>
    public PortConflictInfo? CheckPortConflict(AppEntry entry)
    {
        if (entry.Port is not int port || entry.IsUrlOnly)
            return null;

        var pid = PortProcessFinder.FindListenerPid(port);
        if (!PortConflict.IsConflict(pid, _processes.ManagedPids()))
            return null;

        string name;
        try   { name = System.Diagnostics.Process.GetProcessById(pid!.Value).ProcessName; }
        catch { name = "unknown"; }

        return new PortConflictInfo(port, pid!.Value, name);
    }

    /// <summary>Injection guard for the service name that gets interpolated into the elevated
    /// <c>Stop-Service</c> command. Allows ONLY <c>[A-Za-z0-9._\- ]</c> (alphanumeric, dot,
    /// underscore, hyphen, space). Anything outside that set — in particular single quotes, which
    /// could break out of the quoted <c>-Name '...'</c> argument — is rejected so we never spawn a
    /// crafted PowerShell command. Windows SCM service names are restricted to a similar character
    /// set in practice, so a legitimate service is never blocked.</summary>
    internal static bool IsValidServiceName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9._\- ]+$");

    /// <summary>Stop a Windows service by SCM internal name via a per-action UAC-elevated
    /// <c>Stop-Service</c>, then poll until <paramref name="port"/> is free. AppShelf itself stays
    /// non-elevated. Never throws to the caller: a rejected name, a cancelled UAC prompt (Win32
    /// 1223), a spawn failure, a non-zero exit, and a stopped-but-still-bound port all map to a
    /// <see cref="ServiceStopOutcome"/> with a readable <see cref="ServiceStopOutcome.Reason"/>
    /// (null on full success and on the quiet cancel path).</summary>
    public async Task<ServiceStopOutcome> StopServiceElevatedAsync(string serviceName, int port)
    {
        if (!IsValidServiceName(serviceName))
            return new ServiceStopOutcome(false, false, false,
                $"Service name '{serviceName}' contains characters that cannot be passed safely. " +
                "Stop it manually from an elevated terminal.");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            Arguments = $"-NoProfile -NonInteractive -Command \"Stop-Service -Name '{serviceName}' -Force\""
        };

        System.Diagnostics.Process? proc;
        try
        {
            proc = System.Diagnostics.Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled the UAC elevation prompt — not an error.
            return new ServiceStopOutcome(false, true, false, null);
        }
        catch (Exception ex)
        {
            return new ServiceStopOutcome(false, false, false,
                $"Could not start the elevated Stop-Service process: {ex.Message}");
        }

        if (proc is null)
            return new ServiceStopOutcome(false, false, false, "Process did not start.");

        try
        {
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                // The elevated process is still running after 30 s — kill its tree so it does not
                // linger. The kill may itself throw if the process exited in the meantime.
                try { proc.Kill(entireProcessTree: true); }
                catch { /* already gone — nothing to clean up */ }

                return new ServiceStopOutcome(true, false, false,
                    "Stop-Service timed out after 30 seconds. The service may still be running.");
            }

            if (proc.ExitCode != 0)
                return new ServiceStopOutcome(true, false, false,
                    $"Stop-Service exited with code {proc.ExitCode}. The service may still be running.");
        }
        finally
        {
            proc.Dispose();
        }

        // Poll until the port is free (max ~3 s / 20 × 150 ms) — same pattern as ReclaimAsync.
        for (var i = 0; i < 20 && ProcessManager.IsPortInUse(port); i++)
            await Task.Delay(150);

        bool portFreed = !ProcessManager.IsPortInUse(port);
        return new ServiceStopOutcome(true, false, portFreed,
            portFreed ? null : $"Service stopped but port {port} is still in use — it may take a moment to release.");
    }

    /// <summary>Start members in the given (already role-ordered) sequence, waiting for each to
    /// come up before the next — backend before frontend. Skips members already running.</summary>
    public async Task StartGroupAsync(IReadOnlyList<AppEntry> orderedMembers)
    {
        foreach (var member in orderedMembers)
        {
            if (StatusOf(member) == LaunchStatus.Running) continue;
            await _launcher.LaunchAsync(member);
        }
    }

    /// <summary>Live ports held by group members that AppShelf did NOT launch (for the Stop-all
    /// confirm prompt).</summary>
    public IReadOnlyList<int> UnmanagedLivePorts(IEnumerable<AppEntry> members) =>
        members.Where(m => !_processes.IsManaged(m.Id)
                           && m.Port is int
                           && ProcessManager.IsPortListening(m.Url))
               .Select(m => m.Port!.Value)
               .ToList();

    /// <summary>Stop all members (reverse order). Managed members are torn down cleanly; unmanaged
    /// but live members are killed by port (adopt). Caller confirms BEFORE invoking when
    /// <see cref="UnmanagedLivePorts"/> is non-empty.</summary>
    public void StopGroup(IReadOnlyList<AppEntry> orderedMembers)
    {
        foreach (var member in orderedMembers.Reverse())
        {
            if (_processes.Stop(member.Id)) continue;            // managed -> clean kill
            if (member.Port is int p && ProcessManager.IsPortListening(member.Url))
                ProcessManager.KillByPort(p);                    // adopt-by-port
        }
    }

    public void Dispose() => _processes.Dispose(); // kills all trees, frees ports
}
