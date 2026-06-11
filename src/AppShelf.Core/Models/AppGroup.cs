namespace AppShelf.Core.Models;

/// <summary>A project group: a shared label and its role-ordered members.</summary>
public sealed record AppGroup(string Name, IReadOnlyList<AppEntry> Members);
