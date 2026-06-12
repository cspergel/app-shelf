using System.Windows;
using AppShelf.App.Dialogs;
using AppShelf.Core.Models;

namespace AppShelf.App.Services;

/// <summary>WPF implementation of <see cref="IAppDialogs"/> — opens modal windows owned by
/// the main window and routes confirmations through a message box.</summary>
public sealed class DialogService : IAppDialogs
{
    private readonly GuiAppService _service;

    public DialogService(GuiAppService service) => _service = service;

    public bool ShowAddDialog() => ShowEditor(existing: null);

    public bool ShowEditDialog(AppEntry existing) => ShowEditor(existing);

    private bool ShowEditor(AppEntry? existing)
    {
        var window = new AppEditWindow(_service, existing) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true;
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Info(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? PromptRename(string title, string label, string initial)
    {
        var window = new RenameWindow(title, label, initial) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Result : null;
    }

    public GroupCreateResult? PromptGroupCreate(string suggestedName, IReadOnlyList<GroupMemberSeed> members)
    {
        var window = new GroupCreateWindow(suggestedName, members) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Result : null;
    }
}
