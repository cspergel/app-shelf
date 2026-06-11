using AppShelf.Core.Models;

namespace AppShelf.App.Services;

/// <summary>Abstraction over the add/edit/confirm dialogs so the view model stays UI-free.</summary>
public interface IAppDialogs
{
    /// <summary>Show the add dialog. Returns true if an app was added.</summary>
    bool ShowAddDialog();

    /// <summary>Show the edit dialog for an existing app. Returns true if it was changed.</summary>
    bool ShowEditDialog(AppEntry existing);

    /// <summary>Yes/No confirmation. Returns true for Yes.</summary>
    bool Confirm(string message, string title);

    /// <summary>Show an informational message (e.g. a failed-kill reason).</summary>
    void Info(string message, string title);

    /// <summary>Show the create-group dialog with a pre-filled name and a role combo per member.
    /// Returns the chosen name + per-app roles, or null when the user cancels (aborting the drop).</summary>
    GroupCreateResult? PromptGroupCreate(
        string suggestedName, IReadOnlyList<GroupMemberSeed> members);
}

/// <summary>A prospective group member shown in the create dialog (app id + display name +
/// pre-selected role).</summary>
public sealed record GroupMemberSeed(string AppId, string Name, string Role);

/// <summary>The user's confirmed group-create choices: the group name and the final role per app.</summary>
public sealed record GroupCreateResult(string Name, IReadOnlyDictionary<string, string> RolesByAppId);
