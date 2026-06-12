namespace AppShelf.Core.Models;

/// <summary>
/// Root config object persisted to %APPDATA%/AppShelf/config.json (spec §2).
/// </summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// The global hotkey for the Spotlight overlay as a human string (e.g. "Ctrl+Alt+Space").
    /// <c>null</c> or empty means "use the built-in default chain" (Alt+Space then Ctrl+Alt+Space).
    /// </summary>
    public string? Hotkey { get; set; }

    public List<AppEntry> Apps { get; set; } = new();
}
