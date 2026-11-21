using Hindsound.Input;
using Hindsound.Mac.Input;

namespace Hindsound.Mac.Tests.Input;

/// <summary>The keys of the Core as Carbon names them. Pure: runs on every system.</summary>
public sealed class MacKeyCodesTests
{
    [Theory]
    [InlineData("A", 0x00)]
    [InlineData("S", 0x01)]
    [InlineData("Z", 0x06)]
    [InlineData("Q", 0x0C)]
    [InlineData("M", 0x2E)]
    [InlineData("0", 0x1D)]
    [InlineData("5", 0x17)]
    [InlineData("9", 0x19)]
    [InlineData("F1", 0x7A)]
    [InlineData("F9", 0x65)]
    [InlineData("F12", 0x6F)]
    [InlineData("F20", 0x5A)]
    [InlineData("Space", 0x31)]
    [InlineData("Enter", 0x24)]
    [InlineData("Escape", 0x35)]
    [InlineData("Insert", 0x72)]
    [InlineData("Delete", 0x75)]
    [InlineData("PageDown", 0x79)]
    [InlineData("Left", 0x7B)]
    [InlineData("Up", 0x7E)]
    public void A_key_of_the_Core_is_the_key_code_of_its_place_on_a_Mac(string key, int expected)
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+" + key, out var gesture));
        Assert.Equal((ushort)expected, MacKeyCodes.ToKeyCode(gesture.VirtualKey));
    }

    [Theory]
    [InlineData("F21")]
    [InlineData("F24")]
    [InlineData("Pause")]
    public void A_key_a_Mac_does_not_have_has_no_code(string key)
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+" + key, out var gesture));
        Assert.Null(MacKeyCodes.ToKeyCode(gesture.VirtualKey));
    }

    [Fact]
    public void Every_letter_digit_and_function_key_up_to_F20_has_a_code_of_its_own()
    {
        var keys = Enumerable.Range('A', 26).Concat(Enumerable.Range('0', 10)).Concat(Enumerable.Range(0x70, 20)).Select(k => (uint)k).ToArray();
        var codes = keys.Select(MacKeyCodes.ToKeyCode).ToArray();

        Assert.All(codes, code => Assert.NotNull(code));
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    [Theory]
    [InlineData("Ctrl+S", 0x1000)]
    [InlineData("Alt+S", 0x0800)]
    [InlineData("Shift+S", 0x0200)]
    [InlineData("Win+S", 0x0100)]
    [InlineData("Ctrl+Alt+Shift+Win+S", 0x1B00)]
    public void Ctrl_is_Control_Alt_is_Option_and_Win_is_Command(string text, int expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal((uint)expected, MacKeyCodes.ToModifiers(gesture.Modifiers));
    }
}
