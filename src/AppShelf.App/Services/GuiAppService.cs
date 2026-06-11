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

    /// <summary>Live status for the grid: Running if the port answers, Starting if we are
    /// managing it but it is not up yet, otherwise Stopped.</summary>
    public LaunchStatus StatusOf(AppEntry entry)
    {
        if (ProcessManager.IsPortListening(entry.Url))
            return LaunchStatus.Running;
        if (_processes.IsManaged(entry.Id))
            return LaunchStatus.Starting;
        return LaunchStatus.Stopped;
    }

    public IReadOnlyList<string> LogTail(AppEntry entry) => _processes.GetLogTail(entry.Id);

    public AppEntry Add(AppEntry entry) => _store.AddApp(entry);
    public void Update(AppEntry entry) => _store.UpdateApp(entry);

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
