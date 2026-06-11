namespace AppShelf.Core.Models;

/// <summary>
/// Root config object persisted to %APPDATA%/AppShelf/config.json (spec §2).
/// </summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    public List<AppEntry> Apps { get; set; } = new();
}
