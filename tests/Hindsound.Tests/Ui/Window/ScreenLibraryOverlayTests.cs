using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>What a press does to the menu, the box and the sentence of the Library page that an earlier gesture left open.</summary>
public sealed class ScreenLibraryOverlayTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private readonly ScreenRig _rig = new();
    private readonly ActingLibrary _library = new(8);

    public ScreenLibraryOverlayTests()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(_library);
        Screen.ShowSection(Section.Library);
    }

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private void Click(string id)
    {
        var box = Screen.WidgetById(id)!.Bounds;
        Screen.MouseDown(box.MidX, box.MidY);
        Screen.MouseUp(box.MidX, box.MidY);
    }

    [Fact]
    public void The_second_press_of_a_double_click_on_Delete_does_not_answer_the_question_it_just_opened()
    {
        Screen.SetEditor(new LoadingEditor());

        // Where the row Delete of the menu lies over the button that confirms the question of the box it opens.
        Screen.RightClick(280, 138);
        var menu = Screen.OpenClipMenu!;
        Assert.Equal(ClipMenuItem.Delete, menu.Items[menu.ItemAt(508, 293)]);
        var confirm = NameBox.Frame(NameBoxState.ForClipDelete(menu.Clip, SettingsScreen.ClipListId));
        Assert.True(confirm.Contains(508, 293));

        Screen.MouseDown(508, 293);
        Screen.MouseUp(508, 293);
        Assert.True(Screen.BoxOpen);
        Assert.True(Screen.WidgetById(NameBox.ConfirmId)!.Bounds.Contains(508, 293));

        Screen.MouseDoubleClick(508, 293);
        Screen.MouseUp(508, 293);

        Assert.Empty(_library.Deleted);
        Assert.True(Screen.BoxOpen);

        // A new press asks the question again, and answers it.
        Screen.MouseDown(508, 293);
        Screen.MouseUp(508, 293);
        Assert.Single(_library.Deleted);
    }

    [Fact]
    public void The_second_press_of_a_double_click_on_the_foot_button_leaves_the_menu_it_opened()
    {
        Screen.SelectClip(FakeLibrary.PathOf(1));
        var more = Screen.WidgetById(SettingsScreen.ClipMoreId)!.Bounds;
        Screen.MouseDown(more.MidX, more.MidY);
        Screen.MouseUp(more.MidX, more.MidY);
        Assert.NotNull(Screen.OpenClipMenu);

        Screen.MouseDoubleClick(more.MidX, more.MidY);
        Screen.MouseUp(more.MidX, more.MidY);

        Assert.NotNull(Screen.OpenClipMenu);
    }

    [Fact]
    public void The_hint_for_Discord_stays_for_the_copy_a_reading_is_still_to_bring_through_a_refresh_and_a_status()
    {
        var panel = new FakeEditorPanel();
        Screen.SetEditor(panel);
        Screen.OpenEditor(_library.Rows[0], EditorMode.Soundboard);
        Screen.EditorLoaded(new EditorLoad(EditorLoadOutcome.Loaded, TimeSpan.FromSeconds(30), [new float[600]], ExportFormat.Mp3));
        Click(SettingsScreen.EditorSaveId);
        var copy = ActingLibrary.Folder + "clip0000 (soundboard).mp3";
        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.Saved, [copy]));

        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.LibraryLengthsKnown();
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        Assert.Contains(Soundboard.UploadHint, _rig.Canvas.Drawn);

        // The reading that brings the copy chooses it, and the sentence is still there for it.
        _library.Read([.. _library.Rows, FakeLibrary.Clip("clip0000 (soundboard).mp3")]);
        Screen.LibraryReadingArrived();
        Assert.Equal(copy, Screen.EditorChosenPath);
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        Assert.Contains(Soundboard.UploadHint, _rig.Canvas.Drawn);
    }
}
