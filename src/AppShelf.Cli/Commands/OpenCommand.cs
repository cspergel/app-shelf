using System.Diagnostics;

namespace AppShelf.Cli.Commands;

/// <summary>`appshelf open` — launch the AppShelf desktop app (spec §4).</summary>
public static class OpenCommand
{
    public static int Run()
    {
        var exe = FindDesktopApp();
        if (exe is null)
        {
            CliHelpers.Info(
                "Couldn't find the AppShelf desktop app (AppShelf.exe).\n" +
                "Build it:  dotnet build src/AppShelf.App\n" +
                "or run it: dotnet run --project src/AppShelf.App\n\n" +
                "Meanwhile you can manage everything from the terminal:\n" +
                "  appshelf add | list | launch | stop | rm | port | ports | doctor");
            return 1;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            CliHelpers.Info("Launching AppShelf…");
            return 0;
        }
        catch (Exception ex)
        {
            CliHelpers.Error($"Could not launch AppShelf: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Finds the desktop exe: first next to this CLI (the published side-by-side layout), then —
    /// for dev convenience — by walking up to the repo root and probing the App project's build
    /// output. Returns null if nothing is found.
    /// </summary>
    private static string? FindDesktopApp()
    {
        var sideBySide = Path.Combine(AppContext.BaseDirectory, "AppShelf.exe");
        if (File.Exists(sideBySide))
            return sideBySide;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var appProject = Path.Combine(dir.FullName, "src", "AppShelf.App");
            if (Directory.Exists(appProject))
            {
                return Directory.EnumerateFiles(appProject, "AppShelf.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
        }

        return null;
    }
}
