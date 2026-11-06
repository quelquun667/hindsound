using Hindsound.Input;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Alt+Space or Alt+letter typed for a hotkey: the capture ends on the key down, and its character is still to come.</summary>
public class CaptureKeysTests
{
    [Theory]
    [InlineData(Vk.Space)]
    [InlineData('X')]
    public void The_key_that_ends_the_capture_stays_away_from_Windows(uint vk)
    {
        using var rig = new ScreenRig();
        rig.FocusOn("hotkey.SaveDefault");
        rig.Press(Vk.Enter);
        Assert.True(rig.Screen.Listening);
        var keys = new CaptureKeys();

        keys.KeyDown(rig.Screen.Listening, repeat: false);
        rig.Screen.Key(vk, HotkeyGesture.ModAlt);

        Assert.False(rig.Screen.Listening);
        Assert.True(keys.Swallow);
    }

    [Fact]
    public void Holding_that_key_changes_nothing()
    {
        var keys = new CaptureKeys();
        keys.KeyDown(listening: true, repeat: false);

        keys.KeyDown(listening: false, repeat: true);

        Assert.True(keys.Swallow);
    }

    [Fact]
    public void The_next_key_is_Windows_again()
    {
        var keys = new CaptureKeys();
        keys.KeyDown(listening: true, repeat: false);

        keys.KeyDown(listening: false, repeat: false);

        Assert.False(keys.Swallow);
    }

    [Fact]
    public void A_key_held_from_before_the_capture_is_not_the_capture_s()
    {
        var keys = new CaptureKeys();

        keys.KeyDown(listening: false, repeat: true);

        Assert.False(keys.Swallow);
    }
}
