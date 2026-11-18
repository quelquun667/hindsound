using Hindsound.Input;
using Hindsound.Linux.Window.Input;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.Tests.Window.Input;

/// <summary>The keys of SDL become the virtual keys and modifiers the screen and the shortcut capture take.</summary>
public class SdlKeysTests
{
    private const uint Mask = SdlKeys.ScancodeMask;

    // Values of SDL_scancode.h (SDL 3.2), written out here so that a slip in the table of the code shows.
    [Theory]
    [InlineData(40u, Vk.Enter)]
    [InlineData(88u, Vk.Enter)] // keypad Enter
    [InlineData(41u, Vk.Escape)]
    [InlineData(42u, Vk.Back)]
    [InlineData(43u, Vk.Tab)]
    [InlineData(44u, Vk.Space)]
    [InlineData(73u, 0x2Du)] // Insert
    [InlineData(76u, 0x2Eu)] // Delete
    [InlineData(74u, Vk.Home)]
    [InlineData(77u, Vk.End)]
    [InlineData(75u, Vk.PageUp)]
    [InlineData(78u, Vk.PageDown)]
    [InlineData(80u, Vk.Left)]
    [InlineData(79u, Vk.Right)]
    [InlineData(82u, Vk.Up)]
    [InlineData(81u, Vk.Down)]
    [InlineData(72u, 0x13u)] // Pause
    [InlineData(58u, 0x70u)] // F1
    [InlineData(69u, 0x7Bu)] // F12
    [InlineData(104u, 0x7Cu)] // F13
    [InlineData(115u, 0x87u)] // F24
    [InlineData(84u, 0x6Fu)] // keypad /
    [InlineData(85u, 0x6Au)] // keypad *
    [InlineData(86u, 0x6Du)] // keypad -
    [InlineData(87u, 0x6Bu)] // keypad +
    [InlineData(224u, Vk.Ctrl)]
    [InlineData(228u, Vk.Ctrl)]
    [InlineData(225u, Vk.Shift)]
    [InlineData(229u, Vk.Shift)]
    [InlineData(226u, Vk.Alt)]
    [InlineData(230u, Vk.Alt)]
    [InlineData(227u, Vk.LWin)]
    [InlineData(231u, Vk.RWin)]
    [InlineData(0u, 0u)] // unknown
    [InlineData(0x10Au, 0u)] // a media key: nothing on the screen takes it
    public void A_key_without_a_character_is_told_by_its_place(uint scancode, uint vk) =>
        Assert.Equal(vk, SdlKeys.VirtualKey(scancode, scancode | Mask, 0));

    [Theory]
    [InlineData(4u, 0x61u, 0x41u)] // US A
    [InlineData(20u, 0x61u, 0x41u)] // AZERTY: the key marked A sits where US has Q
    [InlineData(4u, 0x71u, 0x51u)]
    [InlineData(51u, 0x6Du, 0x4Du)] // AZERTY M, where US has ;
    [InlineData(29u, 0x77u, 0x57u)]
    [InlineData(4u, 0x41u, 0x41u)] // a keycode with Shift applied
    [InlineData(30u, 0x31u, 0x31u)]
    [InlineData(39u, 0x30u, 0x30u)]
    [InlineData(30u, 0x21u, 0x31u)] // Shift+1 with modified keycodes: still the key 1
    [InlineData(32u, 0x23u, 0x33u)] // Shift+3 likewise
    [InlineData(31u, 0xE9u, 0x32u)] // AZERTY 2 without the French digits: the place names it
    [InlineData(16u, 0x2Cu, 0xBCu)] // AZERTY comma, where US has M
    [InlineData(51u, 0x3Bu, 0xBAu)]
    [InlineData(45u, 0x2Du, 0xBDu)]
    [InlineData(46u, 0x3Du, 0xBBu)]
    [InlineData(47u, 0x5Bu, 0xDBu)]
    [InlineData(48u, 0x5Du, 0xDDu)]
    [InlineData(49u, 0x5Cu, 0xDCu)]
    [InlineData(52u, 0x27u, 0xDEu)]
    [InlineData(53u, 0x60u, 0xC0u)]
    [InlineData(54u, 0x2Cu, 0xBCu)]
    [InlineData(55u, 0x2Eu, 0xBEu)]
    [InlineData(56u, 0x2Fu, 0xBFu)]
    [InlineData(56u, 0x3Fu, 0xBFu)]
    [InlineData(100u, 0x3Cu, 0xE2u)] // the key between left Shift and Z on a European keyboard
    public void A_letter_or_a_digit_follows_the_layout_and_the_rest_its_place(uint scancode, uint keycode, uint vk) =>
        Assert.Equal(vk, SdlKeys.VirtualKey(scancode, keycode, 0));

    [Theory]
    [InlineData(89u, true, 0x61u)] // keypad 1
    [InlineData(97u, true, 0x69u)] // keypad 9
    [InlineData(98u, true, 0x60u)] // keypad 0
    [InlineData(99u, true, 0x6Eu)] // keypad .
    [InlineData(89u, false, Vk.End)]
    [InlineData(90u, false, Vk.Down)]
    [InlineData(91u, false, Vk.PageDown)]
    [InlineData(92u, false, Vk.Left)]
    [InlineData(93u, false, 0x0Cu)]
    [InlineData(94u, false, Vk.Right)]
    [InlineData(95u, false, Vk.Home)]
    [InlineData(96u, false, Vk.Up)]
    [InlineData(97u, false, Vk.PageUp)]
    [InlineData(98u, false, 0x2Du)]
    [InlineData(99u, false, 0x2Eu)]
    public void The_keypad_follows_num_lock(uint scancode, bool numLock, uint vk) =>
        Assert.Equal(vk, SdlKeys.VirtualKey(scancode, scancode | Mask, numLock ? SdlKeys.ModNum : (ushort)0));

    [Theory]
    [InlineData((ushort)0x0000, 0u)]
    [InlineData((ushort)0x0001, HotkeyGesture.ModShift)]
    [InlineData((ushort)0x0002, HotkeyGesture.ModShift)]
    [InlineData((ushort)0x0040, HotkeyGesture.ModControl)]
    [InlineData((ushort)0x0080, HotkeyGesture.ModControl)]
    [InlineData((ushort)0x0100, HotkeyGesture.ModAlt)]
    [InlineData((ushort)0x0200, HotkeyGesture.ModAlt)]
    [InlineData((ushort)0x0400, HotkeyGesture.ModWin)]
    [InlineData((ushort)0x0800, HotkeyGesture.ModWin)]
    [InlineData((ushort)0x0141, HotkeyGesture.ModControl | HotkeyGesture.ModAlt | HotkeyGesture.ModShift)]
    [InlineData((ushort)0x7000, 0u)] // Num Lock, Caps Lock and AltGr are no modifiers of a shortcut
    public void The_modifiers_become_the_flags_of_a_shortcut(ushort mod, uint flags) =>
        Assert.Equal(flags, SdlKeys.Modifiers(mod));

    [Fact]
    public void Every_key_a_shortcut_can_name_comes_out_of_some_sdl_key()
    {
        var reached = new HashSet<uint>();
        for (uint scancode = 0; scancode < 512; scancode++)
        {
            foreach (var mod in new ushort[] { 0, SdlKeys.ModNum })
            {
                reached.Add(SdlKeys.VirtualKey(scancode, scancode | Mask, mod));
            }
        }

        for (uint c = 'a'; c <= 'z'; c++) reached.Add(SdlKeys.VirtualKey(4, c, 0));
        for (uint c = '0'; c <= '9'; c++) reached.Add(SdlKeys.VirtualKey(30, c, 0));
        foreach (var vk in reached.Where(v => v != 0))
        {
            Assert.True(vk < 0x100, $"0x{vk:X} is no virtual key");
        }

        for (uint vk = 1; vk < 0x100; vk++)
        {
            if (HotkeyGesture.TryCreate(HotkeyGesture.ModControl, vk, out var gesture))
            {
                Assert.True(reached.Contains(vk), $"{gesture} has no key of SDL");
            }
        }
    }

    [Fact]
    public void A_ctrl_shortcut_typed_on_sdl_reads_as_on_windows()
    {
        var vk = SdlKeys.VirtualKey(SdlKeys.ScanF1 + 9, (SdlKeys.ScanF1 + 9) | Mask, 0);
        var modifiers = SdlKeys.Modifiers(SdlKeys.ModLCtrl | SdlKeys.ModRShift);
        Assert.True(HotkeyGesture.TryCreate(modifiers, vk, out var gesture));
        Assert.Equal("Ctrl+Shift+F10", gesture.ToString());
    }
}
