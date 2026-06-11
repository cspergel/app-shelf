using System.Text.Json.Serialization;

namespace AppShelf.Core.Models;

/// <summary>
/// A single registered app. Persisted to config.json (spec §2).
///
/// - URL-only app: <see cref="Cmd"/> is null/empty — just opens <see cref="Url"/>.
/// - Local app: <see cref="Cmd"/> is present — run <see cref="Cmd"/> in <see cref="Dir"/>,
///   poll <see cref="Url"/>, then open it.
/// </summary>
public sealed class AppEntry
{
    /// <summary>Slug, unique. Derived from <see cref="Name"/> if not supplied.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Working directory for local apps; null for pure URL apps.</summary>
    public string? Dir { get; set; }

    /// <summary>Launch command; null for pure URL apps.</summary>
    public string? Cmd { get; set; }

    /// <summary>URL to open once running (or the URL for a URL-only app).</summary>
    public string Url { get; set; } = "";

    /// <summary>Optional install command, e.g. "npm install".</summary>
    public string? InstallCmd { get; set; }

    /// <summary>
    /// Framework detected at add time (e.g. "Vite", "Next.js", "Streamlit"), or null when
    /// added manually. Used to build correct port/host launch flags for the port registry.
    /// </summary>
    public string? Framework { get; set; }

    /// <summary>Free-form tags.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Favorite flag. Default false.</summary>
    public bool Favorite { get; set; }

    /// <summary>"browser" | "webview". v0 only honors "browser".</summary>
    public string Open { get; set; } = OpenTargets.Browser;

    /// <summary>Optional project group label. Apps sharing a non-empty value render as one
    /// group card. Null/empty = ungrouped (renders standalone, as before).</summary>
    public string? Group { get; set; }

    /// <summary>Role within a group: drives Start-all order and the group's Open target.
    /// One of <see cref="AppRoles"/>. Default "other".</summary>
    public string Role { get; set; } = AppRoles.Other;

    /// <summary>Optional manual sort position within a group, among members of the same
    /// <see cref="Role"/>. Null = unordered (sorts after ordered members, then by name).
    /// Backward-compatible: absent in older configs.</summary>
    public int? Order { get; set; }

    /// <summary>
    /// Reserved port for this project (port registry). Source of truth for the port the app
    /// binds to; <see cref="Url"/> is kept consistent with it. Null = no reservation yet.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// When true, the reserved <see cref="Port"/> is pinned by the user and must never be
    /// auto-reassigned by the allocator. Default false.
    /// </summary>
    public bool PortFixed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLaunchedAt { get; set; }

    /// <summary>True when this entry just opens a URL (no command to run).</summary>
    [JsonIgnore]
    public bool IsUrlOnly => string.IsNullOrWhiteSpace(Cmd);

    /// <summary>True when this entry runs a local command before opening its URL.</summary>
    [JsonIgnore]
    public bool IsLocal => !IsUrlOnly;
}

/// <summary>Allowed values for <see cref="AppEntry.Open"/>.</summary>
public static class OpenTargets
{
    public const string Browser = "browser";
    public const string WebView = "webview";
}

/// <summary>Allowed values for <see cref="AppEntry.Role"/>.</summary>
public static class AppRoles
{
    public const string Backend = "backend";
    public const string Frontend = "frontend";
    public const string Other = "other";

    /// <summary>Start order: backends first, then frontends, then everything else.</summary>
    public static int Order(string role) => role switch
    {
        Backend => 0,
        Frontend => 1,
        _ => 2,
    };
}
