using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>What the editor asks of the app: every call written down, nothing read or played.</summary>
internal sealed class FakeEditorPanel : IEditorPanel
{
    public List<ClipGroup> Loads { get; } = [];

    public List<EditRequest> Plays { get; } = [];

    public int Stops { get; private set; }

    public List<(EditRequest Request, EditSave Save)> Saves { get; } = [];

    public List<string> Discord { get; } = [];

    public int Closes { get; private set; }

    public long Bytes { get; set; } = 115_000;

    public void Load(ClipGroup clip) => Loads.Add(clip);

    public long EstimateBytes(EditRequest request) => Bytes;

    public void Play(EditRequest request) => Plays.Add(request);

    public void StopPlaying() => Stops++;

    public void Save(EditRequest request, EditSave save) => Saves.Add((request, save));

    public void HandToDiscord(string path) => Discord.Add(path);

    public void Close() => Closes++;
}

/// <summary>The page of the editor: its handles, keys, playback, volumes, soundboard line and the dialog of Save.</summary>
public sealed class EditorPageTests : IDisposable
{
    private const string Name = "Hindsound_Discord_2026-11-08_20-47-51";

    private readonly ScreenRig _rig = new();
    private readonly PlayingLibraryFake _library = new();
    private readonly FakeEditorPanel _panel = new();

    public EditorPageTests()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetLibrary(_library);
        Screen.SetEditor(_panel);
        Screen.ShowSection(Section.Library);
    }

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static ClipGroup OneFile => new([new ClipFile(@"C:\Clips\" + Name + ".mp3", 720_000, default)]);

    private static ClipGroup TwoFiles => new([
        new ClipFile(@"C:\Clips\" + Name + "_system.wav", 720_000, default),
        new ClipFile(@"C:\Clips\" + Name + "_mic.wav", 720_000, default)]);

    private static float[] Peaks => [.. Enumerable.Range(0, 600).Select(i => (float)(0.2 + 0.2 * Math.Sin(i / 7.0)))];

    private void Open(EditorMode mode = EditorMode.Normal, ClipGroup? clip = null, int files = 1, double seconds = 30)
    {
        Screen.OpenEditor(clip ?? OneFile, mode);
        Screen.EditorLoaded(new EditorLoad(EditorLoadOutcome.Loaded, TimeSpan.FromSeconds(seconds), [.. Enumerable.Range(0, files).Select(_ => (IReadOnlyList<float>)Peaks)], ExportFormat.Mp3));
    }

    private EditorSelection Selection => Screen.EditorSelection!;

    private void Click(string id)
    {
        var box = Screen.WidgetById(id)!.Bounds;
        Screen.MouseDown(box.MidX, box.MidY);
        Screen.MouseUp(box.MidX, box.MidY);
    }

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn;
    }

    [Fact]
    public void An_arrow_moves_the_handle_with_the_focus_by_a_tenth_and_Shift_with_it_by_a_second()
    {
        Open();
        _rig.FocusOn(SettingsScreen.EditorStartId);
        _rig.Press(Vk.Right);
        Assert.Equal(100, Selection.StartMs);
        Screen.Key(Vk.Right, HotkeyGesture.ModShift);
        Assert.Equal(1_100, Selection.StartMs);

        _rig.FocusOn(SettingsScreen.EditorEndId);
        Screen.Key(Vk.Left, HotkeyGesture.ModShift);
        _rig.Press(Vk.Left);
        Assert.Equal(28_900, Selection.EndMs);
        Assert.Equal(1_100, Selection.StartMs);
    }

    [Fact]
    public void The_keys_never_bring_the_selection_under_0_2_second()
    {
        Open(seconds: 3);
        _rig.FocusOn(SettingsScreen.EditorStartId);
        for (var i = 0; i < 10; i++)
        {
            Screen.Key(Vk.Right, HotkeyGesture.ModShift);
        }

        Assert.Equal(2_800, Selection.StartMs);
        Assert.Equal(3_000, Selection.EndMs);
    }

    [Fact]
    public void A_drag_of_a_handle_follows_the_mouse_and_stops_at_0_2_second_from_the_other()
    {
        Open();
        var start = Screen.WidgetById(SettingsScreen.EditorStartId)!.Bounds;
        var grabX = start.MidX;
        var timeline = EditorTimeline.Of(EditorWave.Panel, 1, 30_000, 1);
        Screen.MouseDown(grabX, start.MidY);
        var tenSeconds = timeline.XOf(10_000) - timeline.XOf(0);
        Screen.MouseMove(grabX + tenSeconds, start.MidY);
        Assert.InRange(Selection.StartMs, 9_900, 10_100);

        Screen.MouseMove(grabX + 10 * tenSeconds, start.MidY);
        Screen.MouseUp(grabX + 10 * tenSeconds, start.MidY);
        Assert.Equal(29_800, Selection.StartMs);
        Assert.Equal(30_000, Selection.EndMs);
    }

    [Fact]
    public void For_the_soundboard_neither_keys_nor_the_mouse_keep_more_than_5_2_seconds()
    {
        Open(EditorMode.Soundboard);
        Assert.Equal(5_200, Selection.EndMs);
        _rig.FocusOn(SettingsScreen.EditorEndId);
        Screen.Key(Vk.Right, HotkeyGesture.ModShift);
        Assert.Equal(6_200, Selection.EndMs);
        Assert.Equal(1_000, Selection.StartMs);

        var end = Screen.WidgetById(SettingsScreen.EditorEndId)!.Bounds;
        Screen.MouseDown(end.MidX, end.MidY);
        Screen.MouseMove(SettingsScreen.WindowWidth - 1, end.MidY);
        Screen.MouseUp(SettingsScreen.WindowWidth - 1, end.MidY);
        Assert.Equal(30_000, Selection.EndMs);
        Assert.Equal(5_200, Selection.KeptMs);
        Assert.Contains(Soundboard.FitsText, Painted());
    }

    [Fact]
    public void Play_sends_the_part_kept_and_the_volume_and_Stop_stops()
    {
        Open();
        _rig.FocusOn(SettingsScreen.EditorStartId);
        Screen.Key(Vk.Right, HotkeyGesture.ModShift);
        _rig.FocusOn(SettingsScreen.EditorVolumePrefix + "0");
        _rig.Press(Vk.Right, Vk.Right, Vk.Right);
        Click(SettingsScreen.EditorPlayId);

        var request = Assert.Single(_panel.Plays);
        Assert.Equal(EditorMode.Normal, request.Mode);
        Assert.Equal(TimeSpan.FromSeconds(1), request.Start);
        Assert.Equal(TimeSpan.FromSeconds(30), request.End);
        Assert.Equal([3.0], request.GainsDb);

        Screen.EditorPreviewChanged(PreviewState.Playing);
        Assert.Equal("Stop", Painted().Single(text => text is "Play" or "Stop"));
        Click(SettingsScreen.EditorPlayId);
        Assert.Equal(1, _panel.Stops);
    }

    [Fact]
    public void A_clip_in_two_files_has_a_volume_for_each_and_Play_sends_both()
    {
        Open(clip: TwoFiles, files: 2);
        var stops = Screen.StopIds();
        Assert.Contains(SettingsScreen.EditorVolumePrefix + "0", stops);
        Assert.Contains(SettingsScreen.EditorVolumePrefix + "1", stops);
        var drawn = Painted();
        Assert.Contains("PC volume", drawn);
        Assert.Contains("Mic volume", drawn);
        Assert.DoesNotContain("Volume", drawn);

        _rig.FocusOn(SettingsScreen.EditorVolumePrefix + "1");
        _rig.Press(Vk.Left);
        Click(SettingsScreen.EditorPlayId);
        Assert.Equal([0.0, -1.0], Assert.Single(_panel.Plays).GainsDb);
    }

    [Fact]
    public void A_boost_says_where_the_peaks_are_held()
    {
        Open();
        Assert.DoesNotContain(SettingsScreen.HeldText(-1), Painted());
        _rig.FocusOn(SettingsScreen.EditorVolumePrefix + "0");
        _rig.Press(Vk.Right);
        Assert.Contains("Peaks held under -1 dB", Painted());
    }

    [Fact]
    public void The_tab_goes_through_the_handles_Play_the_volume_Cancel_and_Save_in_that_order()
    {
        Open();
        var stops = Screen.StopIds().Where(id => id.StartsWith("edit.", StringComparison.Ordinal) || id.StartsWith("foot.", StringComparison.Ordinal)).ToList();
        Assert.Equal([SettingsScreen.EditorStartId, SettingsScreen.EditorEndId, SettingsScreen.EditorPlayId, SettingsScreen.EditorVolumePrefix + "0", SettingsScreen.EditorCancelId, SettingsScreen.EditorSaveId], stops);
    }

    [Fact]
    public void The_line_turns_amber_past_5_2_seconds_and_back_when_the_part_kept_fits()
    {
        Open();
        Assert.Contains(Soundboard.TooLongText, Painted());
        Assert.Equal(Theme.Amber, _rig.Canvas.TextColors[Soundboard.TooLongText]);
        Assert.DoesNotContain(Soundboard.FitsText, Painted());

        _rig.FocusOn(SettingsScreen.EditorEndId);
        for (var i = 0; i < 25; i++)
        {
            Screen.Key(Vk.Left, HotkeyGesture.ModShift);
        }

        Assert.Equal(5_000, Selection.EndMs);
        var drawn = Painted();
        Assert.Contains(Soundboard.FitsText, drawn);
        Assert.DoesNotContain(Soundboard.TooLongText, drawn);
    }

    [Theory]
    [InlineData(59_960, "1:00.0")]
    [InlineData(119_970, "2:00.0")]
    [InlineData(59_940, "0:59.9")]
    [InlineData(3_200, "0:03.2")]
    public void The_clock_carries_the_rounded_seconds_over_to_the_minute(long ms, string shown)
    {
        Assert.Equal(shown, SettingsScreen.EditClock(ms));
    }

    [Fact]
    public void The_page_is_titled_after_what_it_was_opened_for()
    {
        Open();
        Assert.Contains(SettingsScreen.EditorTitle, Painted());
        Assert.DoesNotContain(SettingsScreen.EditorSoundboardTitle, Painted());

        Screen.CloseEditor();
        Open(EditorMode.Soundboard);
        Assert.Contains(SettingsScreen.EditorSoundboardTitle, Painted());
        Assert.DoesNotContain(SettingsScreen.EditorTitle, Painted());
    }

    [Fact]
    public void The_size_comes_from_the_estimate_of_the_app()
    {
        Open();
        Assert.Contains("about 115 KB", Painted());
        _panel.Bytes = 2_450_000;
        Assert.Contains("about 2.5 MB", Painted());
    }

    [Fact]
    public void Escape_closes_the_dialog_first_then_the_editor()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        Assert.NotNull(Screen.EditorDialog);
        Assert.Equal(EditorSaveDialogState.NameId, Screen.FocusId);

        _rig.Press(Vk.Escape);
        Assert.Null(Screen.EditorDialog);
        Assert.True(Screen.EditorOpen);
        Assert.Equal(0, _panel.Closes);

        _rig.Press(Vk.Escape);
        Assert.False(Screen.EditorOpen);
        Assert.Equal(1, _panel.Closes);
        Assert.Empty(_panel.Saves);
    }

    [Fact]
    public void Tab_stays_in_the_dialog_and_nothing_under_it_takes_the_mouse()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        for (var i = 0; i < 12; i++)
        {
            _rig.Press(Vk.Tab);
            Assert.StartsWith(EditorSaveDialogState.Prefix, Screen.FocusId);
        }

        // The side bar, the band and the handles are only to be seen.
        Screen.MouseDown(60, 300);
        Screen.MouseUp(60, 300);
        Screen.MouseDown(500, 36);
        Screen.MouseUp(500, 36);
        Assert.Equal(Section.Library, Screen.Section);
        Assert.Equal(0, _rig.Backend.Pauses);
        Assert.Null(Screen.WidgetById(SettingsScreen.EditorStartId));
        Assert.NotNull(Screen.EditorDialog);
    }

    [Fact]
    public void Back_from_another_page_the_keys_go_to_the_dialog_again()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        Screen.ShowSection(Section.Now);
        Screen.ShowSection(Section.Library);

        _rig.Press(Vk.Down);
        Assert.Equal(Section.Library, Screen.Section);
        Assert.NotNull(Screen.EditorDialog);
        Assert.Equal(EditorSaveDialogState.NameId, Screen.FocusId);
    }

    [Fact]
    public void The_dialog_saves_a_copy_named_after_the_clip_by_default()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        Assert.Contains(EditorSaveDialogState.CopyHelp, Painted());
        Assert.True(((Button)Screen.WidgetById(EditorSaveDialogState.CopyId)!).Accented);
        Click(EditorSaveDialogState.CopyId);

        var (request, save) = Assert.Single(_panel.Saves);
        Assert.Equal(new EditSave(EditSaveChoice.Copy, Name + " (cut)"), save);
        Assert.Equal(TimeSpan.FromSeconds(30), request.End);
        Assert.Null(Screen.EditorDialog);
        Assert.False(((Button)Screen.WidgetById(SettingsScreen.EditorSaveId)!).Enabled);
    }

    [Fact]
    public void The_dialog_can_replace_the_original_and_refuses_an_empty_name_for_a_copy()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        Screen.EditChanged("   ");
        Assert.False(((Button)Screen.WidgetById(EditorSaveDialogState.CopyId)!).Enabled);
        _rig.Press(Vk.Enter);
        Assert.Empty(_panel.Saves);
        Assert.NotNull(Screen.EditorDialog);

        Click(EditorSaveDialogState.ReplaceId);
        Assert.Equal(new EditSave(EditSaveChoice.Replace, null), Assert.Single(_panel.Saves).Save);
    }

    [Fact]
    public void The_dialog_has_the_name_of_the_copy_then_Cancel_Replace_the_original_and_Save_as_a_copy()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        var drawn = Painted();
        Assert.Contains(EditorSaveDialogState.NameCaption, drawn);
        Assert.Contains(EditorSaveDialogState.CopyHelp, drawn);
        var stops = Screen.StopIds().Where(id => id.StartsWith(EditorSaveDialogState.Prefix, StringComparison.Ordinal));
        Assert.Equal([EditorSaveDialogState.NameId, EditorSaveDialogState.CancelId, EditorSaveDialogState.ReplaceId, EditorSaveDialogState.CopyId], stops);
        Assert.Contains(EditorSaveDialogState.ReplaceLabel, drawn);
        Assert.Contains(EditorSaveDialogState.CopyLabel, drawn);

        // Enter in the name saves the copy, as the button in accent does.
        _rig.Press(Vk.Enter);
        Assert.Equal(new EditSave(EditSaveChoice.Copy, Name + " (cut)"), Assert.Single(_panel.Saves).Save);
    }

    [Fact]
    public void A_normal_save_closes_the_editor_chooses_the_new_clip_and_never_opens_Discord()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        Click(EditorSaveDialogState.CopyId);
        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.Saved, [@"C:\Clips\clip0003.mp3"]));

        Assert.False(Screen.EditorOpen);
        Assert.Equal(1, _panel.Closes);
        Assert.Empty(_panel.Discord);
        Assert.Equal(@"C:\Clips\clip0003.mp3", Screen.EditorChosenPath);
    }

    [Fact]
    public void Save_for_the_soundboard_writes_at_once_and_hands_the_copy_to_Discord_when_it_is_written()
    {
        Open(EditorMode.Soundboard);
        Click(SettingsScreen.EditorSaveId);
        Assert.Null(Screen.EditorDialog);
        var (request, save) = Assert.Single(_panel.Saves);
        Assert.Equal(EditorMode.Soundboard, request.Mode);
        Assert.Equal(new EditSave(EditSaveChoice.Copy, Name + Soundboard.CopySuffix), save);
        Assert.Empty(_panel.Discord);

        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.Saved, [@"C:\Clips\clip0002.mp3"]));
        Assert.Equal([@"C:\Clips\clip0002.mp3"], _panel.Discord);
        Assert.False(Screen.EditorOpen);
        Assert.Contains(Soundboard.UploadHint, Painted());
    }

    [Theory]
    [InlineData(EditSaveOutcome.CannotRecycle, SettingsScreen.EditorCantRecycle)]
    [InlineData(EditSaveOutcome.TooBig, SettingsScreen.EditorTooBig)]
    [InlineData(EditSaveOutcome.SourceGone, SettingsScreen.EditorSourceGone)]
    [InlineData(EditSaveOutcome.Failed, SettingsScreen.EditorFailed)]
    public void A_save_that_fails_says_why_and_the_editor_stays(EditSaveOutcome outcome, string text)
    {
        Open(EditorMode.Soundboard);
        Click(SettingsScreen.EditorSaveId);
        Screen.EditorSaved(new EditSaveResult(outcome, []));

        Assert.True(Screen.EditorOpen);
        Assert.Contains(text, Painted());
        Assert.DoesNotContain(SettingsScreen.EditorNothingChanges, Painted());
        Assert.Empty(_panel.Discord);
        Assert.True(((Button)Screen.WidgetById(SettingsScreen.EditorSaveId)!).Enabled);
    }

    [Fact]
    public void While_the_save_runs_Escape_Cancel_and_Save_do_nothing_and_the_foot_says_so()
    {
        Open(EditorMode.Soundboard);
        Click(SettingsScreen.EditorSaveId);
        Assert.Contains(SettingsScreen.EditorSaving, Painted());
        Assert.False(((Button)Screen.WidgetById(SettingsScreen.EditorCancelId)!).Enabled);
        Assert.False(((Button)Screen.WidgetById(SettingsScreen.EditorSaveId)!).Enabled);

        _rig.Press(Vk.Escape);
        Click(SettingsScreen.EditorCancelId);
        Assert.True(Screen.EditorOpen);
        Assert.Equal(0, _panel.Closes);
        Assert.Equal(SettingsScreen.EditorPlayId, Screen.FocusId);

        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.Saved, [@"C:\Clips\clip0002.mp3"]));
        Assert.Equal([@"C:\Clips\clip0002.mp3"], _panel.Discord);
        Assert.False(Screen.EditorOpen);
    }

    [Fact]
    public void A_soundboard_copy_written_after_the_editor_closed_still_goes_to_Discord()
    {
        Open(EditorMode.Soundboard);
        Click(SettingsScreen.EditorSaveId);
        Screen.SetEditor(_panel);
        Assert.False(Screen.EditorOpen);

        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.Saved, [@"C:\Clips\clip0002.mp3"]));
        Assert.Equal([@"C:\Clips\clip0002.mp3"], _panel.Discord);
        Assert.Contains(Soundboard.UploadHint, Painted());
    }

    [Theory]
    [InlineData(EditSaveOutcome.CannotRecycle, SettingsScreen.EditorCantRecycle)]
    [InlineData(EditSaveOutcome.Failed, SettingsScreen.EditorFailed)]
    public void A_save_that_fails_after_the_editor_closed_is_said_on_the_line_of_the_Library(EditSaveOutcome outcome, string text)
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        Click(EditorSaveDialogState.ReplaceId);
        Screen.SetEditor(_panel);

        Screen.EditorSaved(new EditSaveResult(outcome, []));
        Assert.False(Screen.EditorOpen);
        Assert.Contains(text, string.Join(" ", Painted()));
        Assert.Empty(_panel.Discord);
    }

    [Fact]
    public void A_replacement_kept_aside_closes_the_editor_chooses_the_edit_and_says_where_the_original_is()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        Click(EditorSaveDialogState.ReplaceId);
        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.SavedAside, [@"C:\Clips\clip0002.mp3"]));

        Assert.False(Screen.EditorOpen);
        Assert.Equal(1, _panel.Closes);
        Assert.Equal(@"C:\Clips\clip0002.mp3", Screen.EditorChosenPath);
        var drawn = string.Join(" ", Painted());
        Assert.Contains(SettingsScreen.EditorSavedAside, drawn);
        Assert.DoesNotContain(SettingsScreen.EditorFailed, drawn);
        Assert.Empty(_panel.Discord);
    }

    [Fact]
    public void A_replacement_whose_edit_could_not_be_kept_says_the_original_is_in_the_Recycle_Bin()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        Click(EditorSaveDialogState.ReplaceId);
        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.SavedAside, []));

        Assert.False(Screen.EditorOpen);
        Assert.Contains(SettingsScreen.EditorAsideLost, string.Join(" ", Painted()));
    }

    [Theory]
    [InlineData(EditorLoadOutcome.Gone)]
    [InlineData(EditorLoadOutcome.Unreadable)]
    public void A_clip_that_can_t_be_read_leaves_only_Cancel(EditorLoadOutcome outcome)
    {
        Screen.OpenEditor(OneFile, EditorMode.Normal);
        Screen.EditorLoaded(new EditorLoad(outcome, TimeSpan.Zero, [], ExportFormat.Mp3));

        Assert.Contains(SettingsScreen.EditorCantRead, Painted());
        var stops = Screen.StopIds().Where(id => id.StartsWith("edit.", StringComparison.Ordinal) || id.StartsWith("foot.", StringComparison.Ordinal));
        Assert.Equal([SettingsScreen.EditorCancelId], stops);
        Click(SettingsScreen.EditorSaveId);
        Assert.Empty(_panel.Saves);
    }

    [Fact]
    public void Leaving_the_Library_while_the_selection_plays_stops_it_once()
    {
        Open();
        Click(SettingsScreen.EditorPlayId);
        Screen.EditorPreviewChanged(PreviewState.Playing);
        Assert.True(Screen.WantsWave);

        Screen.ShowSection(Section.Hotkeys);
        Assert.True(Screen.WantsWave);
        Screen.WaveTick();
        Screen.WaveTick();
        Assert.Equal(1, _panel.Stops);

        Screen.EditorPreviewChanged(PreviewState.Stopped);
        Assert.False(Screen.WantsWave);
    }

    [Fact]
    public void The_play_head_follows_the_clock_from_the_start_of_the_part_kept_and_stops_at_its_end()
    {
        var now = TimeSpan.Zero;
        Screen.EditorClock = () => now;
        Open();
        _rig.FocusOn(SettingsScreen.EditorStartId);
        Screen.Key(Vk.Right, HotkeyGesture.ModShift);
        Click(SettingsScreen.EditorPlayId);
        Assert.Null(Screen.EditorPlayheadMs);

        Screen.EditorPreviewChanged(PreviewState.Playing);
        now = TimeSpan.FromSeconds(2.5);
        Screen.WaveTick();
        Assert.Equal(3_500, Screen.EditorPlayheadMs);
        now = TimeSpan.FromMinutes(5);
        Assert.Equal(30_000, Screen.EditorPlayheadMs);
    }

    [Fact]
    public void Kept_never_reads_as_the_limit_of_the_soundboard_beside_its_warning()
    {
        Open();
        Selection.Nudge(EditorEnd.End, 5_210 - Selection.EndMs);
        Assert.Equal(5_210, Selection.KeptMs);

        var drawn = Painted();
        Assert.Contains(Soundboard.TooLongText, drawn);
        Assert.Contains("5.21 s", drawn);
        Assert.DoesNotContain("5.2 s", drawn);
    }

    [Fact]
    public void A_replacement_kept_aside_with_fewer_files_than_the_clip_chooses_the_first_and_says_where_the_original_is()
    {
        Open(clip: TwoFiles, files: 2);
        Click(SettingsScreen.EditorSaveId);
        Click(EditorSaveDialogState.ReplaceId);
        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.SavedAside, [@"C:\Clips\clip0003.mp3"]));

        Assert.False(Screen.EditorOpen);
        Assert.Equal(@"C:\Clips\clip0003.mp3", Screen.EditorChosenPath);
        var drawn = string.Join(" ", Painted());
        Assert.Contains(SettingsScreen.EditorSavedAside, drawn);
        Assert.DoesNotContain(SettingsScreen.EditorAsideLost, drawn);
    }

    [Fact]
    public void While_a_save_from_the_dialog_runs_Escape_Cancel_and_Save_do_nothing_and_the_foot_says_so()
    {
        Open();
        Click(SettingsScreen.EditorSaveId);
        Click(EditorSaveDialogState.ReplaceId);
        Assert.Null(Screen.EditorDialog);
        Assert.Contains(SettingsScreen.EditorSaving, Painted());
        Assert.False(((Button)Screen.WidgetById(SettingsScreen.EditorCancelId)!).Enabled);
        Assert.False(((Button)Screen.WidgetById(SettingsScreen.EditorSaveId)!).Enabled);

        _rig.Press(Vk.Escape);
        Click(SettingsScreen.EditorCancelId);
        Click(SettingsScreen.EditorSaveId);
        Assert.True(Screen.EditorOpen);
        Assert.Null(Screen.EditorDialog);
        Assert.Equal(0, _panel.Closes);
        Assert.Single(_panel.Saves);
    }

    [Fact]
    public void The_second_press_of_a_double_click_on_Save_leaves_the_dialog_it_opened()
    {
        Open();
        var save = Screen.WidgetById(SettingsScreen.EditorSaveId)!.Bounds;
        Screen.MouseDown(save.MidX, save.MidY);
        Screen.MouseUp(save.MidX, save.MidY);
        Assert.NotNull(Screen.EditorDialog);
        Assert.Equal(EditorSaveDialogState.NameId, Screen.FocusId);

        Screen.MouseDoubleClick(save.MidX, save.MidY);
        Screen.MouseUp(save.MidX, save.MidY);

        Assert.NotNull(Screen.EditorDialog);
        Assert.Equal(EditorSaveDialogState.NameId, Screen.FocusId);
        Assert.Empty(_panel.Saves);
    }
}
