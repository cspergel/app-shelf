using System.Net;
using System.Net.Sockets;
using AppShelf.Core.Process;

namespace AppShelf.Core.Tests;

/// <summary>
/// <see cref="ProcessManager.IsPortListening"/> must report a live server regardless of which
/// loopback family it bound to. 'localhost' resolves to BOTH ::1 and 127.0.0.1 (IPv6 first on
/// Windows), and dev servers pick different families: Vite binds ::1 (IPv6), uvicorn
/// --host 127.0.0.1 / 0.0.0.0 binds IPv4. A single-family probe gives a false "stopped" for the
/// other family — a uvicorn backend (IPv4) showing grayed-out while live was exactly this.
/// </summary>
public class PortListeningTests
{
    [Fact]
    public void IsPortListening_IPv4OnlyLoopback_DetectedViaLocalhost()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); // 127.0.0.1, IPv4 only
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.True(ProcessManager.IsPortListening($"http://localhost:{port}/docs"));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void IsPortListening_IPv6OnlyLoopback_DetectedViaLocalhost()
    {
        var listener = new TcpListener(IPAddress.IPv6Loopback, 0); // ::1, IPv6 only
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.True(ProcessManager.IsPortListening($"http://localhost:{port}"));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void IsPortListening_NothingListening_ReturnsFalse()
    {
        // Reserve an ephemeral port, then release it so nothing is listening there.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Assert.False(ProcessManager.IsPortListening($"http://localhost:{port}", timeoutMs: 250));
    }
}
