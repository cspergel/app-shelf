using System.Windows;
using System.Windows.Input;
using AppShelf.App.Spotlight;

namespace AppShelf.App.Dialogs;

/// <summary>
/// Lets the user pick the global Spotlight hotkey (spec §5.3 hardening). The capture box swallows
/// keystrokes and shows the formatted combo; Save live-registers it via <see cref="_tryRegister"/>
/// (which restores the previous binding on failure) and, only on success, persists it via
/// <see cref="_persist"/>. A rejected combo (already owned by another app) shows an inline message
/// and keeps the dialog open. "Use default" clears the choice → the built-in Alt+Space chain.
/// </summary>
public partial class HotkeyWindow : Window
{
    private readonly Func<string?, bool> _tryRegister;
    private readonly Action<string?> _persist;

    /// <summary>The combo the user has captured this session, or null when "Use default" is chosen
    /// (or nothing captured yet — Save then means "(re)apply the default chain").</summary>
    private string? _captured;
    private bool _hasCapture;

    /// <param name="currentHotkey">The currently-active combo for display, or null.</param>
    /// <param name="tryRegister">Live-registers a combo (null = default chain); false if rejected,
    /// restoring the previous binding. Wire to <c>HotkeyService.TrySetHotkey</c>.</param>
    /// <param name="persist">Saves the combo to config (null = clear). Wire to
    /// <c>GuiAppService.SetHotkey</c>. Only called after a successful register.</param>
    public HotkeyWindow(string? currentHotkey, Func<string?, bool> tryRegister, Action<string?> persist)
    {
        InitializeComponent();

        _tryRegister = tryRegister;
        _persist = persist;

        CaptureBox.Text = string.IsNullOrWhiteSpace(currentHotkey)
            ? "Press a shortcut…"
            : currentHotkey;

        Loaded += (_, _) => CaptureBox.Focus();
    }

    private void CaptureBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Always swallow — this box is a capture surface, not a text field.
        e.Handled = true;

        // Resolve the real key (system keys arrive as Key.System with the true key in SystemKey).
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore lone modifier presses — wait for a real key to accompany them.
        if (IsModifierKey(key))
            return;

        var modifiers = Keyboard.Modifiers;

        // Require at least one modifier so the global hotkey can't swallow a bare letter system-wide.
        if (modifiers == ModifierKeys.None)
        {
            ShowMessage("Add a modifier (Ctrl, Alt, Shift, or Win) to the key.");
            return;
        }

        _captured = HotkeyCombo.Format(modifiers, key);
        _hasCapture = true;
        CaptureBox.Text = _captured;
        ClearMessage();
    }

    private void UseDefaultButton_OnClick(object sender, RoutedEventArgs e)
    {
        _captured = null;       // null = revert to the built-in default chain
        _hasCapture = true;
        CaptureBox.Text = "Default (Alt+Space)";
        ClearMessage();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Nothing changed this session — treat Save as a no-op close.
        if (!_hasCapture)
        {
            DialogResult = false;
            return;
        }

        if (_tryRegister(_captured))
        {
            _persist(_captured);
            DialogResult = true;
            return;
        }

        ShowMessage("That shortcut is already in use by another app — try another.");
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.Visibility = Visibility.Visible;
    }

    private void ClearMessage()
    {
        MessageText.Text = string.Empty;
        MessageText.Visibility = Visibility.Collapsed;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;
}
