using AppShelf.Core.Config;
using AppShelf.Core.Detection;
using AppShelf.Core.Models;
using AppShelf.Core.Process;

namespace AppShelf.Cli.Commands;

/// <summary>
/// `appshelf add` (spec §4). Detects the stack in a folder (or takes explicit flags), assigns
/// a clash-free reserved port from the registry, and appends to config.json.
/// </summary>
public static class AddCommand
{
    public static int Run(ConfigStore store, ArgMap args)
    {
        var config = store.Load();

        var isUrlOnly = args.Has("url-only") ||
                        (args.Has("url") && !args.Has("cmd") && !args.Has("dir"));

        if (!TryResolveRole(args, out _))
            return 1;

        var entry = isUrlOnly ? BuildUrlOnly(args) : BuildLocal(args, config);
        if (entry is null)
            return 1;

        var added = store.AddApp(entry);

        CliHelpers.Info($"Added '{added.Name}' ({added.Id}):");
        CliHelpers.Info($"  type:   {(added.IsUrlOnly ? "URL" : "Local")}");
        if (added.IsLocal)
        {
            CliHelpers.Info($"  dir:    {added.Dir}");
            CliHelpers.Info($"  cmd:    {added.Cmd}");
            if (added.Framework is not null) CliHelpers.Info($"  stack:  {added.Framework}");
        }
        CliHelpers.Info($"  url:    {added.Url}");
        if (added.Port is int p) CliHelpers.Info($"  port:   {p}{(added.PortFixed ? " (fixed)" : " (reserved)")}");
        if (added.Group is not null) CliHelpers.Info($"  group:  {added.Group} ({added.Role})");
        return 0;
    }

    private static AppEntry? BuildUrlOnly(ArgMap args)
    {
        var url = args.Get("url");
        if (string.IsNullOrWhiteSpace(url))
        {
            CliHelpers.Error("URL-only app needs --url <URL>.");
            return null;
        }

        var name = args.Get("name");
        if (string.IsNullOrWhiteSpace(name))
            name = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;

        return new AppEntry
        {
            Name = name,
            Url = url,
            Cmd = null,
            Dir = null,
            Open = args.Get("open") ?? OpenTargets.Browser,
            Tags = ParseTags(args.Get("tags")),
            Favorite = args.Has("favorite"),
            Group = ResolveGroup(args),
            Role = ResolveRole(args),
        };
    }

    private static AppEntry? BuildLocal(ArgMap args, AppConfig config)
    {
        var dir = Path.GetFullPath(args.Get("dir") ?? Directory.GetCurrentDirectory());
        var cmd = args.Get("cmd");
        var url = args.Get("url");
        var install = args.Get("install");
        string? framework = null;

        var runDir = dir;

        if (string.IsNullOrWhiteSpace(cmd))
        {
            var detected = new StackDetector().Detect(dir);
            if (detected is null)
            {
                CliHelpers.Error($"no known framework detected in '{dir}'. " +
                                 "Re-run with explicit flags: --cmd \"<command>\" --url <url>.");
                return null;
            }

            cmd = detected.Cmd;
            url ??= detected.Url;
            install ??= detected.InstallCmd;
            framework = detected.Framework;
            runDir = detected.Dir; // may be a subfolder (e.g. frontend/)

            var where = string.Equals(detected.Dir, dir, StringComparison.OrdinalIgnoreCase)
                ? ""
                : $" in {Path.GetFileName(detected.Dir)}/";
            CliHelpers.Info($"Detected {detected.Framework}{where}: cmd='{detected.Cmd}', url='{detected.Url}'.");
            if (!Confirm(args))
            {
                CliHelpers.Info("Cancelled. Re-run with explicit --cmd/--url to override detection.");
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            CliHelpers.Error("a local app needs a --url (or a detectable framework).");
            return null;
        }

        var name = args.Get("name");
        if (string.IsNullOrWhiteSpace(name))
            name = new DirectoryInfo(dir).Name; // name from the project root, not the subfolder

        var entry = new AppEntry
        {
            Name = name,
            Dir = runDir,
            Cmd = cmd,
            Url = url,
            InstallCmd = install,
            Framework = framework,
            Open = args.Get("open") ?? OpenTargets.Browser,
            Tags = ParseTags(args.Get("tags")),
            Favorite = args.Has("favorite"),
            Group = ResolveGroup(args),
            Role = ResolveRole(args),
        };

        AssignPort(entry, args, config);
        return entry;
    }

    /// <summary>Reserve a clash-free port (port registry): explicit --port, else allocate.</summary>
    private static void AssignPort(AppEntry entry, ArgMap args, AppConfig config)
    {
        var reserved = CliHelpers.ReservedPorts(config).ToList();

        if (args.Get("port") is { } raw && int.TryParse(raw, out var fixedPort))
        {
            entry.Port = fixedPort;
            entry.PortFixed = true;
        }
        else
        {
            var preferred = CliHelpers.PortFromUrl(entry.Url);
            entry.Port = new PortAllocator().Allocate(reserved, preferred);
        }

        entry.Url = CliHelpers.WithPort(entry.Url, entry.Port.Value);
    }

    private static bool Confirm(ArgMap args)
    {
        if (args.Has("yes") || Console.IsInputRedirected)
            return true; // non-interactive: accept detection

        Console.Write("Add this app? [Y/n] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is null or "" or "y" or "yes";
    }

    private static List<string> ParseTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? new List<string>()
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string? ResolveGroup(ArgMap args) =>
        string.IsNullOrWhiteSpace(args.Get("group")) ? null : args.Get("group")!.Trim();

    private static string ResolveRole(ArgMap args)
    {
        TryResolveRole(args, out var role);
        return role;
    }

    /// <summary>Validate --role against <see cref="AppRoles"/>; default "other". Returns false
    /// (and prints an error) on an unknown role.</summary>
    private static bool TryResolveRole(ArgMap args, out string role)
    {
        var raw = args.Get("role");
        if (string.IsNullOrWhiteSpace(raw))
        {
            role = AppRoles.Other;
            return true;
        }

        role = raw.Trim().ToLowerInvariant();
        if (role is AppRoles.Backend or AppRoles.Frontend or AppRoles.Other)
            return true;

        CliHelpers.Error($"unknown --role '{raw}'. Use backend, frontend, or other.");
        role = AppRoles.Other;
        return false;
    }
}
