using System.Net;
using System.Net.Sockets;
using AppShelf.Core.Process;

namespace AppShelf.Core.Tests;

public class PortProcessFinderTests
{
    [Fact]
    public void FindListenerPid_IPv4Listener_ReturnsThisProcess()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); // 127.0.0.1
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.Equal(Environment.ProcessId, PortProcessFinder.FindListenerPid(port));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void FindListenerPid_IPv6Listener_ReturnsThisProcess()
    {
        var listener = new TcpListener(IPAddress.IPv6Loopback, 0); // ::1
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.Equal(Environment.ProcessId, PortProcessFinder.FindListenerPid(port));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void FindListenerPid_FreePort_ReturnsNull()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Assert.Null(PortProcessFinder.FindListenerPid(port));
    }
}
