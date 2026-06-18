using AppShelf.Core.Config;
using AppShelf.Core.Process;

namespace AppShelf.Cli.Commands;

/// <summary>
/// `appshelf doctor` — scans relevant dev-server ports, shows what is listening with an honest
/// ownership tier, and (with `--kill &lt;port&gt;`) tears down a listener after a typed confirm
/// (`--yes` skips the prompt). The short-lived CLI manages nothing of its own, so it passes an
/// empty managed-PID set (only the long-running GUI can mark ports Managed).
///
/// `--json` emits a structured JSON array to stdout (table output suppressed). `--watch [seconds]`
/// re-scans on an interval until Ctrl+C (default 2 s); combined with `--json` it streams JSONL
/// (one compact array per tick). `--kill` cannot be combined with `--json` or `--watch`.
/// </summary>
public static class DoctorCommand
{
    public static async Task<int> RunAsync(ConfigStore store, ArgMap args)
    {
        var isJson = args.Has("json");
        var isWatch = args.Has("watch");

        // Incompatible-combo guard (before any scan).
        if ((isJson || isWatch) && args.Has("kill"))
        {
            CliHelpers.Error("--kill cannot be combined with --json or --watch.");
            return 1;
        }

        // Parse --watch interval (default 2 s; bare --watch keeps the default).
        var watchInterval = 2;
        if (isWatch && args.Get("watch") is { } rawWatch)
        {
            if (!int.TryParse(rawWatch, out watchInterval) || watchInterval < 1)
            {
                CliHelpers.Error($"--watch interval must be a positive integer (got '{rawWatch}').");
                return 1;
            }
        }

        if (isWatch)
            return await RunWatchAsync(store, isJson, watchInterval);

        if (isJson)
        {
            var apps = store.Load().Apps;
            var reports = new PortDoctor().Scan(apps, new HashSet<int>());
            Console.WriteLine(PortReportJson.ToJson(reports, indented: true));
            return 0;
        }

        if (args.Get("kill") is { } raw)
            return RunKill(store, args, raw);

        return RunTable(store);
    }

    private static async Task<int> RunWatchAsync(ConfigStore store, bool isJson, int intervalSeconds)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var apps = store.Load().Apps;
                var reports = new PortDoctor().Scan(apps, new HashSet<int>());

                if (isJson)
                {
                    Console.WriteLine(PortReportJson.ToJson(reports, indented: false));
                }
                else
                {
                    Console.WriteLine($"--- {DateTime.Now:HH:mm:ss} ---");
                    PrintTable(reports);
                }

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C — clean exit.
        }

        return 0;
    }

    private static int RunKill(ConfigStore store, ArgMap args, string raw)
    {
        var apps = store.Load().Apps;
        var doctor = new PortDoctor();

        if (!int.TryParse(raw, out var port)) { CliHelpers.Error($"invalid port '{raw}'."); return 1; }
        var report = doctor.Scan(apps, new HashSet<int>()).FirstOrDefault(r => r.Port == port);
        if (report is null) { CliHelpers.Info($"nothing listening on {port}."); return 0; }
        CliHelpers.Info($"port {port}: {report.Evidence.ProcessName} (pid {report.Evidence.Pid}) " +
                        $"[{report.Tier}] owner={report.OwnerAppName ?? "(none)"}");
        if (!args.Has("yes"))
        {
            Console.Write("kill this process tree? [y/N] ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            { CliHelpers.Info("cancelled."); return 0; }
        }
        var outcome = ProcessManager.KillByPort(port);
        if (outcome.Success)
        {
            CliHelpers.Info($"killed; port {port} freed.");
            return 0;
        }
        CliHelpers.Error($"could not free port {port}: {outcome.Reason}");
        if (report.Evidence.IsService)
            CliHelpers.Info("(this is a Windows service — stop it with an elevated 'Stop-Service', " +
                            "or run AppShelf as administrator.)");
        return 1;
    }

    private static int RunTable(ConfigStore store)
    {
        var apps = store.Load().Apps;
        var reports = new PortDoctor().Scan(apps, new HashSet<int>());
        if (reports.Count == 0) { CliHelpers.Info("No dev-server ports in use."); return 0; }
        PrintTable(reports);
        return 0;
    }

    private static void PrintTable(IReadOnlyList<PortReport> reports)
    {
        if (reports.Count == 0)
        {
            Console.WriteLine("No dev-server ports in use.");
            return;
        }
        Console.WriteLine("PORT   PID     PROCESS         TIER            SVC  OWNER");
        foreach (var r in reports)
            Console.WriteLine($"{r.Port,-6} {r.Evidence.Pid,-7} {Trunc(r.Evidence.ProcessName, 15),-15} " +
                              $"{r.Tier,-15} {(r.Evidence.IsService ? "yes" : "-"),-4} {r.OwnerAppName ?? "-"}");
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
