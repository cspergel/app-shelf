using System.Windows.Input;

namespace AppShelf.App.Spotlight;

/// <summary>
/// Pure parsing/formatting between a human hotkey string ("Ctrl+Shift+J") and the Win32
/// <c>RegisterHotKey</c> shape (modifier-mask + virtual-key). No WPF dispatcher required — only
/// <see cref="KeyInterop"/> and <see cref="Enum"/>, so it is unit-testable. Modifier tokens are
/// case-insensitive (Ctrl/Control, Alt, Shift, Win/Windows); the single non-modifier token is the
/// key. The parse returns null when the string has no real key or cannot be understood.
/// </summary>
public static class HotkeyCombo
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>
    /// Parse "Ctrl+Shift+J" into a Win32 modifier mask + virtual-key. Returns null when the string
    /// is blank, has no non-modifier key, names an unknown token, or maps to <see cref="Key.None"/>.
    /// </summary>
    public static (uint modifiers, uint vk)? Parse(string? combo)
    {
        if (string.IsNullOrWhiteSpace(combo))
            return null;

        uint modifiers = 0;
        Key? key = null;

        foreach (var raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    // Non-modifier token = the key. Only one is allowed; a second one is malformed.
                    if (key is not null)
                        return null;
                    if (!Enum.TryParse<Key>(raw, ignoreCase: true, out var parsed) || parsed == Key.None)
                        return null;
                    key = parsed;
                    break;
            }
        }

        if (key is null)
            return null;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        if (vk == 0)
            return null;

        return (modifiers, vk);
    }

    /// <summary>Format a captured modifier set + key into the canonical "Ctrl+Alt+Space" display
    /// string. Modifier order is fixed (Ctrl, Alt, Shift, Win) so display is stable.</summary>
    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>(4);
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
