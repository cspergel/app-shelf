using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AppShelf.App.Dialogs;
using AppShelf.App.Services;
using AppShelf.App.Spotlight;
using AppShelf.App.Tray;
using AppShelf.App.ViewModels;
using Forms = System.Windows.Forms;

namespace AppShelf.App;

/// <summary>
/// Application bootstrap (spec §5.1): builds the service + view model, shows the main window,
/// and installs the tray icon. Closing the window hides to tray; only Quit (or app exit) tears
/// down running dev servers and frees their ports.
/// </summary>
public partial class App : Application
{
    // A stable, machine-wide name so a second launch can detect the first. Not random/time-based.
    private const string SingleInstanceMutexName = "AppShelf.SingleInstance.9E1C7A3B-2F4D-4C6E-9B8A-1D5E7F0A2C34";

    private Mutex? _singleInstance;
    private GuiAppService _service = null!;
    private IAppDialogs _dialogs = null!;
    private TrayIconService _tray = null!;
    private MainWindow _window = null!;
    private SpotlightWindow _spotlight = null!;
    private HotkeyService? _hotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard: a second launch would spin up a competing tray icon AND a second
        // ProcessManager — the two would race on config.json writes and on kill-on-close jobs for
        // the same dev servers. Bail early and point the user at the running instance.
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "AppShelf is already running — look for its icon in the system tray (near the clock).",
                "AppShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        // Last-resort safety nets: surface a readable message instead of the raw .NET crash dialog,
        // and keep running dev servers from being orphaned by an unexpected failure.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            _service = new GuiAppService();
            _dialogs = new DialogService(_service);

            // Construct the tray first so its ShowBalloon can be handed to the MainViewModel's
            // crash watcher. The tray ctor only captures the menu/double-click action delegates
            // (instance methods on App) — it does not touch _window, _spotlight, or the view model
            // at construction time, so building it ahead of them is safe.
            _tray = new TrayIconService(
                open: ShowWindow,
                search: ShowSpotlight,
                hotkey: ShowHotkeySettings,
                add: AddFromTray,
                ports: ShowPorts,
                quit: QuitApp);

            var viewModel = new MainViewModel(_service, _dialogs, ShowPorts, _tray.ShowBalloon);

            _window = new MainWindow(viewModel, _service);
            MainWindow = _window; // so modal dialogs can anchor to it

            // Create the Spotlight overlay HIDDEN. EnsureHandle materialises the HWND without
            // showing the window — required before HotkeyService can RegisterHotKey against it.
            var spotlightVm = new SpotlightViewModel(_service);
            _spotlight = new SpotlightWindow(spotlightVm);
            new WindowInteropHelper(_spotlight).EnsureHandle();

            _hotkey = new HotkeyService(_spotlight, ToggleSpotlight, _service.GetHotkey());

            // Show on launch (so `appshelf open` produces a window); close hides back to the tray.
            _window.Show();
        }
        catch (Exception ex)
        {
            // Most likely a corrupt or locked config.json — tell the user why and exit cleanly
            // rather than crashing during startup with a raw stack trace.
            MessageBox.Show(
                $"AppShelf could not start:\n\n{ex.Message}\n\n" +
                "If your config file is corrupt, fix or delete it and try again:\n" +
                "%APPDATA%\\AppShelf\\config.json",
                "AppShelf", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A UI-thread exception (a failed launch, a locked config write, a bad URL). Log the full
        // detail, show the user the gist, and keep the app alive — the tray and running servers stay.
        LogError(e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to:\n{ErrorLogPath}",
            "AppShelf", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Non-UI-thread fatal error. We can't recover, but we can record + report it. OnExit still
        // runs (kills the job-owned dev servers) on a normal shutdown path.
        var ex = e.ExceptionObject as Exception;
        LogError(ex);
        MessageBox.Show(
            $"AppShelf hit a fatal error and must close:\n\n{ex?.Message ?? "an unexpected error"}\n\n" +
            $"Details were written to:\n{ErrorLogPath}",
            "AppShelf", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string ErrorLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppShelf", "error.log");

    /// <summary>Appends the full exception (with stack trace) to %APPDATA%\AppShelf\error.log so a
    /// user can attach it to a bug report. Logging must never throw.</summary>
    private static void LogError(Exception? ex)
    {
        try
        {
            var path = ErrorLogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            /* never let logging crash the crash handler */
        }
    }

    private void ShowWindow() => _window.ShowFromTray();

    private void ShowPorts()
    {
        var owner = _window.IsVisible ? _window : null;
        var ports = new PortsWindow(_service, _dialogs) { Owner = owner };
        ports.ShowDialog();
    }

    /// <summary>Open the Hotkey settings dialog. A successful change re-registers the global hotkey
    /// live (no restart) via <see cref="HotkeyService.TrySetHotkey"/> and persists it to config; the
    /// dialog restores the previous binding if a chosen combo is already in use by another app.</summary>
    private void ShowHotkeySettings()
    {
        if (_hotkey is null)
            return;

        var owner = _window.IsVisible ? _window : null;
        var dialog = new HotkeyWindow(
            currentHotkey: _hotkey.ActiveHotkey,
            tryRegister: _hotkey.TrySetHotkey,
            persist: _service.SetHotkey)
        {
            Owner = owner,
        };
        dialog.ShowDialog();
    }

    private void ToggleSpotlight()
    {
        if (_spotlight.IsVisible)
            _spotlight.Hide();
        else
            ShowSpotlight();
    }

    private void ShowSpotlight()
    {
        // Position: centre on the monitor under the cursor, in its upper third vertically.
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var workArea = screen.WorkingArea;

        // SpotlightWindow uses SizeToContent, so force a layout pass to get a valid DesiredSize.
        _spotlight.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _spotlight.Arrange(new Rect(_spotlight.DesiredSize));

        var source = PresentationSource.FromVisual(_spotlight)
                     ?? PresentationSource.FromVisual(_window);
        var scaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

        // WorkingArea is in physical pixels; window Left/Top are in DIPs.
        var dipLeft = workArea.Left * scaleX;
        var dipTop = workArea.Top * scaleY;
        var dipWidth = workArea.Width * scaleX;
        var dipHeight = workArea.Height * scaleY;

        var winW = _spotlight.DesiredSize.Width;
        var winH = _spotlight.DesiredSize.Height;

        _spotlight.Left = dipLeft + (dipWidth - winW) / 2;
        _spotlight.Top = dipTop + (dipHeight - winH) / 3; // upper-third, not dead centre

        if (_spotlight.DataContext is SpotlightViewModel vm)
            vm.Reset();

        _spotlight.Show();
        _spotlight.Activate();
    }

    private void AddFromTray()
    {
        _window.ShowFromTray();
        if (_window.DataContext is MainViewModel vm && vm.AddCommand.CanExecute(null))
            vm.AddCommand.Execute(null);
    }

    private void QuitApp()
    {
        _window.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        _service?.Dispose(); // kills all running dev-server trees, frees ports (hard quit)
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
