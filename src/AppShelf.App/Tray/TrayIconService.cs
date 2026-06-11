using System.Drawing;
using Forms = System.Windows.Forms;

namespace AppShelf.App.Tray;

/// <summary>System-tray icon and menu (spec §5.1). The app runs here in the background all
/// day; the window only shows on demand.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIconService(Action open, Action add, Action ports, Action quit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open AppShelf", null, (_, _) => open());
        menu.Items.Add("Add app…", null, (_, _) => add());
        menu.Items.Add("Ports…", null, (_, _) => ports());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        _icon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "AppShelf",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => open();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
