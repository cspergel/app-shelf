using System.Windows;

namespace AppShelf.App.Dialogs;

/// <summary>Minimal single-field text prompt (used to rename a group). Mirrors
/// <see cref="GroupCreateWindow"/>'s pattern: the box is pre-filled, focused, and fully selected on
/// load; Enter confirms (OK is IsDefault) and Esc cancels (Cancel is IsCancel). <see cref="Result"/>
/// holds the trimmed text on OK.</summary>
public partial class RenameWindow : Window
{
    public RenameWindow(string title, string label, string initial)
    {
        InitializeComponent();

        Title = title;
        FieldLabel.Text = label;
        ValueBox.Text = initial;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    /// <summary>The trimmed entered text; populated on OK, null until then.</summary>
    public string? Result { get; private set; }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        Result = ValueBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
