using AppShelf.Core.Config;
using AppShelf.Core.Process;

namespace AppShelf.Cli.Commands;

/// <summary>
/// Port registry CLI (extension): `appshelf port &lt;id|name&gt; [--set N | --auto]` to view or
/// assign a reserved port, and `appshelf ports` to list assignments and flag conflicts.
/// </summary>
public static class PortCommand
{
    public static int RunPort(ConfigStore store, ArgMap args)
    {
        var token = args.FirstPositional;
        if (token is null)
        {
            CliHelpers.Error("usage: appshelf port <id|name> [--set <port> | --auto]");
            return 1;
        }

        var config = store.Load();
        var entry = CliHelpers.ResolveOrReport(config, token);
        if (entry is null)
            return 1;

        // --set <port>: pin a fixed port.
        if (args.Get("set") is { } raw)
        {
            if (!int.TryParse(raw, out var port) || port is < 1 or > 65535)
            {
                CliHelpers.Error($"invalid port '{raw}'.");
                return 1;
            }

            var clash = config.Apps.FirstOrDefault(a =>
                a.Port == port && !string.Equals(a.Id, entry.Id, StringComparison.Ordinal));
            if (clash is not null)
            {
                CliHelpers.Error($"port {port} is already reserved by '{clash.Name}' ({clash.Id}).");
                return 1;
            }

            entry.Port = port;
            entry.PortFixed = true;
            entry.Url = CliHelpers.WithPort(entry.Url, port);
            store.UpdateApp(entry);
            CliHelpers.Info($"'{entry.Name}' pinned to port {port}. url={entry.Url}");
            return 0;
        }

        // --auto: allocate the next clash-free port.
        if (args.Has("auto"))
        {
            var reserved = CliHelpers.ReservedPorts(config, entry.Id).ToList();
            var preferred = CliHelpers.EffectivePort(entry);
            var port = new PortAllocator().Allocate(reserved, preferred);
            entry.Port = port;
            entry.PortFixed = false;
            entry.Url = CliHelpers.WithPort(entry.Url, port);
            store.UpdateApp(entry);
            CliHelpers.Info($"'{entry.Name}' assigned port {port}. url={entry.Url}");
            return 0;
        }

        // No flag: show current assignment.
        var current = entry.Port?.ToString() ?? "(none)";
        var fixedLabel = entry.Port.HasValue ? (entry.PortFixed ? " (fixed)" : " (reserved)") : "";
        var live = entry.Port.HasValue && ProcessManager.IsPortInUse(entry.Port.Value) ? " — in use now" : "";
        CliHelpers.Info($"'{entry.Name}': port {current}{fixedLabel}{live}");
        return 0;
    }

    public static int RunPorts(ConfigStore store, ArgMap args)
    {
        var config = store.Load();
        var withPorts = config.Apps.Where(a => a.Port.HasValue).OrderBy(a => a.Port).ToList();

        if (withPorts.Count == 0)
        {
            CliHelpers.Info("No ports reserved yet.");
            return 0;
        }

        CliHelpers.Info("PORT   FIXED  IN-USE  APP");
        foreach (var a in withPorts)
        {
            var inUse = ProcessManager.IsPortInUse(a.Port!.Value) ? "yes" : "no";
            CliHelpers.Info($"{a.Port,-6} {(a.PortFixed ? "yes" : "no"),-6} {inUse,-7} {a.Name} ({a.Id})");
        }

        var conflicts = withPorts
            .GroupBy(a => a.Port!.Value)
            .Where(g => g.Count() > 1)
            .ToList();

        if (conflicts.Count > 0)
        {
            CliHelpers.Info("");
            foreach (var g in conflicts)
                CliHelpers.Error($"CONFLICT: port {g.Key} reserved by {string.Join(", ", g.Select(a => a.Id))}. " +
                                 "Reassign one with 'appshelf port <id> --auto'.");
        }

        return conflicts.Count > 0 ? 1 : 0;
    }
}
