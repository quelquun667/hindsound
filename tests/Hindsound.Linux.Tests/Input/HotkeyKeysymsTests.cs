using Hindsound.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>The keys of settings.json in the words of the portal and of X11, and back. Pure logic: runs everywhere.</summary>
public sealed class HotkeyKeysymsTests
{
    private static HotkeyGesture Parse(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture), text);
        return gesture;
    }

    [Theory]
    [InlineData("Ctrl+Shift+F10", "CTRL+SHIFT+F10")]
    [InlineData("Ctrl+Alt+H", "CTRL+ALT+h")]
    [InlineData("Win+Space", "LOGO+space")]
    [InlineData("Ctrl+PageUp", "CTRL+Page_Up")]
    [InlineData("Ctrl+PageDown", "CTRL+Page_Down")]
    [InlineData("Alt+Enter", "ALT+Return")]
    [InlineData("Shift+Escape", "SHIFT+Escape")]
    [InlineData("Ctrl+Alt+Shift+Win+7", "CTRL+ALT+SHIFT+LOGO+7")]
    [InlineData("F24", "F24")]
    [InlineData("Ctrl+Delete", "CTRL+Delete")]
    public void A_gesture_is_offered_to_the_portal_in_the_words_of_the_shortcuts_specification(string gesture, string trigger)
    {
        Assert.Equal(trigger, HotkeyKeysyms.ToPortalTrigger(Parse(gesture)));
    }

    [Theory]
    [InlineData("CTRL+a", "Ctrl+A")]
    [InlineData("CTRL+SHIFT+a", "Ctrl+Shift+A")]
    [InlineData("CTRL+ALT+Return", "Ctrl+Alt+Enter")]
    [InlineData("ctrl+shift+f10", "Ctrl+Shift+F10")]
    [InlineData("Ctrl+Shift+H", "Ctrl+Shift+H")]
    [InlineData("CTRL+SHIFT+H", "Ctrl+Shift+H")]
    [InlineData("Super+F5", "Win+F5")]
    [InlineData("LOGO+space", "Win+Space")]
    [InlineData("CTRL+Prior", "Ctrl+PageUp")]
    [InlineData("CTRL+Next", "Ctrl+PageDown")]
    [InlineData("Ctrl+PageUp", "Ctrl+PageUp")]
    [InlineData(" CTRL + ALT + Delete ", "Ctrl+Alt+Delete")]
    [InlineData("F1", "F1")]
    public void A_trigger_is_read_back_into_a_gesture(string trigger, string gesture)
    {
        Assert.True(HotkeyKeysyms.TryParseTrigger(trigger, out var read), trigger);
        Assert.Equal(Parse(gesture), read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("CTRL+")]
    [InlineData("NUM+F1")]
    [InlineData("CTRL+CTRL+a")]
    [InlineData("CTRL+F25")]
    [InlineData("CTRL+F0")]
    [InlineData("CTRL+Hyper_L")]
    [InlineData("HYPER+a")]
    [InlineData("Strg+Umschalt+F10")]
    [InlineData("CTRL+ab")]
    public void A_trigger_a_gesture_cannot_hold_is_not_read(string? trigger)
    {
        Assert.False(HotkeyKeysyms.TryParseTrigger(trigger, out _));
    }

    [Fact]
    public void Every_key_a_gesture_can_name_goes_to_a_keysym_and_back()
    {
        var keys = HotkeyKeysyms.KnownVirtualKeys().ToList();
        Assert.Equal(26 + 10 + 24 + 15, keys.Count);
        foreach (var vk in keys)
        {
            var keysym = HotkeyKeysyms.ToKeysym(vk);
            Assert.NotNull(keysym);
            Assert.Equal(vk, HotkeyKeysyms.ToVirtualKey(keysym.Value));
            Assert.NotNull(HotkeyKeysyms.KeysymName(vk));
        }

        Assert.Equal(keys.Count, keys.Select(vk => HotkeyKeysyms.ToKeysym(vk)).Distinct().Count());
    }

    [Fact]
    public void Every_gesture_goes_to_a_trigger_and_back_unchanged()
    {
        uint[] modifierSets = [HotkeyGesture.ModControl, HotkeyGesture.ModAlt | HotkeyGesture.ModShift, HotkeyGesture.ModWin, 0x000F];
        foreach (var vk in HotkeyKeysyms.KnownVirtualKeys())
        {
            foreach (var modifiers in modifierSets)
            {
                Assert.True(HotkeyGesture.TryCreate(modifiers, vk, out var gesture));
                var trigger = HotkeyKeysyms.ToPortalTrigger(gesture);
                Assert.NotNull(trigger);
                Assert.True(HotkeyKeysyms.TryParseTrigger(trigger, out var back), trigger);
                Assert.Equal(gesture, back);

                // The app's own words read back too, as a desktop may echo them.
                Assert.True(HotkeyKeysyms.TryParseTrigger(gesture.ToString(), out var own), gesture.ToString());
                Assert.Equal(gesture, own);
            }
        }
    }

    [Theory]
    [InlineData((uint)'H', 0x68u)]
    [InlineData((uint)'A', 0x61u)]
    [InlineData((uint)'7', 0x37u)]
    [InlineData(0x70u, 0xffbeu)]
    [InlineData(0x79u, 0xffc7u)]
    [InlineData(0x87u, 0xffd5u)]
    [InlineData(0x20u, 0x20u)]
    [InlineData(0x0Du, 0xff0du)]
    [InlineData(0x2Eu, 0xffffu)]
    [InlineData(0x21u, 0xff55u)]
    [InlineData(0x13u, 0xff13u)]
    public void A_virtual_key_has_the_keysym_of_keysymdef(uint virtualKey, uint keysym)
    {
        Assert.Equal(keysym, HotkeyKeysyms.ToKeysym(virtualKey));
    }

    [Fact]
    public void An_uppercase_letter_keysym_is_the_same_key_and_an_unknown_one_is_none()
    {
        Assert.Equal((uint)'Q', HotkeyKeysyms.ToVirtualKey(0x51));
        Assert.Equal((uint)'Q', HotkeyKeysyms.ToVirtualKey(0x71));
        Assert.Null(HotkeyKeysyms.ToVirtualKey(0xffe1));
        Assert.Null(HotkeyKeysyms.ToKeysym(0x5B));
        Assert.Null(HotkeyKeysyms.ToPortalTrigger(new HotkeyGesture(HotkeyGesture.ModControl, 0x5B)));
    }

    [Fact]
    public void The_modifiers_map_to_the_x11_masks_and_back_without_the_locks()
    {
        Assert.Equal(HotkeyKeysyms.X11ControlMask, HotkeyKeysyms.ToX11Modifiers(HotkeyGesture.ModControl));
        Assert.Equal(HotkeyKeysyms.X11Mod1Mask, HotkeyKeysyms.ToX11Modifiers(HotkeyGesture.ModAlt));
        Assert.Equal(HotkeyKeysyms.X11ShiftMask, HotkeyKeysyms.ToX11Modifiers(HotkeyGesture.ModShift));
        Assert.Equal(HotkeyKeysyms.X11Mod4Mask, HotkeyKeysyms.ToX11Modifiers(HotkeyGesture.ModWin));
        Assert.Equal(1u | 4u | 8u | 64u, HotkeyKeysyms.ToX11Modifiers(0x000F));

        var state = HotkeyKeysyms.X11ControlMask | HotkeyKeysyms.X11LockMask | HotkeyKeysyms.X11Mod2Mask;
        Assert.Equal(HotkeyGesture.ModControl, HotkeyKeysyms.FromX11Modifiers(state));
        Assert.Equal(0x000Fu, HotkeyKeysyms.FromX11Modifiers(HotkeyKeysyms.ToX11Modifiers(0x000F)));
    }

    [Fact]
    public void A_grab_is_made_under_every_lock_state_and_a_press_matches_whatever_the_locks()
    {
        var numLock = HotkeyKeysyms.X11Mod2Mask;
        Assert.Equal(new uint[] { 0, 2, 16, 18 }, HotkeyKeysyms.LockVariants(numLock));
        Assert.Equal(new uint[] { 0, 2 }, HotkeyKeysyms.LockVariants(0));

        var ctrlShift = HotkeyKeysyms.X11ControlMask | HotkeyKeysyms.X11ShiftMask;
        foreach (var variant in HotkeyKeysyms.LockVariants(numLock))
        {
            Assert.True(HotkeyKeysyms.StateMatches(ctrlShift | variant, ctrlShift, numLock));
        }

        Assert.False(HotkeyKeysyms.StateMatches(HotkeyKeysyms.X11ControlMask, ctrlShift, numLock));
        Assert.False(HotkeyKeysyms.StateMatches(ctrlShift | HotkeyKeysyms.X11Mod1Mask, ctrlShift, numLock));
    }
}
