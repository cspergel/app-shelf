using System.Drawing;
using Forms = System.Windows.Forms;

namespace AppShelf.App.Tray;

/// <summary>A single app projected for the tray quick-launch submenu. <c>Status</c> is the same
/// string the bridge emits (<c>"Running"</c>, <c>"Stopped"</c>, <c>"StoppedUnexpectedly"</c>, …);
/// <c>IsRunning</c> is the convenience bool (Running/Starting) the menu uses to pick the
/// launch-vs-stop toggle without re-parsing the status string.</summary>
internal sealed record TrayAppView(string Id, string Name, string Status, bool Favorite, bool IsRunning);

/// <summary>System-tray icon and menu (spec §5.1). The app runs here in the background all
/// day; the window only shows on demand.</summary>
public sealed class TrayIconService : IDisposable
{
    // Cap the quick-launch submenu so a user with 20+ favorited/running apps still gets a
    // readable menu. Overflow is silently dropped (favorites-first, then alphabetical).
    private const int MaxQuickLaunchItems = 8;

    // Sentinel tags marking the boundary of the dynamically-rebuilt quick-launch section, so
    // OnMenuOpening can cleanly remove the previous build before inserting a fresh one.
    private const string AppsSectionTag = "apps-quicklaunch";

    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Icon? _appIcon;

    // Status-dot bitmaps for the quick-launch menu items. WinForms draws menu *text* via GDI, which
    // renders colour-emoji (🟢/🔴/⚪) as monochrome glyphs — so a coloured dot must be an Image, not a
    // text glyph. Created once (service lives for the app's lifetime) and assigned to each item's
    // Image; the items never own/dispose these (we null their Image before disposing on rebuild).
    private readonly Bitmap _dotRunning;
    private readonly Bitmap _dotError;
    private readonly Bitmap _dotIdle;

    // Quick-launch wiring: a snapshot accessor (read on the WinForms message-pump thread in
    // OnMenuOpening — zero I/O) plus launch/stop actions (dispatched off the UI thread).
    private readonly Func<IReadOnlyList<TrayAppView>> _getAppSnapshot;
    private readonly Action<string> _launchApp;
    private readonly Action<string> _stopApp;

    // Constructor is internal: it accepts the internal TrayAppView snapshot type, and the only
    // caller is App.xaml.cs (same assembly). Keeps TrayAppView App-layer-internal per the plan.
    internal TrayIconService(
        Action open, Action search, Action hotkey, Action add, Action ports, Action quit,
        Func<IReadOnlyList<TrayAppView>> getAppSnapshot,
        Action<string> launchApp,
        Action<string> stopApp)
    {
        _getAppSnapshot = getAppSnapshot;
        _launchApp = launchApp;
        _stopApp = stopApp;

        // Ostara emerald for live; a visible red/grey that read on the (light) system menu surface.
        // Color is fully-qualified: a global using aliases unqualified `Color` to WPF's Media.Color.
        _dotRunning = MakeDot(System.Drawing.Color.FromArgb(0x00, 0xAB, 0x55));
        _dotError = MakeDot(System.Drawing.Color.FromArgb(0xE5, 0x48, 0x4D));
        _dotIdle = MakeDot(System.Drawing.Color.FromArgb(0x8C, 0x96, 0xA0));

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open AppShelf", null, (_, _) => open());
        menu.Items.Add("Search…", null, (_, _) => search());
        menu.Items.Add("Hotkey…", null, (_, _) => hotkey());
        menu.Items.Add("Add app…", null, (_, _) => add());
        menu.Items.Add("Ports…", null, (_, _) => ports());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        // Rebuild the quick-launch section every time the menu opens, from the last-known
        // status snapshot (no blocking probe on the UI thread).
        menu.Opening += OnMenuOpening;
        _menu = menu;

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

    /// <summary>Rebuild the quick-launch app section at the top of the tray menu. Runs on the
    /// WinForms message-pump thread (safe to touch WinForms APIs directly). Reads a pre-computed
    /// status snapshot — NEVER probes status here (a blocking TCP probe would freeze the menu).
    /// Shows favorited and/or currently-running apps with a status dot and a launch/stop toggle.</summary>
    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Remove the previously-inserted dynamic section (tagged items + their trailing separator).
        for (var i = _menu.Items.Count - 1; i >= 0; i--)
        {
            if (_menu.Items[i].Tag as string == AppsSectionTag)
            {
                var item = _menu.Items[i];
                _menu.Items.RemoveAt(i);
                item.Image = null; // shared status-dot bitmaps are owned by this service, not the item
                item.Dispose();
            }
        }

        IReadOnlyList<TrayAppView> snapshot;
        try
        {
            snapshot = _getAppSnapshot() ?? Array.Empty<TrayAppView>();
        }
        catch
        {
            // A snapshot read should never throw, but never let the tray menu fail to open.
            return;
        }

        var apps = snapshot
            .Where(a => a.Favorite || a.IsRunning)
            .OrderByDescending(a => a.Favorite)         // favorites first
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase) // alphabetical within tier
            .Take(MaxQuickLaunchItems)
            .ToList();

        if (apps.Count == 0)
            return; // no curated apps → no section, no separator

        // Insert at the very top (above "Open AppShelf") for quick access, followed by a separator.
        var insertAt = 0;
        foreach (var app in apps)
        {
            var menuItem = new Forms.ToolStripMenuItem(app.Name)
            {
                Tag = AppsSectionTag,
                Image = StatusDotImage(app.Status),
            };
            var id = app.Id;
            var running = app.IsRunning;
            menuItem.Click += (_, _) =>
            {
                // Fire-and-forget off the UI thread: launch/stop do blocking work. Exceptions are
                // swallowed (a removed-between-poll-and-click app shouldn't crash the tray menu).
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (running) _stopApp(id);
                        else _launchApp(id);
                    }
                    catch
                    {
                        /* fire-and-forget tray action — no UI to surface a failure to */
                    }
                });
            };
            _menu.Items.Insert(insertAt++, menuItem);
        }

        var separator = new Forms.ToolStripSeparator { Tag = AppsSectionTag };
        _menu.Items.Insert(insertAt, separator);
    }

    /// <summary>Picks the status-dot bitmap for a menu item: green for live (Running/Starting), red
    /// for crashed/errored, grey otherwise. Returns a shared bitmap (do not dispose per-item).</summary>
    private Bitmap StatusDotImage(string status) => status switch
    {
        "Running" or "Starting" => _dotRunning,
        "Error" or "StoppedUnexpectedly" => _dotError,
        _ => _dotIdle,
    };

    /// <summary>Draws a small antialiased filled circle for use as a menu-item status dot. 16×16 is
    /// the WinForms default menu image size; the circle is inset so it reads as a dot, not a tile.</summary>
    private static Bitmap MakeDot(System.Drawing.Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 4, 4, 8, 8);
        return bmp;
    }

    /// <summary>Show a tray balloon notification for 5 seconds with a Warning icon.</summary>
    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Warning);

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
        _dotRunning.Dispose();
        _dotError.Dispose();
        _dotIdle.Dispose();
    }
}
