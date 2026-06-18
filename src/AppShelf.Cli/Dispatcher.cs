using AppShelf.Cli.Commands;
using AppShelf.Core.Config;

namespace AppShelf.Cli;

/// <summary>Routes the command word to a command handler (spec §4). All logic lives in Core.</summary>
public static class Dispatcher
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var rest = ArgMap.Parse(args[1..]);
        var store = new ConfigStore();

        try
        {
            return command switch
            {
                "add" => AddCommand.Run(store, rest),
                "list" or "ls" => ListCommand.Run(store, rest),
                "launch" or "start" => await LaunchCommand.RunAsync(store, rest),
                "stop" => StopCommand.Run(store, rest),
                "rm" or "remove" => RemoveCommand.Run(store, rest),
                "port" => PortCommand.RunPort(store, rest),
                "ports" => PortCommand.RunPorts(store, rest),
                "doctor" => await DoctorCommand.RunAsync(store, rest),
                "open" => OpenCommand.Run(),
                "help" or "--help" or "-h" => PrintUsage(),
                _ => Unknown(command),
            };
        }
        catch (InvalidOperationException ex)
        {
            // Readable, expected failures (bad dir, dup id, locked config, etc.) — spec §10.
            CliHelpers.Error(ex.Message);
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        CliHelpers.Error($"unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static int PrintUsage()
    {
        CliHelpers.Info(
            """
            appshelf — your personal local app launcher

            Usage:
              appshelf add [--dir <path>] [--name <name>] [--cmd <command>] [--url <url>]
                           [--install <cmd>] [--tags a,b] [--open browser|webview] [--port <n>]
                           [--group <name>] [--role backend|frontend|other] [--favorite] [--yes]
              appshelf add --url <URL> --name <name>      add a URL-only app
              appshelf list                               list apps (name | type | status | port | url)
              appshelf launch <id|name>                   launch an app and open its target
              appshelf stop <id|name>                     stop a running app (frees its port)
              appshelf rm <id|name>                       remove an app
              appshelf port <id|name> [--set <n> | --auto]   view or assign a reserved port
              appshelf ports                              list port reservations and conflicts
              appshelf doctor [--kill <port> [--yes]]     scan live dev ports, classify, free a port
              appshelf open                               launch the desktop app
            """);
        return 0;
    }
}
