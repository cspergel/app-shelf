using AppShelf.Core.Config;
using AppShelf.Core.Launch;
using AppShelf.Core.Process;

namespace AppShelf.Cli.Commands;

/// <summary>
/// `appshelf launch &lt;id|name&gt;` (spec §4). URL-only apps open immediately. Local apps are
/// spawned DETACHED so the dev server survives the CLI exiting, then the browser is opened
/// once the port is up. Stopping is best handled by 'appshelf stop' or the GUI (§4 caveat).
/// </summary>
public static class LaunchCommand
{
    public static async Task<int> RunAsync(ConfigStore store, ArgMap args)
    {
        var token = args.FirstPositional;
        if (token is null)
        {
            CliHelpers.Error("usage: appshelf launch <id|name>");
            return 1;
        }

        var config = store.Load();
        var entry = CliHelpers.ResolveOrReport(config, token);
        if (entry is null)
            return 1;

        var opener = new TargetOpener();

        if (entry.IsUrlOnly)
        {
            opener.Open(entry);
            CliHelpers.Info($"Opened {entry.Url}");
            return 0;
        }

        if (ProcessManager.IsPortListening(entry.Url))
        {
            opener.Open(entry);
            CliHelpers.Info($"'{entry.Name}' is already running. Opened {entry.Url}");
            return 0;
        }

        CliHelpers.Info($"Starting '{entry.Name}'...");
        var pid = ProcessManager.StartDetached(entry);

        var deadline = TimeSpan.FromSeconds(30);
        var waited = TimeSpan.Zero;
        var step = TimeSpan.FromMilliseconds(250);
        while (waited < deadline)
        {
            if (ProcessManager.IsPortListening(entry.Url))
            {
                opener.Open(entry);
                entry.LastLaunchedAt = DateTimeOffset.UtcNow;
                store.UpdateApp(entry);
                CliHelpers.Info($"Launched '{entry.Name}' (pid {pid}) at {entry.Url}");
                CliHelpers.Info($"Note: run 'appshelf stop {entry.Id}' to stop it (the CLI launched it detached).");
                return 0;
            }
            await Task.Delay(step);
            waited += step;
        }

        CliHelpers.Error($"'{entry.Name}' did not come up at {entry.Url} within 30s; it may still be starting (pid {pid}).");
        return 1;
    }
}
