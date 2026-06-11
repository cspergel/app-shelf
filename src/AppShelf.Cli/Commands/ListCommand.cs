using AppShelf.Core.Config;
using AppShelf.Core.Process;

namespace AppShelf.Cli.Commands;

/// <summary>`appshelf list` — table of name | type | status | port | url (spec §4).</summary>
public static class ListCommand
{
    public static int Run(ConfigStore store, ArgMap args)
    {
        var config = store.Load();
        if (config.Apps.Count == 0)
        {
            CliHelpers.Info("No apps yet. Add one with 'appshelf add'.");
            return 0;
        }

        var rows = config.Apps
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new[]
            {
                a.Name,
                a.IsUrlOnly ? "URL" : "Local",
                ProcessManager.IsPortListening(a.Url) ? "running" : "stopped",
                a.Port?.ToString() ?? "-",
                a.Url,
            })
            .ToList();

        string[] headers = { "NAME", "TYPE", "STATUS", "PORT", "URL" };
        var widths = new int[headers.Length];
        for (var c = 0; c < headers.Length; c++)
            widths[c] = Math.Max(headers[c].Length, rows.Max(r => r[c].Length));

        CliHelpers.Info(Format(headers, widths));
        foreach (var row in rows)
            CliHelpers.Info(Format(row, widths));
        return 0;
    }

    private static string Format(string[] cells, int[] widths) =>
        string.Join("  ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd();
}
