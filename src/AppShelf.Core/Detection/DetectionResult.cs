namespace AppShelf.Core.Detection;

/// <summary>
/// Best-effort result of inferring how to launch a project from its folder (spec §3.2).
/// Returned by <c>StackDetector.Detect</c>; null when nothing matches (caller prompts).
/// <see cref="Dir"/> is the directory the command should run in — usually the folder passed
/// to Detect, but a subfolder (e.g. <c>frontend/</c>) when the project is split.
/// </summary>
public sealed record DetectionResult(
    string Cmd,
    string Url,
    string? InstallCmd,
    string Framework,
    string Dir);
