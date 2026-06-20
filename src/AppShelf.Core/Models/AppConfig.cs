namespace AppShelf.Core.Models;

/// <summary>
/// Root config object persisted to %APPDATA%/AppShelf/config.json (spec §2).
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Config schema version marker (currently always 1). Reserved for future use.
    /// There is no migration system in v0 — this value exists so a future migration path
    /// can detect older files. Do not add migration logic here without creating a dedicated
    /// migration component first.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// The global hotkey for the Spotlight overlay as a human string (e.g. "Ctrl+Alt+Space").
    /// <c>null</c> or empty means "use the built-in default chain" (Alt+Space then Ctrl+Alt+Space).
    /// </summary>
    public string? Hotkey { get; set; }

    public List<AppEntry> Apps { get; set; } = new();
}
