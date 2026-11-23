using System.IO;
using Hindsound.App;
using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The hotkey "Save and edit" on the Hotkeys page, in the settings file, in the manager, and where the window goes to open it.</summary>
public sealed class SaveAndEditPageTests : IDisposable
{
    private const uint CtrlAlt = HotkeyGesture.ModControl | HotkeyGesture.ModAlt;

    private readonly ScreenRig _rig = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-saveedit-").FullName;

    public void Dispose()
    {
        _rig.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private SettingsScreen Screen => _rig.Screen;

    private static ClipGroup AClip => new([new ClipFile(@"C:\Clips\a.mp3", 1000, default)]);

    // ---------------------------------------------------------------- the page

    [Fact]
    public void The_row_is_the_second_one_right_under_Save_clip()
    {
        Assert.Equal(
            ["Save clip", "Save and edit", "Save up to 15s", "Save up to 1 min", "Save up to 5 min", "Pause or resume", "Copy last clip"],
            SettingsEditor.HotkeyRows.Select(row => row.Label));
    }

    [Fact]
    public void With_an_editor_the_page_shows_seven_fields_and_Reset_to_defaults_stays_there()
    {
        Screen.SetEditor(new FakeEditorPanel());
        Screen.ShowSection(Section.Hotkeys);

        foreach (var (action, _) in SettingsEditor.HotkeyRows)
        {
            Assert.True(Screen.WidgetById("hotkey." + action) is not null, action.ToString());
        }

        var save = Screen.WidgetById("hotkey." + HotkeyAction.SaveDefault)!.Bounds;
        var edit = Screen.WidgetById("hotkey." + HotkeyAction.SaveAndEdit)!.Bounds;
        var next = Screen.WidgetById("hotkey." + HotkeyAction.Save15s)!.Bounds;
        Assert.True(save.Y < edit.Y && edit.Y < next.Y);
        Assert.NotNull(Screen.WidgetById("hotkey.reset"));
    }

    [Fact]
    public void Without_an_editor_the_row_is_not_shown_and_the_six_others_are()
    {
        Screen.ShowSection(Section.Hotkeys);

        Assert.Null(Screen.WidgetById("hotkey." + HotkeyAction.SaveAndEdit));
        foreach (var (action, _) in SettingsEditor.HotkeyRows.Where(row => row.Action != HotkeyAction.SaveAndEdit))
        {
            Assert.NotNull(Screen.WidgetById("hotkey." + action));
        }
    }

    [Fact]
    public void A_combination_typed_for_it_is_kept_registered_and_written()
    {
        Screen.SetEditor(new FakeEditorPanel());
        Screen.ShowSection(Section.Hotkeys);
        _rig.FocusOn("hotkey." + HotkeyAction.SaveAndEdit);
        _rig.Press(Vk.Enter);

        Screen.Key('E', CtrlAlt);

        Assert.Equal("Ctrl+Alt+E", _rig.Settings.Hotkeys.SaveAndEdit);
        Assert.True(_rig.Native.Holds(CtrlAlt, 'E'));
    }

    [Fact]
    public void The_combination_of_another_action_is_refused_as_a_duplicate_and_kept_by_the_other()
    {
        _rig.Settings.Hotkeys.Save15s = "Ctrl+Alt+E";
        _rig.Manager.Apply(_rig.Settings.Hotkeys);
        Screen.SetEditor(new FakeEditorPanel());
        Screen.ShowSection(Section.Hotkeys);
        _rig.FocusOn("hotkey." + HotkeyAction.SaveAndEdit);
        _rig.Press(Vk.Enter);

        Screen.Key('E', CtrlAlt);

        Assert.Null(_rig.Settings.Hotkeys.SaveAndEdit);
        Assert.Equal("Ctrl+Alt+E", _rig.Settings.Hotkeys.Save15s);
        Assert.Equal(HotkeyAction.Save15s, Screen.Editor.DuplicateOf(HotkeyAction.SaveAndEdit, "Ctrl+Alt+E"));
    }

    [Fact]
    public void Reset_to_defaults_leaves_it_not_set()
    {
        _rig.Settings.Hotkeys.SaveAndEdit = "Ctrl+Alt+E";
        _rig.Manager.Apply(_rig.Settings.Hotkeys);
        Screen.SetEditor(new FakeEditorPanel());
        Screen.ShowSection(Section.Hotkeys);

        var reset = (Button)Screen.WidgetById("hotkey.reset")!;
        var box = reset.Bounds;
        Screen.MouseDown(box.MidX, box.MidY);
        Screen.MouseUp(box.MidX, box.MidY);

        Assert.Null(_rig.Settings.Hotkeys.SaveAndEdit);
        Assert.False(_rig.Native.Holds(CtrlAlt, 'E'));
        Assert.Equal(HotkeySettings.DefaultSaveShortcut, _rig.Settings.Hotkeys.SaveDefault);
    }

    // ---------------------------------------------------------------- the editor of the screen

    [Fact]
    public void An_editor_just_opened_holds_nothing_and_a_moved_part_or_a_volume_holds_something()
    {
        Screen.SetEditor(new FakeEditorPanel());
        Screen.ShowSection(Section.Library);
        Screen.OpenEditor(AClip, EditorMode.Normal);
        Assert.False(Screen.EditorHoldsWork);

        Screen.EditorLoaded(new EditorLoad(EditorLoadOutcome.Loaded, TimeSpan.FromSeconds(30), [new float[600]], Hindsound.Export.ExportFormat.Mp3));
        Assert.False(Screen.EditorHoldsWork);

        _rig.FocusOn(SettingsScreen.EditorEndId);
        Screen.Key(Vk.Left, 0);
        Assert.True(Screen.EditorHoldsWork);

        Screen.CloseEditor();
        Assert.False(Screen.EditorHoldsWork);
    }

    // ---------------------------------------------------------------- the settings file

    private string File1 => Path.Combine(_folder, "settings.json");

    [Fact]
    public void A_file_without_the_key_reads_as_not_set_and_the_default_is_not_set()
    {
        File.WriteAllText(File1, """{ "hotkeys": { "saveDefault": "Ctrl+Shift+F10", "copyLast": "Ctrl+Alt+C" } }""");

        var read = new SettingsStore(File1).Read();

        Assert.Equal(SettingsLoadStatus.Loaded, read.Status);
        Assert.Null(read.Settings!.Hotkeys.SaveAndEdit);
        Assert.Null(new AppSettings().Hotkeys.SaveAndEdit);
    }

    [Fact]
    public void The_key_is_written_next_to_the_others_and_read_back_in_its_canonical_form()
    {
        var store = new SettingsStore(File1);
        var settings = new AppSettings { Hotkeys = { SaveAndEdit = "alt+ctrl+e", CopyLast = "Ctrl+Alt+C" } }.Normalize();

        store.Save(settings);

        var text = File.ReadAllText(File1);
        Assert.Contains("\"copyLast\": \"Ctrl+Alt+C\"", text);
        Assert.Contains("\"saveAndEdit\": \"Ctrl+Alt+E\"", text);
        Assert.True(text.IndexOf("\"copyLast\"", StringComparison.Ordinal) < text.IndexOf("\"saveAndEdit\"", StringComparison.Ordinal));
        Assert.Equal("Ctrl+Alt+E", store.Load().Hotkeys.SaveAndEdit);
    }

    [Fact]
    public void A_key_the_reader_does_not_know_is_ignored_so_a_file_of_this_version_still_loads_in_0_12_0()
    {
        // 0.12.0 reads with the same store and the same options: a key that is not one of its properties is skipped, never an error.
        File.WriteAllText(File1, """{ "hotkeys": { "saveDefault": "F8", "saveAndEdit": "Ctrl+Alt+E", "aKeyOfALaterVersion": "Ctrl+Q" }, "clipSeconds": 45 }""");

        var read = new SettingsStore(File1).Read();

        Assert.Equal(SettingsLoadStatus.Loaded, read.Status);
        Assert.Equal("F8", read.Settings!.Hotkeys.SaveDefault);
        Assert.Equal(45, read.Settings.ClipSeconds);
    }

    // ---------------------------------------------------------------- the manager

    [Fact]
    public void The_manager_registers_it_as_a_seventh_action_after_the_six_others_in_their_places()
    {
        Assert.Equal(6, (int)HotkeyAction.SaveAndEdit);
        var native = new FakeHotkeys();
        using var manager = new HotkeyManager(native);

        var results = manager.Apply(new HotkeySettings { SaveAndEdit = "Ctrl+Alt+E" });

        Assert.Equal(7, results.Count);
        Assert.Equal(HotkeyStatus.Registered, results[(int)HotkeyAction.SaveAndEdit].Status);
        Assert.Equal(HotkeyStatus.NotSet, results[(int)HotkeyAction.CopyLast].Status);
        Assert.True(native.Holds(CtrlAlt, 'E'));
    }

    [Fact]
    public void The_same_combination_as_another_action_gets_the_Duplicate_status()
    {
        using var manager = new HotkeyManager(new FakeHotkeys());

        var results = manager.Apply(new HotkeySettings { SaveDefault = "Ctrl+Alt+E", SaveAndEdit = "Ctrl+Alt+E" });

        Assert.Equal(HotkeyStatus.Registered, results[(int)HotkeyAction.SaveDefault].Status);
        Assert.Equal(HotkeyStatus.Duplicate, results[(int)HotkeyAction.SaveAndEdit].Status);
    }

    [Fact]
    public void A_press_of_it_reaches_the_handler_as_that_action()
    {
        var native = new FakeHotkeys();
        using var manager = new HotkeyManager(native);
        var seen = new List<HotkeyAction>();
        using var got = new ManualResetEventSlim();
        manager.Pressed += action =>
        {
            seen.Add(action);
            got.Set();
        };
        manager.Apply(new HotkeySettings { SaveAndEdit = "Ctrl+Alt+E" });

        var id = native.Registered.First(entry => entry.Value == (CtrlAlt, (uint)'E')).Key;
        manager.Dispatch(id);

        Assert.True(got.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal([HotkeyAction.SaveAndEdit], seen);
    }

    // ---------------------------------------------------------------- where the window goes

    private sealed class FakeEditorScreen(Section on, List<string> calls, bool holds = false, bool hasEditor = true) : IEditorWindowScreen
    {
        public Section Section { get; private set; } = on;

        public bool EditorHoldsWork => holds;

        public bool HasEditor => hasEditor;

        public void ShowSection(Section section) => Section = section;

        public void ShowUpdates()
        {
        }

        public void ShowUpdateCard()
        {
        }

        public void SelectClip(string path)
        {
        }

        public void OpenEditorOn(ClipGroup clip)
        {
            calls.Add("editor " + clip.Main.Path);
            Section = Section.Library;
        }
    }

    private sealed record Run(WindowGoing Going, List<string> Calls, List<string> Journal, Func<FakeEditorScreen?> Screen);

    private static Run Going(Section? on, bool holds = false, bool mayOpen = true, bool madeHasEditor = true)
    {
        var calls = new List<string>();
        var journal = new List<string>();
        var open = on is { } section ? new FakeEditorScreen(section, calls, holds) : null;
        var going = new WindowGoing(
            () => mayOpen,
            () => open,
            () => calls.Add("bring back"),
            target =>
            {
                calls.Add("create " + target);
                open = new FakeEditorScreen(target, calls, hasEditor: madeHasEditor);
            },
            journal.Add);
        return new Run(going, calls, journal, () => open);
    }

    [Fact]
    public void A_closed_window_is_made_on_the_Library_then_opens_the_editor()
    {
        var run = Going(null);

        Assert.Equal(EditorOpening.Opened, run.Going.OpenEditor(AClip));

        Assert.Equal(["create Library", @"editor C:\Clips\a.mp3"], run.Calls);
    }

    [Theory]
    [InlineData((int)Section.Now)]
    [InlineData((int)Section.Library)]
    [InlineData((int)Section.General)]
    public void An_open_window_with_nothing_to_keep_comes_to_the_front_and_opens_the_editor_wherever_it_was(int onNumber)
    {
        var on = (Section)onNumber;
        var run = Going(on);

        Assert.Equal(EditorOpening.Opened, run.Going.OpenEditor(AClip));

        Assert.Equal(["bring back", @"editor C:\Clips\a.mp3"], run.Calls);
    }

    [Theory]
    [InlineData((int)Section.Library)]
    [InlineData((int)Section.General)]
    public void An_editor_that_holds_work_is_not_replaced_the_window_comes_to_the_front_and_the_journal_says_why(int onNumber)
    {
        var on = (Section)onNumber;
        var run = Going(on, holds: true);

        Assert.Equal(EditorOpening.KeptWhatItShows, run.Going.OpenEditor(AClip));

        Assert.Equal(["bring back"], run.Calls);
        Assert.Equal(on, run.Screen()!.Section);
        Assert.Contains(run.Journal, line => line.StartsWith("editor not opened: the editor holds changes or a save in progress", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData((int)Section.FirstRun)]
    [InlineData((int)Section.UpdateCard)]
    public void A_card_is_never_left_for_the_editor(int cardNumber)
    {
        var card = (Section)cardNumber;
        var run = Going(card);

        Assert.Equal(EditorOpening.KeptWhatItShows, run.Going.OpenEditor(AClip));

        Assert.Equal(card, run.Screen()!.Section);
        Assert.DoesNotContain(run.Calls, call => call.StartsWith("editor", StringComparison.Ordinal));
        Assert.Contains(run.Journal, line => line.Contains("the window shows a card", StringComparison.Ordinal));
    }

    [Fact]
    public void A_leaving_app_and_a_window_with_no_editor_open_nothing_and_say_so()
    {
        var leaving = Going(null, mayOpen: false);
        Assert.Equal(EditorOpening.NotOpened, leaving.Going.OpenEditor(AClip));
        Assert.Empty(leaving.Calls);
        Assert.Contains(leaving.Journal, line => line.Contains("the app is leaving", StringComparison.Ordinal));

        var bare = Going(null, madeHasEditor: false);
        Assert.Equal(EditorOpening.NotOpened, bare.Going.OpenEditor(AClip));
        Assert.Equal(["create Library"], bare.Calls);
        Assert.Contains(bare.Journal, line => line.Contains("no window with an editor", StringComparison.Ordinal));
    }
}
