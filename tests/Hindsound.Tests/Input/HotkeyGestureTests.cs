using Hindsound.Input;

namespace Hindsound.Tests.Input;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+F10", 0x0006u, 0x79u)]
    [InlineData("Ctrl+Alt+H", 0x0003u, 0x48u)]
    [InlineData("Alt+F4", 0x0001u, 0x73u)]
    [InlineData("Win+Shift+S", 0x000Cu, 0x53u)]
    [InlineData("Ctrl+Alt+Shift+Win+Z", 0x000Fu, 0x5Au)]
    [InlineData("Ctrl+Alt+Shift+F24", 0x0007u, 0x87u)]
    [InlineData("F1", 0u, 0x70u)]
    [InlineData("F12", 0u, 0x7Bu)]
    [InlineData("F24", 0u, 0x87u)]
    [InlineData("Ctrl+0", 0x0002u, 0x30u)]
    [InlineData("Alt+9", 0x0001u, 0x39u)]
    [InlineData("Ctrl+Space", 0x0002u, 0x20u)]
    [InlineData("Shift+PageDown", 0x0004u, 0x22u)]
    public void Parse_GivesModifiersAndVirtualKey(string text, uint modifiers, uint key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(modifiers, g.Modifiers);
        Assert.Equal(key, g.VirtualKey);
    }

    [Theory]
    [InlineData("ctrl+shift+f10", "Ctrl+Shift+F10")]
    [InlineData(" Shift + Ctrl + a ", "Ctrl+Shift+A")]
    [InlineData("Control+Alt+h", "Ctrl+Alt+H")]
    [InlineData("Win+Alt+Shift+Ctrl+Q", "Ctrl+Alt+Shift+Win+Q")]
    public void Parse_ToleratesCaseSpacesAndOrder_AndWritesCanonicalText(string text, string canonical)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(canonical, g.ToString());
    }

    [Theory]
    [InlineData("Ctrl+Shift+F10")]
    [InlineData("Ctrl+Alt+Shift+Win+Z")]
    [InlineData("F5")]
    [InlineData("Alt+7")]
    [InlineData("Ctrl+Home")]
    [InlineData("Shift+Escape")]
    [InlineData("Ctrl+Alt+Shift+F24")]
    public void RoundTrip_TextToGestureToText(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(text, g.ToString());
        Assert.True(HotkeyGesture.TryParse(g.ToString(), out var again));
        Assert.Equal(g, again);
    }

    [Theory]
    [InlineData(0x0006u, 0x79u, "Ctrl+Shift+F10")]
    [InlineData(0x0003u, 0x48u, "Ctrl+Alt+H")]
    [InlineData(0x0000u, 0x7Au, "F11")]
    public void RoundTrip_PartsToTextToParts(uint modifiers, uint key, string text)
    {
        Assert.True(HotkeyGesture.TryCreate(modifiers, key, out var g));
        Assert.Equal(text, g.ToString());
        Assert.True(HotkeyGesture.TryParse(text, out var parsed));
        Assert.Equal(modifiers, parsed.Modifiers);
        Assert.Equal(key, parsed.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl++A")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Banana+A")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F1x")]
    [InlineData("Ctrl+AB")]
    [InlineData("A")]
    [InlineData("5")]
    [InlineData("Space")]
    [InlineData("Escape")]
    public void Invalid_IsRejectedWithoutException(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(default, g);
    }

    [Fact]
    public void TryCreate_RejectsUnknownKeyUnknownModifierAndBareLetter()
    {
        Assert.False(HotkeyGesture.TryCreate(HotkeyGesture.ModControl, 0xFF, out _));
        Assert.False(HotkeyGesture.TryCreate(0x4000, 0x48, out _));
        Assert.False(HotkeyGesture.TryCreate(0, 0x48, out _));
        Assert.True(HotkeyGesture.TryCreate(0, 0x70, out _));
    }
}
