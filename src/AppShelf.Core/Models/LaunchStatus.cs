namespace AppShelf.Core.Models;

/// <summary>
/// Runtime-only status of an app. Never persisted to config.json — the GUI derives it
/// live and the CLI reports it on demand (spec §2).
/// </summary>
public enum LaunchStatus
{
    Stopped,
    Starting,
    Running,
    Error,
    PortInUse,
}
