using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The native text box and the keyboard: nothing the app reports may move the keyboard, a game may be holding it.</summary>
public sealed class SettingsEditFocusTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private void FocusTheTemplateField()
    {
        Screen.ShowSection(Section.Export);
        _rig.FocusOn("export.template");
        Assert.True(_rig.Host.EditVisible);
    }

    private void NewsFromTheApp()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.UpdateStatus(ScreenRig.Capturing(activity: CaptureActivity.Paused));
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetChoices([new AudioDeviceChoice("out", "Speakers")], [new AudioDeviceChoice("mic", "Headset")], [new AudioAppChoice("Discord.exe", "Discord")]);
        Screen.SetLastClip(new LastClipInfo(TimeSpan.FromSeconds(30), DateTime.Now, 1000));
        Screen.SetHotkeyResults([]);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void The_box_takes_the_keyboard_only_when_it_appears_in_the_foreground(bool wasVisible, bool foreground, bool wanted)
    {
        Assert.Equal(wanted, EditFocus.Wanted(wasVisible, foreground));
    }

    [Fact]
    public void News_from_the_app_never_ask_for_the_keyboard_in_the_background()
    {
        _rig.Host.IsForeground = false;
        FocusTheTemplateField();

        NewsFromTheApp();

        Assert.Equal(0, _rig.Host.FocusRequests);
        Assert.True(_rig.Host.EditVisible);
    }

    [Fact]
    public void News_from_the_app_never_ask_for_the_keyboard_in_the_foreground_either()
    {
        FocusTheTemplateField();
        Assert.Equal(1, _rig.Host.FocusRequests);

        NewsFromTheApp();

        Assert.Equal(1, _rig.Host.FocusRequests);
    }

    [Fact]
    public void Typing_does_not_ask_for_the_keyboard_again()
    {
        FocusTheTemplateField();

        Screen.EditChanged("clip {date}");
        Screen.EditChanged("clip {date}x");

        Assert.Equal(1, _rig.Host.FocusRequests);
    }

    [Fact]
    public void Leaving_a_field_and_coming_back_asks_once_each_time()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.accent");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal("general.hex", Screen.FocusId);
        Assert.Equal(1, _rig.Host.FocusRequests);

        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Assert.False(_rig.Host.EditVisible);
        Screen.Key(Vk.Tab, 0);

        Assert.Equal(2, _rig.Host.FocusRequests);
    }

    [Fact]
    public void A_box_that_comes_back_on_screen_in_the_foreground_asks_again()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.accent");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal(1, _rig.Host.FocusRequests);

        Screen.ScrollTo(float.MaxValue);
        Assert.False(_rig.Host.EditVisible);
        Screen.ScrollTo(0);

        Assert.True(_rig.Host.EditVisible);
        Assert.Equal(2, _rig.Host.FocusRequests);
    }

    [Fact]
    public void A_box_that_comes_back_on_screen_in_the_background_waits()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.accent");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);
        _rig.Host.IsForeground = false;

        Screen.ScrollTo(float.MaxValue);
        Screen.ScrollTo(0);

        Assert.True(_rig.Host.EditVisible);
        Assert.Equal(1, _rig.Host.FocusRequests);
    }

    // ---------------------------------------------------------------- a field scrolled away with the focus on it

    private void TypeThenScrollAway()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.accent");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal("general.hex", Screen.FocusId);
        Screen.EditChanged("#FF88");
        Screen.Wheel(-10);
        Assert.False(_rig.Host.EditVisible);
        Assert.Equal("general.hex", Screen.FocusId);
    }

    private void AssertTheFieldIsBack(int focusRequests)
    {
        Assert.True(_rig.Host.EditVisible);
        Assert.Equal(focusRequests, _rig.Host.FocusRequests);
        Assert.True(Screen.Focus!.Bounds.Y - Screen.Scroll >= SettingsScreen.BandHeight);
        Assert.True(Screen.Focus!.Bounds.Bottom - Screen.Scroll <= SettingsScreen.ViewBottom);
    }

    [Fact]
    public void The_first_key_brings_the_field_back_and_is_left_for_the_box()
    {
        TypeThenScrollAway();

        var used = Screen.Key('A', 0);

        AssertTheFieldIsBack(2);
        Assert.False(used);
    }

    [Fact]
    public void A_key_held_also_brings_the_field_back()
    {
        TypeThenScrollAway();

        Assert.False(Screen.Key('A', 0, repeat: true));

        AssertTheFieldIsBack(2);
    }

    [Fact]
    public void Paste_brings_the_field_back()
    {
        TypeThenScrollAway();

        Assert.False(Screen.Key('V', HotkeyGesture.ModControl));

        AssertTheFieldIsBack(2);
    }

    [Theory]
    [InlineData(Vk.Shift, HotkeyGesture.ModShift)]
    [InlineData(Vk.Ctrl, HotkeyGesture.ModControl)]
    [InlineData(Vk.Alt, HotkeyGesture.ModAlt)]
    [InlineData('X', HotkeyGesture.ModAlt)]
    [InlineData('X', HotkeyGesture.ModWin)]
    public void A_modifier_alone_or_a_system_combination_leaves_the_page_where_it_is(uint vk, uint modifiers)
    {
        TypeThenScrollAway();
        var scroll = Screen.Scroll;

        Screen.Key(vk, modifiers);

        Assert.False(_rig.Host.EditVisible);
        Assert.Equal(scroll, Screen.Scroll);
    }

    [Fact]
    public void Enter_on_a_field_scrolled_away_brings_it_back_and_commits()
    {
        TypeThenScrollAway();
        Screen.EditChanged("nope");

        Assert.True(Screen.Key(Vk.Enter, 0));

        AssertTheFieldIsBack(2);
        Assert.NotNull(Assert.IsType<TextField>(Screen.Focus).State.Message);
    }

    [Fact]
    public void Tab_on_a_field_scrolled_away_moves_on_without_bringing_it_back()
    {
        TypeThenScrollAway();

        Screen.Key(Vk.Tab, 0);

        Assert.Equal("general.startup", Screen.FocusId);
        Assert.False(_rig.Host.EditVisible);
    }

    [Fact]
    public void A_box_that_comes_back_keeps_its_text_and_its_caret()
    {
        TypeThenScrollAway();
        var resets = _rig.Host.EditResets;

        Screen.Key('A', 0);

        Assert.Equal(resets, _rig.Host.EditResets);

        Screen.Wheel(-10);
        Screen.Wheel(10);

        Assert.True(_rig.Host.EditVisible);
        Assert.Equal(resets, _rig.Host.EditResets);
    }

    [Fact]
    public void Leaving_a_field_scrolled_away_and_coming_back_gives_the_box_the_text_again()
    {
        TypeThenScrollAway();
        var resets = _rig.Host.EditResets;

        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Assert.Equal("general.accent", Screen.FocusId);
        Screen.Key(Vk.Tab, 0);

        Assert.Equal("general.hex", Screen.FocusId);
        Assert.True(_rig.Host.EditVisible);
        Assert.Equal(resets + 1, _rig.Host.EditResets);
    }
}
