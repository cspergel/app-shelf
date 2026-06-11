using AppShelf.Core.Models;

namespace AppShelf.Core.Launch;

/// <summary>
/// The effective command and environment for launching a local app, after the port registry
/// applies the reserved port and loopback binding.
/// </summary>
public sealed record LaunchPlan(string Command, IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// Builds a <see cref="LaunchPlan"/> for an app (port registry "bind &amp; secure" step).
///
/// When the app has a reserved <see cref="AppEntry.Port"/>, the command/env are augmented so
/// the dev server binds to that exact port on loopback (127.0.0.1) — keeping ports clash-free
/// and off the LAN. The mechanism is framework-specific because dev servers disagree on how
/// they take a port: most Node CLIs use flags, CRA/Flask/Gradio read environment variables.
/// Unknown frameworks fall back to the widely-honored PORT/HOST env vars and leave the command
/// untouched, so a launch is never broken by guessing a flag.
/// </summary>
public static class LaunchPlanBuilder
{
    public const string Loopback = "127.0.0.1";

    public static LaunchPlan Build(AppEntry entry)
    {
        var cmd = entry.Cmd ?? "";
        var env = new Dictionary<string, string>(StringComparer.Ordinal);

        if (entry.Port is not int port)
            return new LaunchPlan(cmd, env);

        var framework = entry.Framework?.Trim();

        switch (framework)
        {
            case "Vite":
            case "SvelteKit":
            case "Astro":
            case "Angular":
                cmd = AppendNpmArgs(cmd, $"--port {port} --host {Loopback}");
                break;

            case "Next.js":
                cmd = AppendNpmArgs(cmd, $"--port {port} --hostname {Loopback}");
                break;

            case "CRA":
                env["PORT"] = port.ToString();
                env["HOST"] = Loopback;
                break;

            case "Streamlit":
                cmd = $"{cmd} --server.port {port} --server.address {Loopback}";
                break;

            case "FastAPI":
                cmd = $"{cmd} --port {port} --host {Loopback}";
                break;

            case "Flask":
                env["FLASK_RUN_PORT"] = port.ToString();
                env["FLASK_RUN_HOST"] = Loopback;
                break;

            case "Gradio":
                env["GRADIO_SERVER_PORT"] = port.ToString();
                env["GRADIO_SERVER_NAME"] = Loopback;
                break;

            default:
                // Best-effort, non-invasive: many servers honor PORT/HOST; leave the command as-is.
                env["PORT"] = port.ToString();
                env["HOST"] = Loopback;
                break;
        }

        return new LaunchPlan(cmd, env);
    }

    /// <summary>Passes args through npm to the underlying script via the "--" separator.</summary>
    private static string AppendNpmArgs(string command, string args) => $"{command} -- {args}";
}
