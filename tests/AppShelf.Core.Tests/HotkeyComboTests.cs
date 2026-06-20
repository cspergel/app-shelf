using System.Windows.Input;
using AppShelf.App.Spotlight;

namespace AppShelf.Core.Tests;

/// <summary>Pure parse/format round-trips for <see cref="HotkeyCombo"/>. These exercise only
/// KeyInterop + Enum, so no live WPF dispatcher is required.</summary>
public class HotkeyComboTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyCombo.MOD_CONTROL | HotkeyCombo.MOD_ALT, 0x20)] // VK_SPACE
    [InlineData("Alt+Space", HotkeyCombo.MOD_ALT, 0x20)]
    [InlineData("Ctrl+Shift+J", HotkeyCombo.MOD_CONTROL | HotkeyCombo.MOD_SHIFT, 0x4A)] // 'J'
    [InlineData("win+space", HotkeyCombo.MOD_WIN, 0x20)] // case-insensitive
    [InlineData("Control+J", HotkeyCombo.MOD_CONTROL, 0x4A)] // Control alias
    public void Parse_ValidCombo_ReturnsModifiersAndVk(string combo, uint expectedMods, uint expectedVk)
    {
        var result = HotkeyCombo.Parse(combo);

        Assert.NotNull(result);
        Assert.Equal(expectedMods, result!.Value.modifiers);
        Assert.Equal(expectedVk, result.Value.vk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]      // modifiers only, no real key
    [InlineData("Shift")]         // single modifier, no key
    [InlineData("Ctrl+Nonsense")] // unknown key token
    [InlineData("Ctrl+J+K")]      // two non-modifier keys
    public void Parse_InvalidCombo_ReturnsNull(string? combo)
    {
        Assert.Null(HotkeyCombo.Parse(combo));
    }

    [Fact]
    public void Parse_IgnoresSurroundingWhitespace()
    {
        var result = HotkeyCombo.Parse("  Ctrl + Shift + J  ");

        Assert.NotNull(result);
        Assert.Equal(HotkeyCombo.MOD_CONTROL | HotkeyCombo.MOD_SHIFT, result!.Value.modifiers);
    }

    [Fact]
    public void Format_OrdersModifiersCanonically()
    {
        var formatted = HotkeyCombo.Format(
            ModifierKeys.Shift | ModifierKeys.Control | ModifierKeys.Alt, Key.J);

        Assert.Equal("Ctrl+Alt+Shift+J", formatted);
    }

    [Fact]
    public void Format_SingleModifierAndKey()
    {
        Assert.Equal("Ctrl+J", HotkeyCombo.Format(ModifierKeys.Control, Key.J));
    }

    [Fact]
    public void FormatThenParse_RoundTrips()
    {
        var formatted = HotkeyCombo.Format(ModifierKeys.Control | ModifierKeys.Shift, Key.J);
        var parsed = HotkeyCombo.Parse(formatted);

        Assert.NotNull(parsed);
        Assert.Equal(HotkeyCombo.MOD_CONTROL | HotkeyCombo.MOD_SHIFT, parsed!.Value.modifiers);
        Assert.Equal((uint)KeyInterop.VirtualKeyFromKey(Key.J), parsed.Value.vk);
    }

    /// <summary>Documents the contract the bridge's <c>setHotkey</c> length-cap relies on: an
    /// absurdly long combo has no valid single key token and therefore never parses. The 100-char
    /// cap itself lives in the bridge (Phase 3 A3); this is the Core-level guard behind it.</summary>
    [Fact]
    public void Parse_WithVeryLongInput_ReturnsNull()
    {
        var longInput = new string('J', 150);

        Assert.Null(HotkeyCombo.Parse(longInput));
    }
}
