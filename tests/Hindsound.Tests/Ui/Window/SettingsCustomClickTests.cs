using Hindsound.Input;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A click on Custom opens a field: what comes next is typing, and it must reach the field.</summary>
public sealed class SettingsCustomClickTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private void Click(float x, float y)
    {
        Screen.MouseDown(x, y);
        Screen.MouseUp(x, y);
    }

    private void AssertTheFieldHasTheKeyboard(string id)
    {
        Assert.Equal(id, Screen.FocusId);
        Assert.True(_rig.Host.EditVisible);
        Assert.Equal(1, _rig.Host.FocusRequests);
    }

    [Fact]
    public void A_click_on_the_custom_length_gives_the_field_the_keyboard()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");
        var box = Screen.Focus!.Bounds;

        Click(box.Right - 2, box.MidY - Screen.Scroll);

        AssertTheFieldHasTheKeyboard("durations.custom");
        Screen.EditChanged("1:30");
        Screen.Key(Vk.Enter, 0);
        Assert.Equal(90, _rig.Settings.ClipSeconds);
    }

    [Fact]
    public void A_click_on_the_custom_color_gives_the_field_the_keyboard()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.accent");
        var box = Screen.Focus!.Bounds;
        var y = box.Y + 10 - Screen.Scroll;

        // From the right: Custom is the last one, and the presets must stay untouched.
        for (var x = box.Right; x > box.X && Screen.FocusId != "general.hex"; x -= 3)
        {
            Click(x, y);
        }

        AssertTheFieldHasTheKeyboard("general.hex");
    }

    [Fact]
    public void A_click_on_a_preset_length_stays_on_the_group()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");
        var box = Screen.Focus!.Bounds;

        Click(box.X + 2, box.MidY - Screen.Scroll);

        Assert.Equal("durations.clip", Screen.FocusId);
        Assert.Equal(15, _rig.Settings.ClipSeconds);
    }

    [Fact]
    public void The_arrows_go_past_custom_without_leaving_the_group()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");

        _rig.Press(Vk.End);

        Assert.Equal("durations.clip", Screen.FocusId);
        _rig.Press(Vk.Left);
        Assert.Equal("durations.clip", Screen.FocusId);
    }

    [Fact]
    public void After_a_click_the_keyboard_does_not_count_as_a_click()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");
        var box = Screen.Focus!.Bounds;
        Click(box.X + 2, box.MidY - Screen.Scroll);

        _rig.Press(Vk.End);

        Assert.Equal("durations.clip", Screen.FocusId);
    }
}
