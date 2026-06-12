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
/// / WorkingDir are null for now (the tier does not depend on them).
/// </summary>
public sealed class WindowsProcessEvidenceProvider : IProcessEvidenceProvider
{
    public ProcessEvidence ForPid(int pid)
    {
        string name = $"pid {pid}";
        string? exe = null;
        DateTimeOffset? started = null;
        bool parentAlive = true;
        bool isService = false;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            name = p.ProcessName;
            try { exe = p.MainModule?.FileName; } catch { /* protected */ }
            try { started = p.StartTime; } catch { /* protected */ }

            var (ppid, parentName) = GetParentFromSnapshot(pid);
            parentAlive = IsParentAlive(p, ppid);
            isService = IsServiceParent(parentName);
        }
        catch (ArgumentException) { /* process gone between scan and query */ }
        return new ProcessEvidence(pid, name, exe, CommandLine: null, WorkingDir: null, started, parentAlive, isService);
    }

    private static bool IsParentAlive(System.Diagnostics.Process child, int? ppid)
    {
        try
        {
            if (ppid is null) return true;        // snapshot failed -> uncertain, fail safe
            if (ppid <= 0) return false;          // no real parent -> orphaned
            using var parent = System.Diagnostics.Process.GetProcessById(ppid.Value);
            try { return parent.StartTime <= child.StartTime; } // guard PID reuse
            catch { return true; }                              // can't read parent start -> fail safe
        }
        catch (ArgumentException) { return false; } // parent PID has no live process -> orphaned
        catch { return true; }                      // uncertain -> fail safe
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
    /// Reads <paramref name="pid"/>'s parent PID and the parent's exe file name from a Toolhelp32
    /// snapshot. Returns <c>(null, null)</c> when the snapshot fails or the pid is not found —
    /// callers treat that as uncertainty and fail safe.
    /// </summary>
    private static (int? Ppid, string? ParentExeName) GetParentFromSnapshot(int pid)
    {
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == InvalidHandle)
            return (null, null);
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };

            // First pass: locate the target pid -> capture its parent pid.
            uint? parentPid = null;
            if (Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    if (entry.th32ProcessID == (uint)pid)
                    {
                        parentPid = entry.th32ParentProcessID;
                        break;
                    }
                } while (Process32NextW(snapshot, ref entry));
            }

            if (parentPid is not uint ppid)
                return (null, null);

            // Second pass: find the parent pid -> capture its exe name.
            // (A fresh snapshot is unnecessary; rescan the same one.)
            var entry2 = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            string? parentName = null;
            if (Process32FirstW(snapshot, ref entry2))
            {
                do
                {
                    if (entry2.th32ProcessID == ppid)
                    {
                        parentName = entry2.szExeFile;
                        break;
                    }
                } while (Process32NextW(snapshot, ref entry2));
            }

            return ((int)ppid, parentName);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }
}
