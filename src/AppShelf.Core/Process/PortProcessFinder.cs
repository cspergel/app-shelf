using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace AppShelf.Core.Process;

/// <summary>
/// Finds which process (PID) is listening on a TCP port, via the Win32 IP Helper API
/// (GetExtendedTcpTable). Enumerates BOTH families: AF_INET (IPv4) and AF_INET6 (IPv6),
/// because dev servers bind different families (Vite -> ::1, uvicorn -> 127.0.0.1/0.0.0.0)
/// and a single-family scan misses live servers in the other.
/// </summary>
public static class PortProcessFinder
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint NO_ERROR = 0;

    /// <summary>Returns the PID listening on the given TCP port (either family), or null.</summary>
    public static int? FindListenerPid(int port)
    {
        foreach (var row in EnumerateListeners())
            if (row.Port == port)
                return row.Pid;
        return null;
    }

    /// <summary>All TCP listeners across both families: (port, pid, family).</summary>
    public static IReadOnlyList<(int Port, int Pid, AddressFamily Family)> ListListeners() =>
        EnumerateListeners().ToList();

    private static IEnumerable<(int Port, int Pid, AddressFamily Family)> EnumerateListeners()
    {
        foreach (var row in EnumerateIPv4())
            yield return row;
        foreach (var row in EnumerateIPv6())
            yield return row;
    }

    private static IEnumerable<(int Port, int Pid, AddressFamily Family)> EnumerateIPv4()
    {
        var size = 0;
        var ret = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
        if (ret != ERROR_INSUFFICIENT_BUFFER && ret != NO_ERROR)
            yield break;

        var table = Marshal.AllocHGlobal(size);
        try
        {
            ret = GetExtendedTcpTable(table, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (ret != NO_ERROR)
                yield break;

            var count = Marshal.ReadInt32(table);
            var rowPtr = IntPtr.Add(table, 4);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                yield return (NetworkPort(row.localPort), (int)row.owningPid, AddressFamily.InterNetwork);
                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally { Marshal.FreeHGlobal(table); }
    }

    private static IEnumerable<(int Port, int Pid, AddressFamily Family)> EnumerateIPv6()
    {
        var size = 0;
        var ret = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_LISTENER, 0);
        if (ret != ERROR_INSUFFICIENT_BUFFER && ret != NO_ERROR)
            yield break;

        var table = Marshal.AllocHGlobal(size);
        try
        {
            ret = GetExtendedTcpTable(table, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (ret != NO_ERROR)
                yield break;

            var count = Marshal.ReadInt32(table);
            var rowPtr = IntPtr.Add(table, 4);
            var rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(rowPtr);
                yield return (NetworkPort(row.localPort), (int)row.owningPid, AddressFamily.InterNetworkV6);
                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally { Marshal.FreeHGlobal(table); }
    }

    private static int NetworkPort(uint raw) =>
        IPAddress.NetworkToHostOrder((short)(raw & 0xFFFF)) & 0xFFFF;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] remoteAddr;
        public uint remoteScopeId;
        public uint remotePort;
        public uint state;
        public uint owningPid;
    }
}
