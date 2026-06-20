using AppShelf.Core.Models;

namespace AppShelf.App.Services;

/// <summary>
/// Null-object stub for <see cref="IAppDialogs"/>. The WPF add/edit/confirm/rename/group-create
/// dialogs were removed in the WebView2 cutover — all user-facing dialogs now live in the
/// React/Tailwind web UI and are driven directly through the JS↔C# bridge.
///
/// The remaining view models (<see cref="ViewModels.MainViewModel"/> and friends) are kept only
/// for the crash-watcher path; their dialog call-sites are dead code (no WPF card grid binds them).
/// This stub keeps those call-sites compiling as harmless no-ops without rewiring the VM
/// constructors. Each method returns a safe default (no dialog is ever shown).
/// </summary>
public sealed class DialogService : IAppDialogs
{
    public DialogService(GuiAppService service)
    {
        // Service no longer needed (no dialogs to construct), but the ctor signature is kept so
        // App.xaml.cs wiring is unchanged.
        _ = service;
    }

    public bool ShowAddDialog() => false;

    public bool ShowEditDialog(AppEntry existing) => false;

    public bool Confirm(string message, string title) => false;

    public void Info(string message, string title)
    {
        // No-op: errors/info now surface as web UI toasts via the bridge.
    }

    public string? PromptRename(string title, string label, string initial) => null;

    public GroupCreateResult? PromptGroupCreate(
        string suggestedName, IReadOnlyList<GroupMemberSeed> members) => null;
}
