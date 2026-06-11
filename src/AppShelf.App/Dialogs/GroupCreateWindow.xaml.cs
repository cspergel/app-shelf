using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using AppShelf.App.Services;
using AppShelf.App.ViewModels;
using AppShelf.Core.Models;

namespace AppShelf.App.Dialogs;

/// <summary>Smart-defaulted "name this group" dialog: a pre-filled (focused, select-all) name box
/// plus a role combo per member. OK returns the chosen name + per-app roles; Cancel aborts the
/// drop.</summary>
public partial class GroupCreateWindow : Window
{
    private readonly ObservableCollection<MemberRow> _rows = new();

    public GroupCreateWindow(string suggestedName, IReadOnlyList<GroupMemberSeed> members)
    {
        InitializeComponent();

        NameBox.Text = suggestedName;
        foreach (var m in members)
            _rows.Add(new MemberRow(m.AppId, m.Name, m.Role));

        MembersList.ItemsSource = _rows;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>Populated on OK; null until then.</summary>
    public GroupCreateResult? Result { get; private set; }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = _rows.FirstOrDefault()?.Name.Trim() + " group";

        var roles = _rows.ToDictionary(r => r.AppId, r => r.Role);
        Result = new GroupCreateResult(name!, roles);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>One editable member row: fixed name + selectable role.</summary>
    private sealed class MemberRow : ObservableObject
    {
        private string _role;

        public MemberRow(string appId, string name, string role)
        {
            AppId = appId;
            Name = name;
            _role = NormalizeRole(role);
        }

        public string AppId { get; }
        public string Name { get; }

        public string[] RoleOptions { get; } =
            { AppRoles.Backend, AppRoles.Frontend, AppRoles.Other };

        public string Role
        {
            get => _role;
            set => SetField(ref _role, value);
        }

        private static string NormalizeRole(string role) => role switch
        {
            AppRoles.Backend => AppRoles.Backend,
            AppRoles.Frontend => AppRoles.Frontend,
            _ => AppRoles.Other,
        };
    }
}
