using AppShelf.Core.Config;
using AppShelf.Core.Process;

namespace AppShelf.Cli.Commands;

/// <summary>
/// `appshelf stop &lt;id|name&gt;` (spec §4). Finds the process listening on the app's port and
/// kills its tree (taskkill /T) — works across CLI invocations for apps launched detached.
/// </summary>
public static class StopCommand
{
    public static int Run(ConfigStore store, ArgMap args)
    {
        var token = args.FirstPositional;
        if (token is null)
        {
            CliHelpers.Error("usage: appshelf stop <id|name>");
            return 1;
        }

        var config = store.Load();
        var entry = CliHelpers.ResolveOrReport(config, token);
        if (entry is null)
            return 1;

        if (entry.IsUrlOnly)
        {
            CliHelpers.Info($"'{entry.Name}' is a URL-only app — nothing to stop.");
            return 0;
        }

        var port = CliHelpers.EffectivePort(entry);
        if (port is null)
        {
            CliHelpers.Error($"can't determine a port for '{entry.Name}'.");
            return 1;
        }

        var pid = PortProcessFinder.FindListenerPid(port.Value);
        if (pid is null)
        {
            CliHelpers.Info($"'{entry.Name}': nothing listening on port {port} (already stopped?).");
            return 0;
        }

        var outcome = ProcessManager.KillTree(pid.Value);
        if (outcome.Success && !ProcessManager.IsPortInUse(port.Value))
        {
            CliHelpers.Info($"Stopped '{entry.Name}' (pid {pid}); port {port} freed.");
            return 0;
        }

        var reason = outcome.Success
            ? "It may be owned by another process or the GUI."
            : outcome.Reason ?? "the kill failed.";
        CliHelpers.Error($"attempted to stop '{entry.Name}' (pid {pid}) but port {port} may still be in use. " +
                         reason);
        return 1;
    }
}
