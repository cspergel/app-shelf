using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AppShelf.App.Spotlight;

/// <summary>
/// Registers a global hotkey (spec §5.3) so the Spotlight overlay can be summoned from anywhere in
/// Windows. A user-configured combo (e.g. <c>Ctrl+Shift+J</c>) is tried first; if it is blank or
/// fails to register, it falls back to the built-in chain <c>Alt+Space</c> then <c>Ctrl+Alt+Space</c>.
/// Failure is logged (never thrown) — the tray "Search…"/"Hotkey…" items remain working fallbacks.
/// The hotkey is unregistered on <see cref="Dispose"/>.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_SPACE = 0x0020;
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 1; // single registration slot, re-used on every (re)register

    private readonly Window _overlayWindow;
    private readonly Action _toggleOverlay;
    private readonly IntPtr _hwnd;
    private readonly HwndSource? _src;
    private bool _hasRegistration;
    private bool _disposed;

    /// <summary>True if a hotkey is currently registered.</summary>
    public bool IsRegistered => _hasRegistration;

    /// <summary>The currently-registered combo as a display string (e.g. "Ctrl+Alt+Space"), or
    /// null when nothing is registered.</summary>
    public string? ActiveHotkey { get; private set; }

    /// <param name="overlayWindow">The HIDDEN overlay window. Its HWND must already exist
    /// (call <c>new WindowInteropHelper(overlayWindow).EnsureHandle()</c> before this ctor).</param>
    /// <param name="toggleOverlay">Invoked on the UI thread when the hotkey fires.</param>
    /// <param name="configuredCombo">The user's saved combo, or null/blank for the default chain.</param>
    public HotkeyService(Window overlayWindow, Action toggleOverlay, string? configuredCombo = null)
    {
        _overlayWindow = overlayWindow;
        _toggleOverlay = toggleOverlay;
        _hwnd = new WindowInteropHelper(overlayWindow).Handle;

        _src = HwndSource.FromHwnd(_hwnd);
        _src?.AddHook(WndProc);

        Register(configuredCombo);
    }

    /// <summary>
    /// Unregister the current hotkey and register <paramref name="combo"/> (or the default chain
    /// when null/blank). On failure, the previous registration is restored so the user is never left
    /// with no working hotkey. Returns true when the requested combo registered successfully.
    /// </summary>
    public bool TrySetHotkey(string? combo)
    {
        var previousCombo = ActiveHotkey;

        Unregister();

        if (Register(combo))
            return true;

        // Restore whatever was working before, so the user isn't left worse off.
        Register(previousCombo);
        return false;
    }

    /// <summary>
    /// Register the configured combo first (if it parses), then the default Alt+Space then
    /// Ctrl+Alt+Space chain. Sets <see cref="ActiveHotkey"/> on success. Returns true if the
    /// requested combo (not a fallback) registered; false if it failed/blank — but a fallback may
    /// still have registered, so callers wanting strict success should check the return value.
    /// </summary>
    private bool Register(string? configuredCombo)
    {
        if (!string.IsNullOrWhiteSpace(configuredCombo))
        {
            var parsed = HotkeyCombo.Parse(configuredCombo);
            if (parsed is { } p && RegisterHotKey(_hwnd, HOTKEY_ID, p.modifiers, p.vk))
            {
                _hasRegistration = true;
                ActiveHotkey = configuredCombo.Trim();
                return true;
            }

            // Requested combo did not register: fall through to the default chain, but report failure
            // so the caller (e.g. the Hotkey dialog) knows the user's choice was rejected.
            if (RegisterDefaultChain())
                Debug.WriteLine($"AppShelf: hotkey '{configuredCombo}' could not be registered; using default chain.");
            return false;
        }

        // No configured combo: the default chain IS the success path.
        return RegisterDefaultChain();
    }

    /// <summary>Try Alt+Space, then Ctrl+Alt+Space. Sets <see cref="ActiveHotkey"/> on success.</summary>
    private bool RegisterDefaultChain()
    {
        if (RegisterHotKey(_hwnd, HOTKEY_ID, MOD_ALT, VK_SPACE))
        {
            _hasRegistration = true;
            ActiveHotkey = "Alt+Space";
            return true;
        }

        if (RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_SPACE))
        {
            _hasRegistration = true;
            ActiveHotkey = "Ctrl+Alt+Space";
            return true;
        }

        _hasRegistration = false;
        ActiveHotkey = null;
        Debug.WriteLine(
            "AppShelf: could not register Alt+Space or Ctrl+Alt+Space — both are unavailable. " +
            "Use the tray \"Search…\" item to open the overlay.");
        return false;
    }

    private void Unregister()
    {
        if (_hasRegistration)
            UnregisterHotKey(_hwnd, HOTKEY_ID);
        _hasRegistration = false;
        ActiveHotkey = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            _overlayWindow.Dispatcher.BeginInvoke(_toggleOverlay);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _src?.RemoveHook(WndProc);
        Unregister();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
