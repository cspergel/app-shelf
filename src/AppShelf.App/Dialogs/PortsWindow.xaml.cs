using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AppShelf.App.Services;
using AppShelf.App.ViewModels;

namespace AppShelf.App.Dialogs;

/// <summary>The "Ports" panel (Port Doctor): a live table of dev-server ports with ownership
/// tiers and evidence-backed confirm kills. Mirrors <c>appshelf doctor</c> in the GUI.</summary>
public partial class PortsWindow : Window
{
    public PortsWindow(GuiAppService service, IAppDialogs dialogs)
    {
        InitializeComponent();
        DataContext = new PortDoctorViewModel(service, dialogs);
    }

    /// <summary>Contain mouse-wheel scroll within the ancestry ScrollViewer so it does not
    /// bubble up to the parent DataGrid's ScrollViewer and jump to the next port row.</summary>
    private void AncestryScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
