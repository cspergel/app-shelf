using System.Drawing;
using Forms = System.Windows.Forms;

namespace AppShelf.App.Tray;

/// <summary>System-tray icon and menu (spec §5.1). The app runs here in the background all
/// day; the window only shows on demand.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon? _appIcon;

    public TrayIconService(Action open, Action search, Action hotkey, Action add, Action ports, Action quit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open AppShelf", null, (_, _) => open());
        menu.Items.Add("Search…", null, (_, _) => search());
        menu.Items.Add("Hotkey…", null, (_, _) => hotkey());
        menu.Items.Add("Add app…", null, (_, _) => add());
        menu.Items.Add("Ports…", null, (_, _) => ports());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        _appIcon = LoadAppIcon();

        _icon = new Forms.NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Application,
            Visible = true,
            Text = "AppShelf",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => open();
    }

    /// <summary>Loads the embedded brand icon at the tray's preferred small size, falling back to
    /// the system icon if anything goes wrong.</summary>
    private static Icon? LoadAppIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/appshelf.ico"));
            if (info is null)
                return null;
            using var stream = info.Stream;
            return new Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon?.Dispose();
    }
}
