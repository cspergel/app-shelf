using AppShelf.Core.Config;
using AppShelf.Core.Models;

namespace AppShelf.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "appshelf-tests", Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "config.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    private ConfigStore NewStore() => new(_path);

    private static AppEntry Entry(string name, string? cmd = null, string url = "http://localhost:5173") =>
        new() { Name = name, Cmd = cmd, Dir = cmd is null ? null : "C:/projects/x", Url = url };

    [Fact]
    public void Load_WhenFileMissing_ReturnsEmptyConfigAndCreatesFile()
    {
        var store = NewStore();

        var config = store.Load();

        Assert.Equal(1, config.Version);
        Assert.Empty(config.Apps);
        Assert.True(File.Exists(_path), "Load should create the config file when missing.");
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsAllFields()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.Apps.Add(new AppEntry
        {
            Id = "my-app",
            Name = "My App",
            Dir = "C:/projects/my-app",
            Cmd = "npm run dev",
            Url = "http://localhost:5173",
            InstallCmd = "npm install",
            Tags = new() { "seo", "web" },
            Favorite = true,
            Open = OpenTargets.Browser,
            Port = 5173,
            PortFixed = true,
            CreatedAt = DateTimeOffset.Parse("2026-06-06T00:00:00Z"),
        });

        store.Save(config);
        var loaded = store.Load();

        var app = Assert.Single(loaded.Apps);
        Assert.Equal("my-app", app.Id);
        Assert.Equal("npm run dev", app.Cmd);
        Assert.Equal("http://localhost:5173", app.Url);
        Assert.Equal(new[] { "seo", "web" }, app.Tags);
        Assert.True(app.Favorite);
        Assert.Equal(5173, app.Port);
        Assert.True(app.PortFixed);
    }

    [Fact]
    public void Save_WritesCamelCaseIndentedJson()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.Apps.Add(Entry("Test", cmd: "npm run dev"));

        store.Save(config);
        var json = File.ReadAllText(_path);

        Assert.Contains("\"apps\"", json);     // camelCase, not "Apps"
        Assert.Contains("\"version\"", json);
        Assert.Contains("\n", json);            // indented / multi-line, hand-editable
    }

    [Fact]
    public void Save_DoesNotSerializeComputedProperties()
    {
        var store = NewStore();
        store.AddApp(Entry("Test", cmd: "npm run dev"));

        var json = File.ReadAllText(_path);

        Assert.DoesNotContain("isUrlOnly", json);
        Assert.DoesNotContain("isLocal", json);
    }

    [Fact]
    public void AddApp_DerivesIdFromName_WhenIdEmpty()
    {
        var store = NewStore();

        var added = store.AddApp(Entry("My Cool App", cmd: "npm run dev"));

        Assert.Equal("my-cool-app", added.Id);
        Assert.Single(store.Load().Apps);
    }

    [Fact]
    public void AddApp_SetsCreatedAt_WhenUnset()
    {
        var store = NewStore();

        var added = store.AddApp(Entry("Test", cmd: "npm run dev"));

        Assert.NotEqual(default, added.CreatedAt);
    }

    [Fact]
    public void AddApp_DuplicateId_Throws()
    {
        var store = NewStore();
        store.AddApp(new AppEntry { Id = "dup", Name = "Dup", Url = "http://x", Cmd = "c", Dir = "d" });

        Assert.Throws<InvalidOperationException>(() =>
            store.AddApp(new AppEntry { Id = "dup", Name = "Other", Url = "http://y", Cmd = "c", Dir = "d" }));
    }

    [Fact]
    public void AddApp_PersistsAcrossStoreInstances()
    {
        NewStore().AddApp(Entry("Persisted", cmd: "npm run dev"));

        var reloaded = NewStore().Load();

        Assert.Single(reloaded.Apps);
        Assert.Equal("persisted", reloaded.Apps[0].Id);
    }

    [Fact]
    public void RemoveApp_RemovesById_AndReturnsTrue()
    {
        var store = NewStore();
        store.AddApp(new AppEntry { Id = "gone", Name = "Gone", Url = "http://x", Cmd = "c", Dir = "d" });

        var removed = store.RemoveApp("gone");

        Assert.True(removed);
        Assert.Empty(store.Load().Apps);
    }

    [Fact]
    public void RemoveApp_Unknown_ReturnsFalse()
    {
        var store = NewStore();

        Assert.False(store.RemoveApp("nope"));
    }

    [Fact]
    public void UpdateApp_ReplacesById()
    {
        var store = NewStore();
        store.AddApp(new AppEntry { Id = "app", Name = "App", Url = "http://x", Cmd = "old", Dir = "d" });

        store.UpdateApp(new AppEntry { Id = "app", Name = "App", Url = "http://x", Cmd = "new", Dir = "d", Port = 4000 });

        var app = Assert.Single(store.Load().Apps);
        Assert.Equal("new", app.Cmd);
        Assert.Equal(4000, app.Port);
    }

    [Fact]
    public void UpdateApp_Unknown_Throws()
    {
        var store = NewStore();

        Assert.Throws<InvalidOperationException>(() =>
            store.UpdateApp(new AppEntry { Id = "missing", Name = "X", Url = "http://x", Cmd = "c", Dir = "d" }));
    }

    [Fact]
    public void Load_ToleratesCaseInsensitiveKeys_ForHandEditing()
    {
        // A hand-edited file using PascalCase keys should still load.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ \"Version\": 1, \"Apps\": [ { \"Id\": \"x\", \"Name\": \"X\", \"Url\": \"http://x\" } ] }");

        var loaded = NewStore().Load();

        Assert.Single(loaded.Apps);
        Assert.Equal("x", loaded.Apps[0].Id);
    }

    /// <summary>Phase 3 B3: a write that hits an access denial (here a read-only target file) must
    /// surface a readable "Access denied" message — NOT the misleading "locked by another process"
    /// retry message, and NOT a raw UnauthorizedAccessException. UnauthorizedAccessException does
    /// not inherit from IOException, so WithRetry must catch it explicitly.</summary>
    [Fact]
    public void Save_ToReadOnlyPath_ThrowsInvalidOperationExceptionWithAccessDeniedMessage()
    {
        var store = NewStore();
        // Create the config first (writable), then mark it read-only so the next Save's
        // File.Replace / File.Move onto it is denied.
        store.Save(new AppConfig());
        File.SetAttributes(_path, FileAttributes.ReadOnly);

        try
        {
            var config = new AppConfig();
            config.Apps.Add(Entry("Blocked", cmd: "npm run dev"));

            var ex = Assert.Throws<InvalidOperationException>(() => store.Save(config));
            Assert.Contains("Access denied", ex.Message);
            Assert.IsType<UnauthorizedAccessException>(ex.InnerException);
        }
        finally
        {
            // Clear the read-only bit so the Dispose cleanup can delete the temp directory.
            File.SetAttributes(_path, FileAttributes.Normal);
        }
    }

    /// <summary>Phase 3 C4: the atomic-regroup rewrite batches all changes into ONE
    /// <see cref="ConfigStore.Save"/>. This validates the primitive that makes that safe — mutating
    /// several apps in a loaded config and saving once persists every change (the old per-app
    /// UpdateApp loop did N independent saves that could interleave under contention).</summary>
    [Fact]
    public void Save_BatchUpdateOfMultipleApps_PersistsAllChangesAtomically()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.Apps.Add(new AppEntry { Id = "a", Name = "A", Url = "http://a", Cmd = "c", Dir = "d" });
        config.Apps.Add(new AppEntry { Id = "b", Name = "B", Url = "http://b", Cmd = "c", Dir = "d" });
        config.Apps.Add(new AppEntry { Id = "c", Name = "C", Url = "http://c", Cmd = "c", Dir = "d" });
        store.Save(config);

        // Mutate all three in-memory (the shape ApplyRegroup uses), then save ONCE.
        var loaded = store.Load();
        foreach (var app in loaded.Apps)
        {
            app.Group = "team";
            app.Role = AppRoles.Backend;
            app.Order = 1;
        }
        store.Save(loaded);

        var reloaded = store.Load();
        Assert.Equal(3, reloaded.Apps.Count);
        Assert.All(reloaded.Apps, a =>
        {
            Assert.Equal("team", a.Group);
            Assert.Equal(AppRoles.Backend, a.Role);
            Assert.Equal(1, a.Order);
        });
    }
}
