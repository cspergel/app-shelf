using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using AppShelf.App.ViewModels;

namespace AppShelf.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _statusTimer;

    /// <summary>When false, closing the window hides it to the tray instead of exiting (spec §5.1).</summary>
    public bool AllowClose { get; set; }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => _viewModel.RefreshStatuses();
        _statusTimer.Start();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    /// <summary>Open the card's ⋯ overflow menu on left-click (anchored below the button).</summary>
    private void MoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } btn)
        {
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>Bring the window back from the tray and refresh the list.</summary>
    public void ShowFromTray()
    {
        _viewModel.LoadAll();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }
}
