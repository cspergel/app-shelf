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
                // Disable GPU compositing so WebView2's GPU compositor invalidates correctly
                // on CSS variable (theme) changes. Without this, computed styles update but the
                // compositor keeps painting stale colors even after a full page reload.
                // Using --disable-gpu-compositing (not --disable-gpu) so Chromium still uses the
                // GPU for rasterisation; only the compositor layer is forced to software.
                var options = new CoreWebView2EnvironmentOptions(
                    additionalBrowserArguments: "--disable-gpu-compositing");
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

#if DEBUG
        WebView.CoreWebView2.Navigate(DevServerUrl);
#else
        // TODO (post-spike): serve the built web assets from disk/embedded resources via
        // SetVirtualHostNameToFolderMapping and navigate to the virtual host, so RELEASE
        // builds don't depend on a running Vite dev server.
        WebView.CoreWebView2.Navigate(DevServerUrl);
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

    /// <summary>Bring the window back from the tray. The web UI polls its own status, so a reload
    /// is enough to refresh immediately.</summary>
    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        WebView.CoreWebView2?.Reload();
    }
}
