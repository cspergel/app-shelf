using AppShelf.Core.Launch;
using AppShelf.Core.Models;
using AppShelf.Core.Process;

namespace AppShelf.Core.Tests;

public class LauncherTests
{
    private sealed class FakeOpener : ITargetOpener
    {
        public List<string> Opened { get; } = new();
        public void Open(AppEntry entry) => Opened.Add(entry.Url);
    }

    [Fact]
    public async Task UrlOnlyApp_OpensImmediately_WithoutSpawning()
    {
        var opener = new FakeOpener();
        using var pm = new ProcessManager();
        var launcher = new Launcher(pm, opener);
        var entry = new AppEntry { Id = "url", Name = "URL", Url = "https://example.com" }; // no Cmd

        var result = await launcher.LaunchAsync(entry);

        Assert.Equal(LaunchStatus.Running, result.Status);
        Assert.Equal(new[] { "https://example.com" }, opener.Opened);
        Assert.NotNull(entry.LastLaunchedAt);
    }

    [Fact]
    public async Task LocalApp_AlreadyListening_OpensWithoutSpawning()
    {
        // Stand up a real listener so IsPortListening() is true, then assert no spawn happens.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var opener = new FakeOpener();
            using var pm = new ProcessManager();
            var launcher = new Launcher(pm, opener);
            var entry = new AppEntry
            {
                Id = "warm",
                Name = "Warm",
                Dir = Path.GetTempPath(),
                Cmd = "this-command-would-fail-if-run",
                Url = $"http://127.0.0.1:{port}",
            };

            var result = await launcher.LaunchAsync(entry);

            Assert.Equal(LaunchStatus.Running, result.Status);
            Assert.Single(opener.Opened);
            Assert.False(pm.IsManaged("warm")); // never spawned
        }
        finally
        {
            listener.Stop();
        }
    }
}
