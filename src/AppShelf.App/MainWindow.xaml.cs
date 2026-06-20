using System.ComponentModel;
using System.Windows;
using AppShelf.App.Services;
using AppShelf.App.ViewModels;
using AppShelf.App.Web;
using Microsoft.Web.WebView2.Core;

namespace AppShelf.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly GuiAppService _service;
    private AppShelfBridge? _bridge;

    // Hotkey delegates injected from App.xaml.cs after HotkeyService is constructed. Stored here
    // because the bridge is created asynchronously (WebView2 init), which may race the wiring call.
    private Func<string?, bool>? _tryRegisterHotkey;
    private Func<string?>? _getActiveHotkey;

    // Tray quick-launch snapshot callback injected from App.xaml.cs. Stored here (same reason as the
    // hotkey delegates) and forwarded to the bridge once WebView2 init builds it.
    private Action<IReadOnlyList<TrayAppSnapshot>>? _onAppsPolled;

    // Dev server the web UI is served from in DEBUG (see vite.config.ts → port 5199).
    private const string DevServerUrl = "http://localhost:5199";

    /// <summary>When false, closing the window hides it to the tray instead of exiting (spec §5.1).</summary>
    public bool AllowClose { get; set; }

    public MainWindow(MainViewModel viewModel, GuiAppService service)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _service = service;
        DataContext = viewModel;

        // Spike: status polling now lives in the React side (useBridge, ~2s). The old
        // DispatcherTimer that drove the WPF card grid is no longer needed.
        Loaded += async (_, _) =>
        {
            try
            {
                // --disable-gpu-compositing: WebView2's GPU compositor invalidates correctly on CSS
                //   variable (theme) changes. Without it, computed styles update but the compositor
                //   keeps painting stale colors even after a full page reload. (Not --disable-gpu, so
                //   Chromium still rasterises on the GPU; only the compositor is forced to software.)
                // --disable-background-timer-throttling (+ renderer-backgrounding / backgrounding-
                //   occluded-windows): when the window is hidden to the tray, Chromium otherwise
                //   throttles JS timers, which stalls the ~2s status poll that feeds the tray
                //   quick-launch snapshot — leaving tray status dots stale (e.g. grey while running).
                //   Disabling throttling keeps the poll live while hidden so the tray reflects reality.
                var options = new CoreWebView2EnvironmentOptions(
                    additionalBrowserArguments:
                        "--disable-gpu-compositing --disable-background-timer-throttling " +
                        "--disable-renderer-backgrounding --disable-backgrounding-occluded-windows");
                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null, userDataFolder: null, options: options);
                await WebView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"The web UI host (WebView2) could not start:\n\n{ex.Message}\n\n" +
                    "Ensure the WebView2 Runtime is installed.",
                    "AppShelf", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    private void WebView_OnInitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess || WebView.CoreWebView2 is null)
            return;

        // Wire the JS↔C# bridge before navigating so the page can call into Core immediately.
        _bridge = new AppShelfBridge(WebView, _service);

        // If the hotkey delegates were already supplied (App.xaml.cs runs before WebView2 init
        // completes), hand them to the freshly-constructed bridge now.
        if (_tryRegisterHotkey is not null && _getActiveHotkey is not null)
            _bridge.SetHotkeyDelegates(_tryRegisterHotkey, _getActiveHotkey);

        // Forward the tray quick-launch snapshot callback (supplied by App.xaml.cs before init).
        if (_onAppsPolled is not null)
            _bridge.OnAppsPolled = _onAppsPolled;

#if DEBUG
        WebView.CoreWebView2.Navigate(DevServerUrl);
#else
        // RELEASE: serve the web UI from assets shipped inside the exe. The web build is embedded
        // as webui.zip, extracted once to %LOCALAPPDATA%/AppShelf/webui/<version>/, and that folder
        // is mapped to a virtual host so the page loads with NO Vite dev server / no localhost.
        try
        {
            var folder = WebUiAssets.EnsureExtracted();
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                WebUiAssets.VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);
            WebView.CoreWebView2.Navigate($"https://{WebUiAssets.VirtualHost}/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The web UI could not be loaded:\n\n{ex.Message}\n\n" +
                "The application may not have been published correctly.",
                "AppShelf", MessageBoxButton.OK, MessageBoxImage.Error);
        }
#endif
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

    /// <summary>Bring the window back from the tray. Deliberately does NOT reload the WebView2:
    /// the web UI polls its own status (~2s) so a reload buys nothing, and a reload remounts the
    /// React app — resetting tab/search/dialog state. That remount also clobbered the C#→JS
    /// navigation push (tray "Ports"/"Add"/"Hotkey" flashed the target view then snapped back to
    /// Apps), since ShowFromTray() runs immediately before PostNavigation().</summary>
    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    /// <summary>Push a C#→JS navigation message to the web UI (no request id). The tray menu uses
    /// this to drive the React app (open Ports tab, add-app dialog, hotkey settings). Marshalled
    /// to the UI thread; a no-op if the WebView2 has not yet initialised.</summary>
    public void PostNavigation(string view)
    {
        void Post()
        {
            if (WebView.CoreWebView2 is null)
                return;

            var payload = System.Text.Json.JsonSerializer.Serialize(
                new { type = "navigate", view },
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                });
            WebView.CoreWebView2.PostWebMessageAsJson(payload);
        }

        if (Dispatcher.CheckAccess())
            Post();
        else
            Dispatcher.BeginInvoke(Post);
    }

    /// <summary>Inject the hotkey live-register + active-hotkey-read delegates from App.xaml.cs.
    /// Stored on the window (the bridge may not yet exist) and forwarded to the bridge if it does.</summary>
    public void SetHotkeyDelegate(Func<string?, bool> tryRegister, Func<string?> getActive)
    {
        _tryRegisterHotkey = tryRegister;
        _getActiveHotkey = getActive;
        _bridge?.SetHotkeyDelegates(tryRegister, getActive);
    }

    /// <summary>Inject the tray quick-launch snapshot callback from App.xaml.cs. Stored on the
    /// window (the bridge may not yet exist) and forwarded to the bridge when it does.</summary>
    public void SetOnAppsPolled(Action<IReadOnlyList<TrayAppSnapshot>> onAppsPolled)
    {
        _onAppsPolled = onAppsPolled;
        if (_bridge is not null)
            _bridge.OnAppsPolled = onAppsPolled;
    }
}
