using System.Windows;
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
}
