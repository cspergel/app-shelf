using System.Management;
using System.Runtime.InteropServices;

namespace AppShelf.Core.Process;

/// <summary>
/// Real process evidence on Windows. Process name / exe path / start time come from
/// <see cref="System.Diagnostics.Process"/> (each access wrapped — they throw for protected
/// processes). Parent PID and the parent's exe name come from a Toolhelp32 process snapshot,
/// which (unlike <c>NtQueryInformationProcess</c> on <c>p.Handle</c>) reads protected/service
/// processes WITHOUT elevation — required to flag service-backed ports like the
/// <c>PEMHTTPD-x64</c> service's <c>httpd.exe</c>. Parent-alive then confirms a still-living
/// process with that PID started no later than the child (guards PID reuse). On any uncertainty
/// we default <c>ParentAlive = true</c> so we never label something orphaned on a guess.
/// <c>IsService</c> = parent exe is <c>services.exe</c> (the Service Control Manager). CommandLine
/// / ExeDir are populated from WMI for the root + alive ancestors (the tier does not depend on them).
/// </summary>
public sealed class WindowsProcessEvidenceProvider : IProcessEvidenceProvider
{
    public ProcessEvidence ForPid(int pid)
    {
        string name = $"pid {pid}";
        string? exe = null;
        string? cmdLine = null;
        string? exeDir = null;
        DateTimeOffset? started = null;
        bool parentAlive = true;
        bool isService = false;
        string? serviceName = null;
        IReadOnlyList<AncestorInfo>? ancestry = null;

        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            name = p.ProcessName;
            try { exe = p.MainModule?.FileName; } catch { /* protected */ }
            try { started = p.StartTime; } catch { /* protected */ }

            var snapshotDict = BuildSnapshotDict();

            // Service-host PIDs from the SCM (running services + their host PIDs). Queried once
            // per ForPid (matches the existing WMI-per-ForPid pattern; the set is small).
            var (serviceHostPids, serviceNames) = QueryRunningServiceHostsWithNames();

            // Immediate parent (for ParentAlive + IsService — preserve existing logic)
            string? immediateParentExe = null;
            if (snapshotDict.TryGetValue((uint)pid, out var parentEntry))
            {
                immediateParentExe = parentEntry.ExeName;
                // parentAlive: inject real start-time lookup
                var parentStartTime = GetProcessStartTime(parentEntry.ParentPid);
                parentAlive = parentStartTime.HasValue &&
                              (!started.HasValue || parentStartTime.Value <= started.Value);
            }

            // IsService = immediate parent is services.exe (cheap signal) OR this process is itself
            // a service host PID OR any raw ancestor is one (catches service WORKERS, e.g. an Apache
            // httpd worker whose parent is the master httpd, not services.exe).
            isService = IsServiceParent(immediateParentExe)
                        || IsServiceBacked(pid, snapshotDict, serviceHostPids, MaxAncestryDepth);

            // When service-backed, resolve the SCM internal service name (for an elevated
            // Stop-Service). Walk to the matching service-host PID and look its name up in the WMI
            // PID→name map. Null when the host PID has no name (e.g. the services.exe-parent signal
            // fired but the host PID is not in the running-service set).
            if (isService &&
                TryFindServiceHost(pid, snapshotDict, serviceHostPids, MaxAncestryDepth, out var hostPid))
                serviceNames.TryGetValue(hostPid, out serviceName);

            // Build the full ancestry chain (pure — no WMI yet; liveness injected)
            var chain = WalkAncestry(
                pid,
                started,
                snapshotDict,
                getStartTime: GetProcessStartTime);

            // Batch WMI query for all alive ancestor PIDs (plus the root process itself)
            var alivePids = chain
                .Where(a => a.Alive)
                .Select(a => a.Pid)
                .ToList();
            alivePids.Insert(0, pid);

            var wmiData = QueryWmiCmdLines(alivePids);

            // Root process: populate cmdLine/exeDir from WMI
            if (wmiData.TryGetValue(pid, out var rootWmi))
            {
                cmdLine = rootWmi.CommandLine;
                exeDir = rootWmi.ExeDir;
            }

            // Re-materialize chain with WMI data filled in for alive nodes
            ancestry = chain
                .Select(a =>
                {
                    if (!a.Alive) return a;
                    if (!wmiData.TryGetValue(a.Pid, out var wmi)) return a;
                    return a with { CommandLine = wmi.CommandLine, ExeDir = wmi.ExeDir };
                })
                .ToList()
                .AsReadOnly();
        }
        catch (ArgumentException) { /* process gone between scan and query */ }

        return new ProcessEvidence(pid, name, exe, cmdLine, exeDir, started, parentAlive, isService, ancestry, serviceName);
    }

    /// <summary>
    /// Returns the start time of the process with <paramref name="pid"/>, or null if the process
    /// is dead, inaccessible, or its start time cannot be read. Null → treat as dead.
    /// </summary>
    private static DateTimeOffset? GetProcessStartTime(uint pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            try { return (DateTimeOffset)p.StartTime; } catch { return null; }
        }
        catch { return null; }
    }

    /// <summary>
    /// True when the parent process exe is <c>services.exe</c> (the Service Control Manager), which
    /// directly parents standalone service exes like <c>httpd.exe</c> (PEMHTTPD). A best-effort
    /// "looks like a service" hint, not a guarantee; fails safe to false. Does NOT catch
    /// svchost-hosted services, which essentially never bind dev ports.
    /// </summary>
    private static bool IsServiceParent(string? parentExeName)
    {
        if (string.IsNullOrEmpty(parentExeName)) return false;
        // Snapshot gives the exe file name (e.g. "services.exe"); compare on the stem.
        var stem = System.IO.Path.GetFileNameWithoutExtension(parentExeName);
        return string.Equals(stem, "services", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when <paramref name="pid"/> is, or descends from, a running-service host process.
    /// This generalises <see cref="IsServiceParent"/>: it catches service WORKERS whose immediate
    /// parent is not <c>services.exe</c> but a service master (e.g. an Apache <c>httpd</c> worker
    /// whose parent is the master <c>httpd</c>, which the SCM launched).
    ///
    /// Walks the RAW parent links in <paramref name="snapshotDict"/> — deliberately independent of
    /// the alive-state / stop-at-first-dead logic in <see cref="WalkAncestry"/>: a service master is
    /// often access-denied (its start time cannot be read, so it "looks dead"), yet it is still a
    /// valid service host PID. Membership is tested against <paramref name="serviceHostPids"/> (the
    /// SCM's own PID list), so it does not depend on reading any protected process.
    ///
    /// Termination: cycle guard (visited set), depth cap (<paramref name="maxDepth"/>), and the
    /// System/root sentinels (parent PID 0 or 4). Pure / static → unit-testable with fake inputs.
    ///
    /// PID-reuse caveat: a recycled PID could in theory match an entry in <paramref name="serviceHostPids"/>.
    /// Because that is a membership test against the SCM's current host PIDs (not a guessed lineage),
    /// a false positive is unlikely and only mislabels a port as service-backed (fail-safe direction).
    /// </summary>
    internal static bool IsServiceBacked(
        int pid,
        Dictionary<uint, (uint ParentPid, string ExeName)> snapshotDict,
        IReadOnlySet<int> serviceHostPids,
        int maxDepth) =>
        TryFindServiceHost(pid, snapshotDict, serviceHostPids, maxDepth, out _);

    /// <summary>
    /// Pure walk that finds the service-host PID backing <paramref name="pid"/>: returns
    /// <c>true</c> and sets <paramref name="matchedHostPid"/> when <paramref name="pid"/> itself or
    /// any ancestor (up to <paramref name="maxDepth"/>) is a member of
    /// <paramref name="serviceHostPids"/>; otherwise returns <c>false</c> and sets
    /// <paramref name="matchedHostPid"/> to 0.
    ///
    /// Same walk semantics as the former <see cref="IsServiceBacked"/> body: raw parent links
    /// (independent of alive-state), cycle guard (visited set), System/root sentinels (parent PID 0
    /// or 4), and a depth cap. The returned host PID lets callers look the matching service NAME up
    /// in the WMI PID→name map from <see cref="QueryRunningServiceHostsWithNames"/>.
    ///
    /// PID-reuse caveat: identical to <see cref="IsServiceBacked"/> — a recycled PID could in
    /// theory match a current service-host PID, but that is a membership test against the SCM's own
    /// list (not a guessed lineage), so a false positive only mislabels a port as service-backed
    /// (fail-safe direction).
    /// </summary>
    internal static bool TryFindServiceHost(
        int pid,
        Dictionary<uint, (uint ParentPid, string ExeName)> snapshotDict,
        IReadOnlySet<int> serviceHostPids,
        int maxDepth,
        out int matchedHostPid)
    {
        if (serviceHostPids.Contains(pid))
        {
            matchedHostPid = pid;
            return true;
        }

        var visited = new HashSet<uint> { (uint)pid };
        uint currentPid = (uint)pid;

        for (int depth = 0; depth < maxDepth; depth++)
        {
            if (!snapshotDict.TryGetValue(currentPid, out var parentEntry))
                break;                                  // parent not in snapshot → exited / root → stop

            var parentPid = parentEntry.ParentPid;
            if (parentPid == 0 || parentPid == 4) break; // System / root sentinel
            if (!visited.Add(parentPid)) break;          // cycle guard

            if (serviceHostPids.Contains((int)parentPid))
            {
                matchedHostPid = (int)parentPid;
                return true;
            }

            currentPid = parentPid;
        }

        matchedHostPid = 0;
        return false;
    }

    // --- Toolhelp32 process snapshot (works for protected/service processes without elevation) ---

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const int MAX_PATH = 260;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>
    /// Materializes the current Toolhelp32 process snapshot into a PID→(parentPid, exeName) dict.
    /// Returns an empty dictionary on failure (snapshot unavailable or access denied). One snapshot
    /// per ForPid call; no cross-call sharing (avoids staleness). Pure / static: no Win32 state
    /// escapes, so the walk logic that consumes this dict is unit-testable in isolation.
    /// </summary>
    private static Dictionary<uint, (uint ParentPid, string ExeName)> BuildSnapshotDict()
    {
        var dict = new Dictionary<uint, (uint, string)>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == InvalidHandle) return dict;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    dict[entry.th32ProcessID] = (entry.th32ParentProcessID, entry.szExeFile);
                } while (Process32NextW(snapshot, ref entry));
            }
        }
        catch { /* fail safe: partial dict is still useful */ }
        finally { CloseHandle(snapshot); }
        return dict;
    }

    internal const int MaxAncestryDepth = 16;

    /// <summary>
    /// Walks the PID-parent chain in <paramref name="snapshotDict"/>, starting from the parent
    /// of <paramref name="startPid"/>. Returns ancestors in nearest-first order (parent, grandparent, …).
    ///
    /// Termination conditions:
    ///   - Parent PID is 0 or 4 (System/root sentinel).
    ///   - Parent PID not found in snapshot (exited parent — the actual orphan case).
    ///   - Visited set blocks a cycle.
    ///   - Depth cap (<see cref="MaxAncestryDepth"/>) reached.
    ///   - PID-reuse detected: candidate parent's start time is LATER than the child's start time,
    ///     meaning this PID was recycled by a different process after the original parent exited.
    ///     Walk stops; the recycled process is NOT included.
    ///   - Dead/unverifiable ancestor encountered: the node is included as a terminal [dead] entry
    ///     and the walk stops immediately. A dead parent is the entire orphan story; anything above
    ///     a dead node cannot be trusted because the recorded grandparent PID may have been recycled.
    ///
    /// Alive-state and monotonicity are derived from <paramref name="getStartTime"/>:
    ///   null return → process is dead or inaccessible → Alive = false → include as terminal, then stop.
    ///   non-null return that is later than child start time → PID reuse → stop (excluded).
    ///   non-null return that is earlier-or-equal → Alive = true → include and continue climbing.
    ///
    /// Dead nodes have CommandLine and ExeDir = null (no WMI call is made for dead processes).
    /// Alive nodes have null cmd/exeDir here; ForPid re-projects with WMI data after the walk.
    /// </summary>
    internal static List<AncestorInfo> WalkAncestry(
        int startPid,
        DateTimeOffset? childStartTime,
        Dictionary<uint, (uint ParentPid, string ExeName)> snapshotDict,
        Func<uint, DateTimeOffset?> getStartTime)
    {
        var result = new List<AncestorInfo>();
        var visited = new HashSet<uint> { (uint)startPid };
        uint currentPid = (uint)startPid;
        DateTimeOffset? currentStartTime = childStartTime;

        for (int depth = 0; depth < MaxAncestryDepth; depth++)
        {
            if (!snapshotDict.TryGetValue(currentPid, out var parentEntry))
                break;                          // parent not in snapshot → exited or root → stop

            var (parentPid, parentExe) = parentEntry;
            if (parentPid == 0 || parentPid == 4) break;   // root / System sentinel
            if (!visited.Add(parentPid)) break;             // cycle guard

            var parentStartTime = getStartTime(parentPid);  // null → dead/inaccessible

            // PID-reuse stop: parent started AFTER its supposed child → recycled PID → bogus lineage.
            // Stop without including the node (the PID now belongs to an unrelated process).
            if (parentStartTime.HasValue && currentStartTime.HasValue &&
                parentStartTime.Value > currentStartTime.Value)
                break;

            bool alive = parentStartTime.HasValue;

            result.Add(new AncestorInfo(
                (int)parentPid,
                System.IO.Path.GetFileNameWithoutExtension(parentExe) ?? parentExe,
                alive,
                CommandLine: null,   // ForPid fills in via WMI for alive nodes
                ExeDir: null));

            // Stop-at-first-dead: a dead ancestor's recorded parent PID is untrustworthy due to
            // PID reuse. The dead node itself is the terminal orphan signal; do not climb further.
            if (!alive) break;

            // Advance: continue climbing from this live parent.
            currentPid = parentPid;
            currentStartTime = parentStartTime;
        }
        return result;
    }

    /// <summary>
    /// Queries Win32_Process via WMI for CommandLine and ExecutablePath for each PID in
    /// <paramref name="pids"/>. Returns a dictionary keyed by PID; missing entries mean the
    /// process was inaccessible (access denied, process exited before query). Never throws:
    /// access-denied and all other WMI failures degrade to an empty or partial dict.
    /// Batched (one query for all PIDs) to minimise WMI round-trips.
    ///
    /// WQL does NOT support IN (...); the WHERE clause uses an OR-chain instead:
    ///   WHERE ProcessId = 100 OR ProcessId = 200 OR ProcessId = 300
    /// PIDs are typed int so no injection risk.
    ///
    /// Note: Win32_Process has no working-directory field. ExeDir is derived as
    /// Path.GetDirectoryName(ExecutablePath) — the exe's install directory, NOT the process CWD.
    /// Render as "exe dir:" in the GUI, not "dir:" or "working dir:".
    ///
    /// This method is live-only (requires a running WMI service). It is intentionally not
    /// unit-tested.
    /// </summary>
    internal static Dictionary<int, (string? CommandLine, string? ExeDir)> QueryWmiCmdLines(
        IEnumerable<int> pids)
    {
        var result = new Dictionary<int, (string?, string?)>();
        try
        {
            var pidList = pids.ToList();
            if (pidList.Count == 0) return result;

            // WQL has no IN operator — build an OR-chain
            var whereClause = string.Join(" OR ", pidList.Select(p => $"ProcessId = {p}"));

            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                $"SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process WHERE {whereClause}");
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                try
                {
                    var pid = Convert.ToInt32(obj["ProcessId"]);
                    var cmd = obj["CommandLine"] as string;
                    var exe = obj["ExecutablePath"] as string;
                    // ExecutablePath is the exe path; derive the directory for "exe dir:" display.
                    var exeDir = exe is not null ? System.IO.Path.GetDirectoryName(exe) : null;
                    result[pid] = (cmd, exeDir);
                }
                catch { /* skip inaccessible row */ }
            }
        }
        catch { /* WMI unavailable, service stopped, access denied — return partial/empty */ }
        return result;
    }

    /// <summary>
    /// Returns the host process IDs of currently-running Windows services AND a PID→service-name
    /// map, via WMI <c>Win32_Service</c>. <see cref="Pids"/> is used by
    /// <see cref="IsServiceBacked"/> / <see cref="TryFindServiceHost"/> to flag service workers
    /// whose immediate parent is not <c>services.exe</c>; <see cref="Names"/> supplies the SCM
    /// internal service name (WMI <c>Name</c>, not <c>DisplayName</c>) used by an elevated
    /// <c>Stop-Service</c> call. ProcessId 0 (reported by stopped services) is excluded.
    ///
    /// Multi-service-per-PID: the WMI <c>Name</c> map keeps the FIRST name encountered for a PID
    /// (svchost can host many services, but standalone dev-port services are single — the first
    /// is correct for the cases we care about).
    ///
    /// Never throws: any WMI failure (service unavailable, access denied) degrades to an empty set
    /// and empty map, so detection simply falls back to the immediate-parent signal and the
    /// "Stop service" action stays hidden.
    ///
    /// This method is live-only (requires a running WMI service). It is intentionally not
    /// unit-tested; the pure walk logic lives in <see cref="TryFindServiceHost"/>.
    /// </summary>
    internal static (HashSet<int> Pids, Dictionary<int, string> Names) QueryRunningServiceHostsWithNames()
    {
        var pids = new HashSet<int>();
        var names = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT ProcessId, Name FROM Win32_Service WHERE State = 'Running'");
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                try
                {
                    var procId = Convert.ToInt32(obj["ProcessId"]);
                    if (procId == 0) continue;             // stopped services report ProcessId 0
                    pids.Add(procId);
                    var name = obj["Name"] as string;
                    if (!string.IsNullOrEmpty(name) && !names.ContainsKey(procId))
                        names[procId] = name;              // keep-first on multi-service PID
                }
                catch { /* skip inaccessible row */ }
            }
        }
        catch { /* WMI unavailable, service stopped, access denied — return empty */ }
        return (pids, names);
    }

    /// <summary>
    /// Deprecated shim — use <see cref="QueryRunningServiceHostsWithNames"/>, which also returns the
    /// PID→service-name map. Kept so any stray positional caller still compiles; returns only the
    /// host-PID set portion.
    /// </summary>
    [Obsolete("Use QueryRunningServiceHostsWithNames() which also returns the PID->service-name map.")]
    internal static HashSet<int> QueryRunningServiceHostPids() =>
        QueryRunningServiceHostsWithNames().Pids;
}
