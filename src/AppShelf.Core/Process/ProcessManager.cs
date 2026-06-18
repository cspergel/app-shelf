using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using AppShelf.Core.Launch;
using AppShelf.Core.Models;

namespace AppShelf.Core.Process;

/// <summary>
/// Owns the running dev-server processes (spec §3.3). Each launched process is assigned to a
/// <see cref="JobObject"/> so stopping it kills the whole tree and frees the port. Keeps an
/// in-memory map of id → running app; this lives in the long-running host (the GUI). The CLI
/// is short-lived and only manages apps it launched in its own session (spec §4 caveat).
/// </summary>
public sealed class ProcessManager : IDisposable
{
    private const int LogTailLines = 200;

    private readonly ConcurrentDictionary<string, RunningApp> _running = new(StringComparer.Ordinal);

    /// <summary>Spawns the app's command in its directory, captures output, and assigns it to a job.</summary>
    public RunningApp Start(AppEntry entry)
    {
        if (entry.IsUrlOnly)
            throw new InvalidOperationException($"App '{entry.Id}' is URL-only and has no command to start.");
        if (string.IsNullOrWhiteSpace(entry.Dir))
            throw new InvalidOperationException($"App '{entry.Id}' has no working directory.");
        if (_running.ContainsKey(entry.Id))
            throw new InvalidOperationException($"App '{entry.Id}' is already running in this session.");

        var plan = LaunchPlanBuilder.Build(entry);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + plan.Command,
            WorkingDirectory = entry.Dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var (key, value) in plan.Environment)
            psi.Environment[key] = value;

        var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        var running = new RunningApp(entry.Id, process);

        process.OutputDataReceived += (_, e) => running.AppendLog(e.Data);
        process.ErrorDataReceived += (_, e) => running.AppendLog(e.Data);

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start app '{entry.Id}'.");

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Assign to a kill-on-close job BEFORE the dev server spawns its children.
            running.Job.AssignProcess(process);
        }
        catch
        {
            // The process is alive but never made it into the job (e.g. AssignProcessToJobObject
            // failed). Don't leak it outside the job — it would hold its port with no way to stop
            // it. Kill the tree directly, dispose the (empty) job, then surface the failure.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            running.Dispose();
            throw;
        }

        _running[entry.Id] = running;
        return running;
    }

    /// <summary>Stops an app this manager launched: closes the job (kills the tree), frees the port.</summary>
    public bool Stop(string id)
    {
        if (!_running.TryRemove(id, out var running))
            return false;

        running.Dispose(); // disposes the job -> kills process tree (KILL_ON_JOB_CLOSE)
        return true;
    }

    /// <summary>True when this manager is tracking a (still-living) process for the id.</summary>
    public bool IsManaged(string id) =>
        _running.TryGetValue(id, out var r) && !r.HasExited;

    /// <summary>True when this manager is tracking an entry for <paramref name="id"/> whose underlying
    /// process has already exited — meaning the exit was NOT driven by the AppShelf Stop path
    /// (which calls TryRemove before the process dies). False for any app that was never
    /// launched this session, was cleanly stopped, or is still alive.</summary>
    public bool IsManagedAndExited(string id) =>
        _running.TryGetValue(id, out var r) && r.HasExited;

    /// <summary>
    /// Live OS process ids of the dev servers this manager launched (skipping any that have
    /// exited). Used by the Port Doctor to mark ports we own as <see cref="PortTier.Managed"/>.
    /// </summary>
    public IReadOnlySet<int> ManagedPids()
    {
        var pids = new HashSet<int>();
        foreach (var running in _running.Values)
        {
            if (running.HasExited)
                continue;
            try { pids.Add(running.Process.Id); }
            catch { /* process gone */ }
        }
        return pids;
    }

    /// <summary>The captured log tail (last ~200 lines) for a managed app, or empty.</summary>
    public IReadOnlyList<string> GetLogTail(string id) =>
        _running.TryGetValue(id, out var r) ? r.LogTail : Array.Empty<string>();

    /// <summary>
    /// Spawns the app's command so it SURVIVES the caller exiting (no kill-on-close job). Used
    /// by the short-lived CLI: the server keeps running after <c>appshelf launch</c> returns.
    /// Returns the started process id. Output is not captured (logs are a GUI feature).
    /// </summary>
    public static int StartDetached(AppEntry entry)
    {
        if (entry.IsUrlOnly)
            throw new InvalidOperationException($"App '{entry.Id}' is URL-only and has no command to start.");
        if (string.IsNullOrWhiteSpace(entry.Dir))
            throw new InvalidOperationException($"App '{entry.Id}' has no working directory.");

        var plan = LaunchPlanBuilder.Build(entry);
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + plan.Command,
            WorkingDirectory = entry.Dir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var (key, value) in plan.Environment)
            psi.Environment[key] = value;

        var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start app '{entry.Id}'.");
        return process.Id;
    }

    /// <summary>The result of running an install command to completion.</summary>
    public sealed record SetupOutcome(bool Ok, IReadOnlyList<string> Output);

    /// <summary>
    /// Runs an app's install command (<c>cmd.exe /c {installCmd}</c>) in its directory to
    /// completion, capturing all output. Used by the GUI's one-click "Run setup" to fix a
    /// failed pre-flight (e.g. create a venv, run <c>npm install</c>) before retrying the launch.
    /// Returns Ok when the command exits 0. Never throws — a launch failure becomes captured output.
    /// </summary>
    public static async Task<SetupOutcome> RunSetupAsync(string dir, string installCmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installCmd))
            return new(false, new[] { "No install command configured." });
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return new(false, new[] { "Folder not found." });

        var output = new List<string>();
        var sink = new object();
        void Append(string? line) { if (line is not null) lock (sink) output.Add(line); }
        string[] Snapshot() { lock (sink) return output.ToArray(); }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + installCmd,
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => Append(e.Data);
            process.ErrorDataReceived += (_, e) => Append(e.Data);

            if (!process.Start())
                return new(false, new[] { $"Failed to start: {installCmd}" });

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return new(process.ExitCode == 0, Snapshot());
        }
        catch (OperationCanceledException)
        {
            var captured = Snapshot();
            return new(false, captured.Length == 0 ? new[] { "Setup was cancelled." } : captured);
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            return new(false, Snapshot());
        }
    }

    /// <summary>
    /// Kills a process AND its child tree by PID via <c>taskkill /T /F</c>. Used by the CLI to
    /// tear down a server it (or a prior CLI run) started detached, freeing the port. Returns a
    /// <see cref="KillOutcome"/> carrying the failure reason (e.g. taskkill's "Access is denied"
    /// for a Windows service) so the caller can tell the user WHY a kill did nothing.
    /// </summary>
    public static KillOutcome KillTree(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo("taskkill", $"/PID {pid} /T /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
                return KillOutcome.Fail($"could not start taskkill for pid {pid}.");

            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            if (p.ExitCode == 0)
                return KillOutcome.Ok;

            var reason = CleanReason(stderr);
            if (string.IsNullOrEmpty(reason))
                reason = CleanReason(stdout);
            if (string.IsNullOrEmpty(reason))
                reason = $"taskkill exited with code {p.ExitCode}.";

            // Classify "Access is denied" (Win32 error 5) so the GUI can show a friendly,
            // service/elevation-aware message instead of taskkill's raw multi-PID dump.
            var accessDenied = IndicatesAccessDenied(stderr) || IndicatesAccessDenied(stdout);
            return KillOutcome.Fail(reason, accessDenied);
        }
        catch (Exception ex)
        {
            return KillOutcome.Fail(ex.Message);
        }
    }

    /// <summary>True when taskkill output reports an access-denied refusal (Win32 error 5),
    /// e.g. a Windows service or an elevated/protected process the user can't kill unelevated.</summary>
    private static bool IndicatesAccessDenied(string? raw) =>
        !string.IsNullOrEmpty(raw) &&
        raw.Contains("access is denied", StringComparison.OrdinalIgnoreCase);

    /// <summary>Collapses taskkill's multi-line output into a single trimmed reason line.</summary>
    private static string CleanReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => l.Trim())
                       .Where(l => l.Length > 0);
        return string.Join(" ", lines).Trim();
    }

    /// <summary>
    /// Finds whatever process is listening on <paramref name="port"/> (either family) and kills
    /// its whole tree. Returns a failing <see cref="KillOutcome"/> when nothing is listening or
    /// the kill fails. Used to free a port held by a server this manager did not launch
    /// (adopt-by-port stop, Port Doctor).
    /// </summary>
    public static KillOutcome KillByPort(int port)
    {
        var pid = PortProcessFinder.FindListenerPid(port);
        return pid is int p ? KillTree(p) : KillOutcome.Fail($"nothing listening on port {port}");
    }

    /// <summary>TCP connect check: is something accepting connections at the URL's host:port?</summary>
    public static bool IsPortListening(string url, int timeoutMs = 500)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return CanConnect(uri.Host, uri.Port, timeoutMs);
    }

    /// <summary>Is a port already in use (something listening on loopback)?</summary>
    public static bool IsPortInUse(int port, string host = "127.0.0.1", int timeoutMs = 300) =>
        CanConnect(host, port, timeoutMs);

    private static bool CanConnect(string host, int port, int timeoutMs)
    {
        // 'localhost' resolves to BOTH ::1 (IPv6) and 127.0.0.1 (IPv4), and Windows returns
        // ::1 first. A dev server may bind only one family (Vite -> ::1, uvicorn --host
        // 127.0.0.1/0.0.0.0 -> IPv4), so a single hostname connect can probe the wrong family
        // and report a live server as stopped. Try every resolved address; any answer = up.
        foreach (var address in ResolveAddresses(host))
        {
            if (TryConnect(address, port, timeoutMs))
                return true;
        }
        return false;
    }

    private static IEnumerable<IPAddress> ResolveAddresses(string host)
    {
        if (IPAddress.TryParse(host, out var literal))
            return new[] { literal };
        try { return Dns.GetHostAddresses(host); }
        catch (SocketException) { return Array.Empty<IPAddress>(); }
        catch (ArgumentException) { return Array.Empty<IPAddress>(); }
    }

    private static bool TryConnect(IPAddress address, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            var connect = client.BeginConnect(address, port, null, null);
            if (!connect.AsyncWaitHandle.WaitOne(timeoutMs))
                return false;
            client.EndConnect(connect);
            return true;
        }
        catch (SocketException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    public void Dispose()
    {
        foreach (var id in _running.Keys.ToArray())
            Stop(id);
    }

    /// <summary>A live process plus its kill-on-close job and a bounded log ring buffer.</summary>
    public sealed class RunningApp : IDisposable
    {
        private readonly object _logLock = new();
        private readonly Queue<string> _log = new(LogTailLines);

        internal RunningApp(string id, System.Diagnostics.Process process)
        {
            Id = id;
            Process = process;
            Job = new JobObject();
            StartedAt = DateTimeOffset.UtcNow;
        }

        public string Id { get; }
        public System.Diagnostics.Process Process { get; }
        public JobObject Job { get; }
        public DateTimeOffset StartedAt { get; }

        public bool HasExited
        {
            get { try { return Process.HasExited; } catch { return true; } }
        }

        public IReadOnlyList<string> LogTail
        {
            get { lock (_logLock) return _log.ToArray(); }
        }

        internal void AppendLog(string? line)
        {
            if (line is null)
                return;
            lock (_logLock)
            {
                if (_log.Count >= LogTailLines)
                    _log.Dequeue();
                _log.Enqueue(line);
            }
        }

        public void Dispose()
        {
            Job.Dispose(); // kills the tree
            try { Process.Dispose(); } catch { /* best effort */ }
        }
    }
}
