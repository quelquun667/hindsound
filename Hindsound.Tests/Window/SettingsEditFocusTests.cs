using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Window;

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
}
