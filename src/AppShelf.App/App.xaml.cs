using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AppShelf.App.Dialogs;
using AppShelf.App.Services;
using AppShelf.App.Tray;
using AppShelf.App.ViewModels;

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
            var viewModel = new MainViewModel(_service, _dialogs, ShowPorts);

            _window = new MainWindow(viewModel);
            MainWindow = _window; // so modal dialogs can anchor to it

            _tray = new TrayIconService(open: ShowWindow, add: AddFromTray, ports: ShowPorts, quit: QuitApp);

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
        _tray?.Dispose();
        _service?.Dispose(); // kills all running dev-server trees, frees ports (hard quit)
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
