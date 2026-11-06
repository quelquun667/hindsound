using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Everything the window does on the keyboard, driven as the window would, with no window.</summary>
public sealed class SettingsScreenTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    private const uint CtrlAlt = HotkeyGesture.ModControl | HotkeyGesture.ModAlt;

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    // ---------------------------------------------------------------- tab order

    [Fact]
    public void Tab_goes_from_the_band_through_the_side_bar_and_the_page_to_the_foot()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.ShowSection(Section.Durations);

        Assert.Equal(["band.pause", "band.save", "nav", "durations.clip", "durations.keep", "foot.open"], Screen.StopIds());
    }

    [Fact]
    public void The_copy_button_is_a_stop_once_there_is_a_clip()
    {
        Screen.SetLastClip(new LastClipInfo(TimeSpan.FromSeconds(30), DateTime.Now, 1000));

        Assert.Contains("foot.copy", Screen.StopIds());
    }

    [Fact]
    public void Tab_and_shift_tab_go_round()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.ShowSection(Section.Durations);
        Assert.Equal("nav", Screen.FocusId);

        Screen.Key(Vk.Tab, 0);
        Assert.Equal("durations.clip", Screen.FocusId);

        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Assert.Equal("nav", Screen.FocusId);

        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Assert.Equal("band.pause", Screen.FocusId);

        Screen.Key(Vk.Tab, HotkeyGesture.ModShift);
        Assert.Equal("foot.open", Screen.FocusId);
    }

    [Fact]
    public void Tab_skips_what_is_disabled()
    {
        Screen.ShowSection(Section.Export);
        Assert.Contains("export.bitrate", Screen.StopIds());

        _rig.Settings.Export.Format = "wav";
        Screen.UpdateStatus(ScreenRig.Capturing());

        Assert.DoesNotContain("export.bitrate", Screen.StopIds());
    }

    [Fact]
    public void Every_setting_of_every_section_can_be_reached_with_tab()
    {
        Screen.ShowSection(Section.Audio);
        Assert.Equal(["audio.record", "audio.output", "audio.mic", "audio.mode", "audio.systemGain", "audio.micGain"], PageStops());

        Screen.ShowSection(Section.Export);
        Assert.Equal(["export.format", "export.bitrate", "export.normalization", "export.browse", "export.template", "export.copy"], PageStops());

        Screen.ShowSection(Section.General);
        Assert.Equal(["general.accent", "general.startup", "general.notifications", "general.position", "general.fullscreen", "general.sound", "general.test"], PageStops());

        Screen.ShowSection(Section.Hotkeys);
        Assert.Equal(
            ["hotkey.SaveDefault", "hotkey.Save15s", "hotkey.Save1m", "hotkey.Save5m", "hotkey.TogglePause", "hotkey.CopyLast", "hotkey.reset"],
            PageStops());
    }

    private List<string> PageStops() =>
        Screen.StopIds().Where(id => id != "nav" && !id.StartsWith("band.", StringComparison.Ordinal) && !id.StartsWith("foot.", StringComparison.Ordinal)).ToList();

    [Fact]
    public void Tab_on_a_hidden_widget_scrolls_it_into_view()
    {
        Screen.ShowSection(Section.General);
        Assert.True(Screen.MaxScroll > 0);
        Assert.Equal(0, Screen.Scroll);

        _rig.FocusOn("general.test");

        Assert.True(Screen.Scroll > 0);
        Assert.True(Screen.Focus!.Bounds.Bottom - Screen.Scroll <= SettingsScreen.ViewBottom);
    }

    // ---------------------------------------------------------------- side bar and groups

    [Fact]
    public void The_arrows_change_the_section_and_the_focus_stays_on_the_side_bar()
    {
        Assert.Equal(Section.Hotkeys, Screen.Section);

        _rig.Press(Vk.Down, Vk.Down);
        Assert.Equal(Section.Audio, Screen.Section);
        Assert.Equal("nav", Screen.FocusId);

        _rig.Press(Vk.End);
        Assert.Equal(Section.General, Screen.Section);

        _rig.Press(Vk.Home, Vk.Up);
        Assert.Equal(Section.Hotkeys, Screen.Section);
    }

    [Fact]
    public void The_arrows_move_the_choice_of_a_group()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");

        _rig.Press(Vk.Right);
        Assert.Equal(60, _rig.Settings.ClipSeconds);

        _rig.Press(Vk.Left, Vk.Left, Vk.Left);
        Assert.Equal(15, _rig.Settings.ClipSeconds);
        Assert.Equal(SettingsChange.Clip, _rig.Backend.AllChanges);
    }

    [Fact]
    public void A_group_does_not_move_past_its_ends()
    {
        Screen.ShowSection(Section.Export);
        _rig.FocusOn("export.format");

        _rig.Press(Vk.Right, Vk.Right);

        Assert.Equal("mp3", _rig.Settings.Export.Format);
        Assert.Empty(_rig.Backend.Changes);
    }

    // ---------------------------------------------------------------- durations

    [Fact]
    public void Custom_opens_a_field_that_keeps_a_valid_value_when_the_focus_leaves()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");
        _rig.Press(Vk.End);
        Assert.Equal(30, _rig.Settings.ClipSeconds);

        Screen.Key(Vk.Tab, 0);
        Assert.Equal("durations.custom", Screen.FocusId);
        Assert.True(_rig.Host.EditVisible);
        Assert.Equal("0:30", _rig.Host.EditText);

        Screen.EditChanged("1:30");
        Assert.Equal(30, _rig.Settings.ClipSeconds);

        Screen.Key(Vk.Tab, 0);
        Assert.Equal(90, _rig.Settings.ClipSeconds);
        Assert.False(_rig.Host.EditVisible);
    }

    [Fact]
    public void An_invalid_custom_value_stays_flagged_and_is_not_kept()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);

        Screen.EditChanged("nine");
        Screen.Key(Vk.Enter, 0);
        Screen.Key(Vk.Tab, 0);

        Assert.Equal(30, _rig.Settings.ClipSeconds);
        var field = Assert.IsType<TextField>(Find("durations.custom"));
        Assert.Equal(SettingsEditor.CustomClipError, field.State.Message);
        Assert.Equal("nine", field.State.Draft);
    }

    private Widget? Find(string id)
    {
        var previous = Screen.FocusId;
        _rig.FocusOn(id);
        var widget = Screen.Focus;
        _rig.FocusOn(previous!);
        return widget;
    }

    [Fact]
    public void A_preset_closes_the_custom_field()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.clip");
        _rig.Press(Vk.End);
        Assert.Contains("durations.custom", Screen.StopIds());

        _rig.Press(Vk.Home);

        Assert.DoesNotContain("durations.custom", Screen.StopIds());
        Assert.Equal(15, _rig.Settings.ClipSeconds);
    }

    [Fact]
    public void The_slider_shows_the_value_at_once_and_keeps_it_when_the_key_comes_up()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.keep");

        Screen.Key(Vk.Right, 0);
        Assert.Equal(300, _rig.Settings.BufferSeconds);
        Assert.Empty(_rig.Backend.Changes);

        Screen.KeyUp(Vk.Right, 0);
        Assert.Equal(360, _rig.Settings.BufferSeconds);
        Assert.Equal(SettingsChange.Buffer, _rig.Backend.Changes.Single());
    }

    [Fact]
    public void The_slider_stops_at_the_longest_clip()
    {
        _rig.Settings.ClipSeconds = 90;
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.keep");

        _rig.Press(Vk.Home);

        Assert.Equal(90, _rig.Settings.BufferSeconds);
    }

    [Fact]
    public void A_slider_being_moved_counts_if_the_window_loses_the_keyboard()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.keep");

        Screen.Key(Vk.End, 0);
        Screen.Deactivated();

        Assert.Equal(600, _rig.Settings.BufferSeconds);
    }

    [Fact]
    public void Dragging_the_slider_writes_only_at_the_end()
    {
        Screen.ShowSection(Section.Durations);
        _rig.FocusOn("durations.keep");
        var box = Screen.Focus!.Bounds;

        Screen.MouseDown(box.X + 50, box.MidY);
        Screen.MouseMove(box.X + 300, box.MidY);
        Assert.Empty(_rig.Backend.Changes);
        Assert.True(_rig.Host.MouseHeld);

        Screen.MouseUp(box.X + 300, box.MidY);
        Assert.False(_rig.Host.MouseHeld);
        Assert.Single(_rig.Backend.Changes);
        Assert.NotEqual(300, _rig.Settings.BufferSeconds);
        Assert.Equal(0, _rig.Settings.BufferSeconds % 60);
    }

    // ---------------------------------------------------------------- lists

    [Fact]
    public void Space_opens_a_list_the_arrows_move_and_enter_chooses()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.sound");

        _rig.Press(Vk.Space);
        Assert.True(Screen.PopupOpen);
        Assert.Equal("whenNoOverlay", _rig.Settings.General.ConfirmSound);

        _rig.Press(Vk.Down, Vk.Down);
        Assert.Equal("whenNoOverlay", _rig.Settings.General.ConfirmSound);

        _rig.Press(Vk.Enter);
        Assert.False(Screen.PopupOpen);
        Assert.Equal("never", _rig.Settings.General.ConfirmSound);
        Assert.Equal("general.sound", Screen.FocusId);
    }

    [Fact]
    public void Escape_closes_a_list_without_choosing()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.fullscreen");

        _rig.Press(Vk.Enter, Vk.Down, Vk.Escape);

        Assert.False(Screen.PopupOpen);
        Assert.Equal("auto", _rig.Settings.General.FullscreenGames);
    }

    [Fact]
    public void Tab_leaves_an_open_list_for_the_next_control()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.fullscreen");

        _rig.Press(Vk.Enter, Vk.Tab);

        Assert.False(Screen.PopupOpen);
        Assert.Equal("general.sound", Screen.FocusId);
    }

    [Fact]
    public void The_arrows_on_a_closed_list_change_its_value()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.fullscreen");

        _rig.Press(Vk.Down);
        Assert.Equal("soundOnly", _rig.Settings.General.FullscreenGames);

        _rig.Press(Vk.Up);
        Assert.Equal("auto", _rig.Settings.General.FullscreenGames);
    }

    [Fact]
    public void A_click_outside_a_list_only_closes_it()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.fullscreen");
        _rig.Press(Vk.Enter);

        Screen.MouseDown(5, 5);

        Assert.False(Screen.PopupOpen);
        Assert.Equal("general.fullscreen", Screen.FocusId);
    }

    [Fact]
    public void Choosing_off_turns_the_microphone_off_through_the_devices()
    {
        Screen.ShowSection(Section.Audio);
        _rig.FocusOn("audio.mic");

        _rig.Press(Vk.End);

        Assert.Equal(AudioSettings.DisabledMic, _rig.Settings.Audio.MicDeviceId);
        Assert.Equal(SettingsChange.Devices, _rig.Backend.Changes.Single());
    }

    [Fact]
    public void The_record_list_chooses_an_app_by_the_name_of_its_exe()
    {
        Screen.SetChoices([], [], [new AudioAppChoice("Discord.exe", "Discord")]);
        Screen.ShowSection(Section.Audio);
        _rig.FocusOn("audio.record");

        _rig.Press(Vk.Down);
        Assert.Equal("Discord.exe", _rig.Settings.Audio.SourceApp);
        Assert.Equal(SettingsChange.SourceApp, _rig.Backend.Changes.Single());

        _rig.Press(Vk.Up);
        Assert.Null(_rig.Settings.Audio.SourceApp);
    }

    [Fact]
    public void The_output_has_no_say_while_one_app_is_recorded()
    {
        Screen.SetChoices([], [], [new AudioAppChoice("Discord.exe", "Discord")]);
        Screen.ShowSection(Section.Audio);
        Assert.Contains("audio.output", Screen.StopIds());
        _rig.FocusOn("audio.record");

        _rig.Press(Vk.Down);
        Assert.DoesNotContain("audio.output", Screen.StopIds());

        _rig.Press(Vk.Up);
        Assert.Contains("audio.output", Screen.StopIds());
    }

    [Fact]
    public void An_open_list_picks_what_it_shows_even_if_the_apps_changed_meanwhile()
    {
        Screen.SetChoices([], [], [new AudioAppChoice("Discord.exe", "Discord")]);
        Screen.ShowSection(Section.Audio);
        _rig.FocusOn("audio.record");
        _rig.Press(Vk.Enter);
        Assert.True(Screen.PopupOpen);

        // Coming back to the window lists the apps again, and this answer arrives once the list is open.
        Screen.SetChoices([], [], [new AudioAppChoice("Game.exe", "Game"), new AudioAppChoice("Discord.exe", "Discord")]);
        _rig.Press(Vk.Down, Vk.Enter);

        Assert.False(Screen.PopupOpen);
        Assert.Equal("Discord.exe", _rig.Settings.Audio.SourceApp);
    }

    [Fact]
    public void A_gain_moves_by_one_db_and_is_kept()
    {
        Screen.ShowSection(Section.Audio);
        _rig.FocusOn("audio.systemGain");

        _rig.Press(Vk.Right, Vk.Right, Vk.Left);

        Assert.Equal(1, _rig.Settings.Audio.SystemGainDb);
    }

    [Fact]
    public void Without_a_microphone_its_gain_is_not_a_stop()
    {
        _rig.Settings.Audio.MicDeviceId = AudioSettings.DisabledMic;
        Screen.ShowSection(Section.Audio);

        Assert.DoesNotContain("audio.micGain", Screen.StopIds());
    }

    // ---------------------------------------------------------------- microphone level

    [Fact]
    public void The_meter_only_runs_on_the_audio_page_with_a_microphone_already_capturing()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Assert.False(_rig.Host.MeterOn);

        Screen.ShowSection(Section.Audio);
        Assert.True(_rig.Host.MeterOn);

        Screen.ShowSection(Section.General);
        Assert.False(_rig.Host.MeterOn);
    }

    [Fact]
    public void The_meter_never_runs_with_the_microphone_off()
    {
        _rig.Settings.Audio.MicDeviceId = AudioSettings.DisabledMic;
        Screen.UpdateStatus(ScreenRig.Capturing(false, micEnabled: false));
        Screen.ShowSection(Section.Audio);

        Assert.False(_rig.Host.MeterOn);
    }

    [Fact]
    public void The_meter_stops_when_the_capture_of_the_microphone_stops()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.ShowSection(Section.Audio);
        Assert.True(_rig.Host.MeterOn);

        Screen.UpdateStatus(ScreenRig.Capturing(false, CaptureActivity.Paused));

        Assert.False(_rig.Host.MeterOn);
    }

    [Fact]
    public void A_silent_meter_does_not_repaint()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.ShowSection(Section.Audio);
        var before = _rig.Host.Invalidated;

        Screen.MeterTick(0);

        Assert.Equal(before, _rig.Host.Invalidated);
    }

    // ---------------------------------------------------------------- hotkeys

    [Fact]
    public void Enter_starts_listening_with_the_hotkeys_suspended()
    {
        _rig.FocusOn("hotkey.Save15s");

        _rig.Press(Vk.Enter);

        Assert.True(Screen.Listening);
        Assert.Equal(1, _rig.Backend.Suspends);
        Assert.Empty(_rig.Native.Registered);
    }

    [Fact]
    public void A_new_combination_is_kept_registered_and_written()
    {
        _rig.FocusOn("hotkey.Save15s");
        _rig.Press(Vk.Enter);

        Screen.Key('H', CtrlAlt);

        Assert.False(Screen.Listening);
        Assert.Equal("Ctrl+Alt+H", _rig.Settings.Hotkeys.Save15s);
        Assert.True(_rig.Native.Holds(CtrlAlt, 'H'));
        Assert.True(_rig.Native.Holds(HotkeyGesture.ModControl | HotkeyGesture.ModShift, 0x79));
        Assert.Contains(SettingsChange.Hotkeys, _rig.Backend.Changes);
    }

    [Fact]
    public void Escape_cancels_and_every_hotkey_comes_back()
    {
        _rig.FocusOn("hotkey.SaveDefault");
        _rig.Press(Vk.Enter);
        Assert.Empty(_rig.Native.Registered);

        Screen.Key(Vk.Escape, 0);

        Assert.False(Screen.Listening);
        Assert.Equal(HotkeySettings.DefaultSaveShortcut, _rig.Settings.Hotkeys.SaveDefault);
        Assert.Single(_rig.Native.Registered);
        Assert.Empty(_rig.Backend.Changes);
    }

    [Fact]
    public void Backspace_clears_an_optional_hotkey()
    {
        _rig.Settings.Hotkeys.CopyLast = "Ctrl+Alt+C";
        _rig.Manager.Apply(_rig.Settings.Hotkeys);
        _rig.FocusOn("hotkey.CopyLast");
        _rig.Press(Vk.Enter);

        Screen.Key(Vk.Back, 0);

        Assert.Null(_rig.Settings.Hotkeys.CopyLast);
        Assert.False(_rig.Native.Holds(CtrlAlt, 'C'));
    }

    [Fact]
    public void Backspace_on_the_main_hotkey_stays_listening_with_a_message()
    {
        _rig.FocusOn("hotkey.SaveDefault");
        _rig.Press(Vk.Enter);

        Screen.Key(Vk.Back, 0);

        Assert.True(Screen.Listening);
        Assert.Equal(HotkeySettings.DefaultSaveShortcut, _rig.Settings.Hotkeys.SaveDefault);
        Assert.Contains(HotkeyCapture.CannotClearMain, DrawnText());
    }

    [Fact]
    public void A_combination_held_by_another_app_says_so_and_keeps_the_old_hotkey_working()
    {
        _rig.Native.OtherApps.Add((CtrlAlt, 'H'));
        _rig.FocusOn("hotkey.SaveDefault");
        _rig.Press(Vk.Enter);

        Screen.Key('H', CtrlAlt);

        Assert.Equal(HotkeySettings.DefaultSaveShortcut, _rig.Settings.Hotkeys.SaveDefault);
        Assert.True(_rig.Native.Holds(HotkeyGesture.ModControl | HotkeyGesture.ModShift, 0x79));
        Assert.Contains(HotkeyResult.InUseMessage, DrawnText());
        Assert.Contains("H", DrawnText());
        Assert.Empty(_rig.Backend.Changes);
    }

    [Fact]
    public void A_duplicate_says_so_and_changes_nothing()
    {
        _rig.FocusOn("hotkey.Save15s");
        _rig.Press(Vk.Enter);

        Screen.Key(0x79, HotkeyGesture.ModControl | HotkeyGesture.ModShift);

        Assert.Null(_rig.Settings.Hotkeys.Save15s);
        Assert.Single(_rig.Native.Registered);
        Assert.Contains(HotkeyResult.DuplicateMessage, DrawnText());
        Assert.Empty(_rig.Backend.Changes);
    }

    [Fact]
    public void A_hotkey_the_app_could_not_register_at_start_is_flagged_on_its_row()
    {
        Screen.SetHotkeyResults([new HotkeyResult(HotkeyAction.SaveDefault, "Ctrl+Shift+F10", HotkeyStatus.Unavailable, HotkeyResult.UnavailableMessage)]);

        Assert.Contains(HotkeyResult.UnavailableMessage, DrawnText());
    }

    [Fact]
    public void Tab_while_listening_cancels_and_moves_on()
    {
        _rig.FocusOn("hotkey.Save15s");
        _rig.Press(Vk.Enter);

        Screen.Key(Vk.Tab, 0);

        Assert.False(Screen.Listening);
        Assert.Equal("hotkey.Save1m", Screen.FocusId);
        Assert.Equal(1, _rig.Backend.Resumes);
    }

    [Fact]
    public void Losing_the_keyboard_or_closing_never_leaves_the_hotkeys_suspended()
    {
        _rig.FocusOn("hotkey.Save15s");
        _rig.Press(Vk.Enter);

        Screen.Deactivated();

        Assert.False(Screen.Listening);
        Assert.Single(_rig.Native.Registered);

        _rig.Press(Vk.Enter);
        Assert.True(Screen.Listening);

        Screen.Closing();

        Assert.False(Screen.Listening);
        Assert.Single(_rig.Native.Registered);
    }

    [Fact]
    public void Reset_comes_back_to_the_defaults()
    {
        _rig.Settings.Hotkeys.SaveDefault = "Ctrl+Alt+H";
        _rig.Settings.Hotkeys.CopyLast = "Ctrl+Alt+C";
        _rig.Manager.Apply(_rig.Settings.Hotkeys);
        _rig.FocusOn("hotkey.reset");

        _rig.Press(Vk.Enter);

        Assert.Equal(HotkeySettings.DefaultSaveShortcut, _rig.Settings.Hotkeys.SaveDefault);
        Assert.Null(_rig.Settings.Hotkeys.CopyLast);
        Assert.Single(_rig.Native.Registered);
    }

    [Fact]
    public void A_click_starts_listening_and_a_click_elsewhere_cancels()
    {
        var box = FieldBox("hotkey.Save1m");

        Screen.MouseDown(box.MidX, box.MidY);
        Screen.MouseUp(box.MidX, box.MidY);
        Assert.True(Screen.Listening);

        Screen.MouseDown(5, 5);
        Screen.MouseUp(5, 5);
        Assert.False(Screen.Listening);
        Assert.Single(_rig.Native.Registered);
    }

    private Box FieldBox(string id)
    {
        _rig.FocusOn(id);
        return Screen.Focus!.Bounds;
    }

    private List<string> DrawnText()
    {
        var canvas = new FakeCanvas();
        Screen.Paint(canvas);
        return canvas.Drawn;
    }

    // ---------------------------------------------------------------- general and export

    [Fact]
    public void The_arrows_choose_a_colour_and_it_applies_at_once()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.accent");

        _rig.Press(Vk.Right, Vk.Right);

        Assert.Equal("#35D6FF", _rig.Settings.General.AccentColor);
        Assert.Equal(SettingsChange.Accent, _rig.Backend.AllChanges);
    }

    [Fact]
    public void The_preview_of_the_notification_follows_the_colour()
    {
        Screen.ShowSection(Section.General);
        var canvas = new FakeCanvas();

        Screen.Editor.SetAccentPreset(AccentPreset.Pink);
        Screen.Paint(canvas);

        Assert.Equal(0xFF6FB5u, canvas.NotificationAccent);
    }

    [Fact]
    public void A_custom_colour_applies_while_typing_and_a_dark_one_is_refused_with_its_message()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.accent");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);
        Assert.Equal("general.hex", Screen.FocusId);

        Screen.EditChanged("#35D6FF");
        Assert.Equal("#35D6FF", _rig.Settings.General.AccentColor);

        Screen.EditChanged("#202020");
        Assert.Equal("#35D6FF", _rig.Settings.General.AccentColor);
        Assert.Contains(SettingsEditor.AccentDarkError, DrawnText());
    }

    [Fact]
    public void Escape_in_a_field_gives_back_what_is_stored()
    {
        Screen.ShowSection(Section.Export);
        _rig.FocusOn("export.template");

        Screen.EditChanged("a/b");
        Assert.Contains(SettingsEditor.TemplateCharsError, DrawnText());

        Screen.Key(Vk.Escape, 0);

        Assert.Equal(ExportSettings.DefaultTemplate, _rig.Host.EditText);
        Assert.DoesNotContain(SettingsEditor.TemplateCharsError, DrawnText());
    }

    [Fact]
    public void The_name_template_applies_while_typing_with_its_preview()
    {
        Screen.Clock = () => new DateTime(2026, 11, 6, 14, 32, 5);
        Screen.ShowSection(Section.Export);
        _rig.FocusOn("export.template");

        Screen.EditChanged("clip {date}");

        Assert.Equal("clip {date}", _rig.Settings.Export.FileNameTemplate);
        Assert.Contains("clip 2026-11-06.mp3", DrawnText());
    }

    [Fact]
    public void Browse_keeps_the_folder_chosen_and_backing_out_changes_nothing()
    {
        Screen.ShowSection(Section.Export);
        _rig.FocusOn("export.browse");

        _rig.Press(Vk.Enter);
        Assert.Equal(ExportSettings.DefaultFolder, _rig.Settings.Export.OutputFolder);

        _rig.Host.FolderToPick = @"D:\Clips";
        _rig.Press(Vk.Enter);
        Assert.Equal(@"D:\Clips", _rig.Settings.Export.OutputFolder);
    }

    [Fact]
    public void Space_flips_a_toggle()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.startup");

        _rig.Press(Vk.Space);
        Assert.True(_rig.Settings.General.StartWithWindows);
        Assert.Equal(SettingsChange.Startup, _rig.Backend.Changes.Single());

        _rig.Press(Vk.Enter);
        Assert.False(_rig.Settings.General.StartWithWindows);
    }

    [Fact]
    public void The_band_and_the_foot_buttons_work_from_the_keyboard()
    {
        Screen.SetLastClip(new LastClipInfo(TimeSpan.FromSeconds(30), DateTime.Now, 1000));
        Screen.UpdateStatus(ScreenRig.Capturing());

        _rig.FocusOn("band.pause");
        _rig.Press(Vk.Enter);
        Assert.Equal(1, _rig.Backend.Pauses);

        _rig.FocusOn("band.save");
        _rig.Press(Vk.Space);
        Assert.Equal(1, _rig.Backend.Saves);

        _rig.FocusOn("foot.copy");
        _rig.Press(Vk.Enter);
        Assert.Equal(1, _rig.Backend.Copies);

        _rig.FocusOn("foot.open");
        _rig.Press(Vk.Enter);
        Assert.Equal(1, _rig.Backend.Folders);
    }

    [Fact]
    public void The_test_notification_button_asks_for_one()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.test");

        _rig.Press(Vk.Enter);

        Assert.Equal(1, _rig.Backend.Tests);
    }

    [Fact]
    public void A_key_with_ctrl_or_alt_is_left_to_windows()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.startup");

        Assert.False(Screen.Key(Vk.Space, HotkeyGesture.ModAlt));
        Assert.False(Screen.Key(0x73, HotkeyGesture.ModAlt));
        Assert.False(_rig.Settings.General.StartWithWindows);
    }

    [Fact]
    public void Settings_read_again_from_the_file_replace_what_is_shown()
    {
        var other = new AppSettings().Normalize();
        other.Export.FileNameTemplate = "other";

        Screen.ReplaceSettings(other);
        Screen.ShowSection(Section.Export);

        Assert.Same(other, Screen.Editor.Settings);
        Assert.Contains("other", DrawnText());
    }

    // ---------------------------------------------------------------- first launch

    [Fact]
    public void The_first_launch_goes_through_its_three_steps_on_the_keyboard()
    {
        using var rig = new ScreenRig(firstRun: true);
        var screen = rig.Screen;
        Assert.Equal(Section.FirstRun, screen.Section);
        Assert.Equal("hotkey.SaveDefault", screen.FocusId);
        Assert.Equal(0, screen.FirstRunStep);

        rig.FocusOn("run.next");
        rig.Press(Vk.Enter);
        Assert.Equal(1, screen.FirstRunStep);
        Assert.Equal("run.next", screen.FocusId);
        Assert.Contains("audio.mic", screen.StopIds());

        rig.Press(Vk.Enter);
        Assert.Equal(2, screen.FirstRunStep);

        rig.FocusOn("run.back");
        rig.Press(Vk.Enter);
        Assert.Equal(1, screen.FirstRunStep);

        rig.Press(Vk.Enter);
        rig.FocusOn("run.next");
        rig.Press(Vk.Enter);

        Assert.Equal(Section.Hotkeys, screen.Section);
        Assert.Equal("nav", screen.FocusId);
    }

    [Fact]
    public void The_first_launch_leaves_start_with_windows_off_until_asked()
    {
        using var rig = new ScreenRig(firstRun: true);
        Assert.False(rig.Settings.General.StartWithWindows);

        rig.FocusOn("run.startup");
        rig.Press(Vk.Space);

        Assert.True(rig.Settings.General.StartWithWindows);
    }

    [Fact]
    public void The_first_launch_shows_the_legal_note()
    {
        using var rig = new ScreenRig(firstRun: true);
        var canvas = new FakeCanvas();

        rig.Screen.Paint(canvas);

        Assert.Contains(canvas.Drawn, text => text.Contains("illegal", StringComparison.Ordinal));
    }

    [Fact]
    public void Try_it_says_to_paste_only_after_the_real_clip_is_saved()
    {
        using var rig = new ScreenRig(firstRun: true);
        rig.FocusOn("run.next");
        rig.Press(Vk.Enter, Vk.Enter);
        Assert.Equal(2, rig.Screen.FirstRunStep);
        Assert.False(rig.Screen.FirstRunClipDone);

        var canvas = new FakeCanvas();
        rig.Screen.Paint(canvas);
        Assert.Contains("Say something, then press your hotkey.", canvas.Drawn);
        Assert.DoesNotContain("Now paste it anywhere.", canvas.Drawn);

        rig.Screen.SetLastClip(new LastClipInfo(TimeSpan.FromSeconds(30), DateTime.Now, 1000));

        canvas = new FakeCanvas();
        rig.Screen.Paint(canvas);
        Assert.Contains("Now paste it anywhere.", canvas.Drawn);
    }

    [Fact]
    public void The_meter_of_the_first_launch_is_only_on_its_own_step()
    {
        using var rig = new ScreenRig(firstRun: true);
        rig.Screen.UpdateStatus(ScreenRig.Capturing());
        Assert.False(rig.Host.MeterOn);

        rig.FocusOn("run.next");
        rig.Press(Vk.Enter);
        Assert.True(rig.Host.MeterOn);

        rig.Press(Vk.Enter);
        Assert.False(rig.Host.MeterOn);
    }

    [Fact]
    public void A_field_scrolled_under_the_band_is_drawn_rather_than_shown_as_a_native_box()
    {
        Screen.ShowSection(Section.General);
        _rig.FocusOn("general.accent");
        _rig.Press(Vk.End);
        Screen.Key(Vk.Tab, 0);
        Assert.True(_rig.Host.EditVisible);

        Screen.ScrollTo(float.MaxValue);

        Assert.False(_rig.Host.EditVisible);

        Screen.ScrollTo(0);

        Assert.True(_rig.Host.EditVisible);
    }
}

public class AppOptionsWindowTests
{
    [Fact]
    public void The_window_options_are_read()
    {
        var options = Hindsound.App.AppOptions.Parse(["--dump-settings", @"C:\t\d", "--scale", "1.5", "--settings-probe"]);

        Assert.Equal(@"C:\t\d", options.DumpSettingsFolder);
        Assert.Equal(1.5, options.DumpScale);
        Assert.True(options.SettingsProbe);
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("nope")]
    public void A_scale_that_makes_no_sense_stays_at_100(string scale)
    {
        Assert.Equal(1, Hindsound.App.AppOptions.Parse(["--scale", scale]).DumpScale);
    }
}
