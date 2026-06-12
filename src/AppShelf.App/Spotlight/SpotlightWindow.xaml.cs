using System.Windows;
using System.Windows.Input;
using AppShelf.App.ViewModels;

namespace AppShelf.App.Spotlight;

/// <summary>
/// The borderless, centered Spotlight overlay (spec §5.3). Never <c>Close()</c>d — only shown and
/// hidden, so its HWND (and the registered hotkey hooked to it) survives for the life of the app.
/// </summary>
public partial class SpotlightWindow : Window
{
    public SpotlightWindow(SpotlightViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // Auto-focus the search box when the window becomes active.
    private void OnActivated(object? sender, EventArgs e)
    {
        Keyboard.Focus(SearchTextBox);
        SearchTextBox.SelectAll();
    }

    // Focus loss dismisses the overlay (Spotlight UX convention).
    private void OnDeactivated(object? sender, EventArgs e)
    {
        Hide();
    }

    // Up/Down move selection, Enter activates, Esc hides.
    private async void OnSearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not SpotlightViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                var acted = await vm.ActivateSelectedAsync();
                if (acted)
                    Hide();
                e.Handled = true;
                break;
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }
}
