using System.Text.Json;
using AppShelf.Core.Models;

namespace AppShelf.Core.Config;

/// <summary>
/// Reads and writes the single source of truth, config.json (spec §3.1).
///
/// - <see cref="Load"/> creates an empty config (and its directory) when missing.
/// - <see cref="Save"/> writes atomically (temp file then replace) so a crash mid-write
///   never corrupts the config.
/// - Writes are camelCase + indented so the file stays hand-editable.
/// - Reads/writes retry briefly when the file is locked (CLI and GUI may both write).
/// </summary>
public sealed class ConfigStore
{
    private const int RetryAttempts = 5;
    private const int RetryDelayMs = 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public string FilePath { get; }

    public ConfigStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultPath;
    }

    /// <summary>
    /// %APPDATA%/AppShelf/config.json — or the path in the APPSHELF_CONFIG environment
    /// variable when set (lets the config be relocated / isolated for testing).
    /// </summary>
    public static string DefaultPath =>
        Environment.GetEnvironmentVariable("APPSHELF_CONFIG") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AppShelf",
                "config.json");

    public AppConfig Load()
    {
        if (!File.Exists(FilePath))
        {
            var empty = new AppConfig();
            Save(empty);
            return empty;
        }

        var json = WithRetry(() => File.ReadAllText(FilePath));
        if (string.IsNullOrWhiteSpace(json))
            return new AppConfig();

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"config.json at '{FilePath}' is not valid JSON. Fix or delete it and try again. ({ex.Message})", ex);
        }
    }

    /// <summary>Atomic write: serialize to a temp file in the same directory, then replace.</summary>
    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tempPath = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");

        WithRetry(() =>
        {
            File.WriteAllText(tempPath, json);
            try
            {
                if (File.Exists(FilePath))
                    File.Replace(tempPath, FilePath, destinationBackupFileName: null);
                else
                    File.Move(tempPath, FilePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* temp cleanup is best-effort */ }
                }
            }
        });
    }

    /// <summary>
    /// Appends a new app. Derives <see cref="AppEntry.Id"/> from the name when empty, stamps
    /// <see cref="AppEntry.CreatedAt"/> when unset, and rejects duplicate ids.
    /// </summary>
    public AppEntry AddApp(AppEntry entry)
    {
        var config = Load();

        if (string.IsNullOrWhiteSpace(entry.Id))
            entry.Id = Slug.From(entry.Name);

        if (config.Apps.Any(a => string.Equals(a.Id, entry.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"An app with id '{entry.Id}' already exists.");

        if (entry.CreatedAt == default)
            entry.CreatedAt = DateTimeOffset.UtcNow;

        config.Apps.Add(entry);
        Save(config);
        return entry;
    }

    /// <summary>Removes an app by id. Returns false (no save) when the id is not present.</summary>
    public bool RemoveApp(string id)
    {
        var config = Load();
        var removed = config.Apps.RemoveAll(a => string.Equals(a.Id, id, StringComparison.Ordinal)) > 0;
        if (removed)
            Save(config);
        return removed;
    }

    /// <summary>Replaces an existing app (matched by id). Throws when the id is not present.</summary>
    public void UpdateApp(AppEntry entry)
    {
        var config = Load();
        var index = config.Apps.FindIndex(a => string.Equals(a.Id, entry.Id, StringComparison.Ordinal));
        if (index < 0)
            throw new InvalidOperationException($"No app with id '{entry.Id}' exists to update.");

        config.Apps[index] = entry;
        Save(config);
    }

    // --- polite file-locking: retry briefly, then surface a clear error ---

    private static T WithRetry<T>(Func<T> action)
    {
        IOException? last = null;
        for (var attempt = 0; attempt < RetryAttempts; attempt++)
        {
            try { return action(); }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(RetryDelayMs);
            }
        }

        throw new InvalidOperationException(
            "config.json is locked by another process (the AppShelf app or another CLI run). " +
            "Close it and try again.", last);
    }

    private static void WithRetry(Action action) =>
        WithRetry<object?>(() => { action(); return null; });
}
