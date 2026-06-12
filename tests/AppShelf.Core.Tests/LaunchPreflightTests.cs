using AppShelf.Core.Launch;
using AppShelf.Core.Models;

namespace AppShelf.Core.Tests;

public sealed class LaunchPreflightTests : IDisposable
{
    private readonly string _dir;

    public LaunchPreflightTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "appshelf-preflight", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private AppEntry Entry(string? dir, string? cmd, string? installCmd = null) => new()
    {
        Id = "test",
        Name = "Test",
        Dir = dir,
        Cmd = cmd,
        InstallCmd = installCmd,
        Url = "http://localhost:5173",
    };

    [Fact]
    public void UrlOnly_ReturnsNull()
    {
        var issue = LaunchPreflight.Check(Entry(dir: null, cmd: null));
        Assert.Null(issue);
    }

    [Fact]
    public void MissingFolder_ReportsAndCannotRunSetup()
    {
        var missing = Path.Combine(_dir, "does-not-exist");
        var issue = LaunchPreflight.Check(Entry(missing, "npm run dev", installCmd: "npm install"));

        Assert.NotNull(issue);
        Assert.Contains("Working folder not found", issue!.Reason);
        Assert.False(issue.CanRunSetup); // a moved/deleted folder is not setup-fixable
    }

    [Fact]
    public void ExplicitExePath_Missing_WithInstall_CanRunSetup()
    {
        var issue = LaunchPreflight.Check(
            Entry(_dir, @"venv\Scripts\python.exe app.py", installCmd: "python -m venv venv"));

        Assert.NotNull(issue);
        Assert.Contains("Not found", issue!.Reason);
        Assert.True(issue.CanRunSetup);
    }

    [Fact]
    public void ExplicitExePath_Missing_WithoutInstall_CannotRunSetup()
    {
        var issue = LaunchPreflight.Check(Entry(_dir, @"venv\Scripts\python.exe app.py"));

        Assert.NotNull(issue);
        Assert.Contains("Not found", issue!.Reason);
        Assert.False(issue.CanRunSetup);
    }

    [Fact]
    public void NodeCommand_MissingNodeModules_WithInstall_CanRunSetup()
    {
        var issue = LaunchPreflight.Check(Entry(_dir, "npm run dev", installCmd: "npm install"));

        Assert.NotNull(issue);
        Assert.Contains("node_modules", issue!.Reason);
        Assert.True(issue.CanRunSetup);
    }

    [Fact]
    public void NodeCommand_MissingNodeModules_WithoutInstall_CannotRunSetup()
    {
        var issue = LaunchPreflight.Check(Entry(_dir, "npm run dev"));

        Assert.NotNull(issue);
        Assert.Contains("node_modules", issue!.Reason);
        Assert.False(issue.CanRunSetup);
    }

    [Fact]
    public void NodeCommand_WithNodeModules_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "node_modules"));

        var issue = LaunchPreflight.Check(Entry(_dir, "npm run dev", installCmd: "npm install"));

        Assert.Null(issue);
    }

    [Fact]
    public void ExplicitExePath_Present_ReturnsNull()
    {
        var bin = Path.Combine(_dir, "venv", "Scripts");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "python.exe"), "");

        var issue = LaunchPreflight.Check(Entry(_dir, @"venv\Scripts\python.exe app.py"));

        Assert.Null(issue);
    }

    [Fact]
    public void NonPathNonNodeCommand_ReturnsNull()
    {
        // e.g. "streamlit run app.py" — not a path, not a Node launcher → no pre-flight block.
        var issue = LaunchPreflight.Check(Entry(_dir, "streamlit run app.py"));

        Assert.Null(issue);
    }
}
