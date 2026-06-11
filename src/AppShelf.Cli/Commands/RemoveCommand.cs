using AppShelf.Core.Config;

namespace AppShelf.Cli.Commands;

/// <summary>`appshelf rm &lt;id|name&gt;` — removes an app from config (spec §4).</summary>
public static class RemoveCommand
{
    public static int Run(ConfigStore store, ArgMap args)
    {
        var token = args.FirstPositional;
        if (token is null)
        {
            CliHelpers.Error("usage: appshelf rm <id|name>");
            return 1;
        }

        var config = store.Load();
        var entry = CliHelpers.ResolveOrReport(config, token);
        if (entry is null)
            return 1;

        store.RemoveApp(entry.Id);
        CliHelpers.Info($"Removed '{entry.Name}' ({entry.Id}).");
        return 0;
    }
}
