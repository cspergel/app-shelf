using AppShelf.Core.Launch;
using Xunit;

namespace AppShelf.Core.Tests;

/// <summary>Shell-injection-safety coverage for <see cref="QuickActions"/> (Phase 3 A1). The
/// runtime launch boundary (<c>Process.Start</c>) is not exercised here — instead the extracted
/// <see cref="QuickActions.BuildTerminalCandidates"/> / <see cref="QuickActions.BuildEditorStartInfo"/>
/// helpers are inspected to prove a hostile directory value is NEVER interpolated into a shell
/// command string. These are <c>internal</c> helpers reached via <c>InternalsVisibleTo</c>.</summary>
public class QuickActionsTests
{
    private const string MaliciousDir = "C:\\proj\" && calc.exe && echo \"x";
    private const string QuoteBacktickDir = "C:\\Users\\Craig's `whoami` Apps\\proj";

    [Fact]
    public void OpenTerminal_WithDirContainingQuotes_ReturnsOkOrFailSafely()
    {
        // The wt.exe candidate must carry the directory as discrete ArgumentList tokens — never as
        // a spliced command string. An injection payload appears verbatim as one argument, not as
        // a separate `&& calc.exe` command.
        var candidates = QuickActions.BuildTerminalCandidates(MaliciousDir);

        var wt = candidates[0];
        Assert.Equal("wt.exe", wt.FileName);
        Assert.Equal(new[] { "-d", MaliciousDir }, wt.ArgumentList);
        Assert.Equal(MaliciousDir, wt.WorkingDirectory);
        // No raw Arguments string => nothing for a shell to re-parse and inject.
        Assert.Equal(string.Empty, wt.Arguments);
    }

    [Fact]
    public void OpenTerminal_WithDirContainingSingleQuoteAndBacktick_DoesNotInterpolateIntoArgs()
    {
        var candidates = QuickActions.BuildTerminalCandidates(QuoteBacktickDir);

        // PowerShell and cmd candidates open in the directory with NO command — the directory is
        // only ever a WorkingDirectory, so a single-quote / backtick cannot escape into a command.
        var powershell = candidates[1];
        Assert.Equal("powershell.exe", powershell.FileName);
        Assert.Equal(string.Empty, powershell.Arguments);
        Assert.Empty(powershell.ArgumentList);
        Assert.Equal(QuoteBacktickDir, powershell.WorkingDirectory);

        var cmd = candidates[2];
        Assert.Equal("cmd.exe", cmd.FileName);
        Assert.Equal(string.Empty, cmd.Arguments);
        Assert.Empty(cmd.ArgumentList);
        Assert.Equal(QuoteBacktickDir, cmd.WorkingDirectory);

        // wt.exe still carries the backtick/quote value as a discrete token, not a command.
        Assert.Equal(new[] { "-d", QuoteBacktickDir }, candidates[0].ArgumentList);
    }

    [Theory]
    [InlineData("code && del C:\\")]
    [InlineData("code\" & calc")]
    [InlineData("code | whoami")]
    [InlineData("code; rm -rf")]
    [InlineData("code`whoami`")]
    public void OpenEditor_WithDirContainingQuotes_ReturnsFailForInvalidEditor(string editor)
    {
        // A directory that exists is required to reach the editor-name validation. Use the temp dir.
        var result = QuickActions.OpenEditor(System.IO.Path.GetTempPath(), editor);

        Assert.False(result.Success);
        Assert.NotNull(result.Reason);
        Assert.Contains("aren't allowed", result.Reason!);
        Assert.False(QuickActions.IsSafeEditorName(editor));
    }

    [Fact]
    public void OpenEditor_WithValidEditorAndSafePath_DoesNotInterpolateDir()
    {
        const string dir = "C:\\proj\" && calc.exe";

        // A safe editor name passes validation; the resulting start info opens "." in the directory
        // and never splices the (hostile) directory value into the cmd /c argument string.
        Assert.True(QuickActions.IsSafeEditorName("code"));

        var psi = QuickActions.BuildEditorStartInfo(dir, "code");

        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Equal("/c code .", psi.Arguments);
        Assert.DoesNotContain("calc.exe", psi.Arguments);
        Assert.Equal(dir, psi.WorkingDirectory);
    }
}
