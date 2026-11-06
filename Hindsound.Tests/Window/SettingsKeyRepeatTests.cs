using Hindsound.Input;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Window;

/// <summary>A key held down, widget by widget: Windows sends the same key again with the repeat bit set.</summary>
public sealed class SettingsKeyRepeatTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private void Hold(uint key, int repeats = 4)
    {
        Screen.Key(key, 0);
        for (var i = 0; i < repeats; i++)
        {
            Screen.Key(key, 0, repeat: true);
        }

        Screen.KeyUp(key, 0);
    }

    // ---------------------------------------------------------------- the bit of the message

    [Theory]
    [InlineData(0x00000001L, false)]
    [InlineData(0x001C0001L, false)]
    [InlineData(0x40000001L, true)]
    [InlineData(0x401C0001L, true)]
    [InlineData(0x80000001L, false)]
    [InlineData(unchecked((long)0xFFFFFFFFC0000001UL), true)]
    public void The_repeat_comes_from_bit_30_of_the_message(long lParam, bool repeat)
    {
        Assert.Equal(repeat, KeyRepeat.FromLParam((nint)lParam));
    }

    [Theory]
    [InlineData(Vk.Left)]
    [InlineData(Vk.Right)]
    [InlineData(Vk.Up)]
    [InlineData(Vk.Down)]
    [InlineData(Vk.PageUp)]
    [InlineData(Vk.PageDown)]
    [InlineData(Vk.Home)]
    [InlineData(Vk.End)]
    [InlineData(Vk.Back)]
    public void What_moves_or_erases_repeats_everywhere(uint vk)
    {
        Assert.True(KeyRepeat.Acts(vk, inField: false));
        Assert.True(KeyRepeat.Acts(vk, inField: true));
    }

    [Theory]
    [InlineData(Vk.Enter)]
    [InlineData(Vk.Tab)]
    [InlineData(Vk.Escape)]
    public void What_commits_or_leaves_never_repeats(uint vk)
    {
        Assert.False(KeyRepeat.Acts(vk, inField: false));
        Assert.False(KeyRepeat.Acts(vk, inField: true));
    }

    [Theory]
    [InlineData(Vk.Space)]
    [InlineData('A')]
    public void Typing_repeats_only_in_a_field(uint vk)
    {
        Assert.False(KeyRepeat.Acts(vk, inField: false));
        Assert.True(KeyRepeat.Acts(vk, inField: true));
    }

    // ---------------------------------------------------------------- buttons

    [Theory]
    [InlineData(Vk.Enter)]
    [InlineData(Vk.Space)]
    public void Holding_a_key_on_save_writes_one_clip(uint vk)
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.FocusOn("band.save");

        Hold(vk);

        Assert.Equal(1, _rig.Backend.Saves);
    }

    [Fact]
    public void Holding_enter_on_pause_toggles_once()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.FocusOn("band.pause");

        Hold(Vk.Enter);

        Assert.Equal(1, _rig.Backend.Pauses);
    }

    [Fact]
    public void Holding_enter_on_copy_copies_once()
    {
        Screen.SetLastClip(new LastClipInfo(TimeSpan.FromSeconds(30), DateTime.Now, 1000));
        _rig.FocusOn("foot.copy");

        Hold(Vk.Enter);

        Assert.Equal(1, _rig.Backend.Copies);
    }

    [Fact]
    public void A_second_real_press_on_a_button_still_counts()
    {
        _rig.FocusOn("band.save");

        Hold(Vk.Enter);
        Hold(Vk.Enter);

        Assert.Equal(2, _rig.Backend.Saves);
    }

    [Fact]
    public void Enter_held_on_next_does_not_skip_the_steps_of_the_first_launch()
    {
        using var rig = new ScreenRig(firstRun: true);
        rig.FocusOn("run.next");

        rig.Screen.Key(Vk.Enter, 0);
        rig.Screen.Key(Vk.Enter, 0, repeat: true);
        rig.Screen.Key(Vk.Enter, 0, repeat: true);

        Assert.Equal(1, rig.Screen.FirstRunStep);
    }

    // ---------------------------------------------------------------- toggle

    [Theory]
    [InlineData(Vk.Enter)]
    [InlineData(Vk.Space)]
    public void Holding_a_key_on_a_toggle_switches_it_once(uint vk)
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.startup");
        Assert.False(_rig.Settings.General.StartWithWindows);

        Hold(vk, repeats: 5);

        Assert.True(_rig.Settings.General.StartWithWindows);
        Assert.Single(_rig.Backend.Changes);
    }

    // ---------------------------------------------------------------- list

    [Theory]
    [InlineData(Vk.Enter)]
    [InlineData(Vk.Space)]
    public void Holding_a_key_on_a_list_opens_it_and_leaves_it_open(uint vk)
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.sound");
        var chosen = _rig.Settings.General.ConfirmSound;

        Hold(vk);

        Assert.True(Screen.PopupOpen);
        Assert.Equal(chosen, _rig.Settings.General.ConfirmSound);
    }

    [Fact]
    public void Holding_an_arrow_moves_along_a_closed_list()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.sound");
        Assert.Equal("whenNoOverlay", _rig.Settings.General.ConfirmSound);

        Hold(Vk.Down, repeats: 2);

        Assert.Equal("never", _rig.Settings.General.ConfirmSound);
    }

    [Fact]
    public void Holding_an_arrow_moves_along_an_open_list_and_enter_held_picks_once()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.sound");
        Screen.Key(Vk.Enter, 0);
        Assert.True(Screen.PopupOpen);

        Hold(Vk.Down, repeats: 1);
        Assert.Equal("whenNoOverlay", _rig.Settings.General.ConfirmSound);
        Assert.True(Screen.PopupOpen);

        Screen.Key(Vk.Enter, 0, repeat: true);
        Assert.True(Screen.PopupOpen);

        Hold(Vk.Enter, repeats: 3);
        Assert.Equal("never", _rig.Settings.General.ConfirmSound);
        Assert.False(Screen.PopupOpen);
    }

    // ---------------------------------------------------------------- slider

    [Fact]
    public void Holding_an_arrow_moves_a_slider_all_the_way_and_keeps_it_once()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.keep");
        var start = _rig.Settings.BufferSeconds;

        Hold(Vk.Right, repeats: 2);

        Assert.Equal(start + 3 * SettingsEditor.BufferSliderStep, _rig.Settings.BufferSeconds);
        Assert.Equal(SettingsChange.Buffer, _rig.Backend.Changes.Single());
    }

    [Fact]
    public void Enter_held_on_a_slider_does_nothing()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.keep");
        var start = _rig.Settings.BufferSeconds;

        Hold(Vk.Enter);

        Assert.Equal(start, _rig.Settings.BufferSeconds);
        Assert.Empty(_rig.Backend.Changes);
    }

    // ---------------------------------------------------------------- hotkey field

    [Theory]
    [InlineData(Vk.Enter)]
    [InlineData(Vk.Space)]
    public void The_key_that_opens_a_hotkey_field_is_not_taken_for_the_combo(uint vk)
    {
        _rig.FocusOn("hotkey." + HotkeyAction.Save15s);

        Hold(vk);

        Assert.True(Screen.Listening);
        Assert.Equal(1, _rig.Backend.Suspends);
        Assert.Null(_rig.Settings.Hotkeys.Save15s);
    }

    [Fact]
    public void A_repeat_does_not_set_or_clear_a_hotkey_or_cancel_the_listening()
    {
        _rig.FocusOn("hotkey." + HotkeyAction.SaveDefault);
        var gesture = _rig.Settings.Hotkeys.SaveDefault;
        Screen.Key(Vk.Enter, 0);

        Screen.Key('H', HotkeyGesture.ModControl | HotkeyGesture.ModAlt, repeat: true);
        Screen.Key(Vk.Back, 0, repeat: true);
        Screen.Key(Vk.Escape, 0, repeat: true);
        Screen.Key(Vk.Tab, 0, repeat: true);

        Assert.True(Screen.Listening);
        Assert.Equal(gesture, _rig.Settings.Hotkeys.SaveDefault);

        Screen.Key('H', HotkeyGesture.ModControl | HotkeyGesture.ModAlt);
        Assert.False(Screen.Listening);
        Assert.Equal("Ctrl+Alt+H", _rig.Settings.Hotkeys.SaveDefault);
    }

    // ---------------------------------------------------------------- tab and text field

    [Fact]
    public void Holding_tab_moves_the_focus_once()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("nav");

        Hold(Vk.Tab);

        Assert.Equal("durations.clip", Screen.FocusId);
    }

    [Fact]
    public void Enter_and_escape_held_in_a_field_act_once()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal("durations.custom", Screen.FocusId);
        Screen.EditChanged("1:30");

        Screen.Key(Vk.Enter, 0);
        Assert.Equal(90, _rig.Settings.ClipSeconds);
        var changes = _rig.Backend.Changes.Count;
        Screen.Key(Vk.Enter, 0, repeat: true);
        Assert.Equal(changes, _rig.Backend.Changes.Count);

        Screen.EditChanged("2:00");
        Screen.Key(Vk.Escape, 0, repeat: true);
        Assert.Equal("2:00", Assert.IsType<TextField>(Screen.Focus).State.Draft);
    }

    [Fact]
    public void A_key_held_that_means_nothing_here_goes_back_to_windows()
    {
        Assert.False(Screen.Key(0x70, 0, repeat: true));
        Assert.True(Screen.Key(Vk.Space, 0, repeat: true));
    }
}
