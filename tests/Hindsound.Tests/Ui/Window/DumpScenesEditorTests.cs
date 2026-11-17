using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The pictures of the editor set up the page they mean to show, over the Library, with nothing behind them.</summary>
public sealed class DumpScenesEditorTests
{
    public static TheoryData<string> Names => [.. DumpScenesEditor.Scenes.Select(scene => scene.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_scene_opens_the_editor_and_paints(string name)
    {
        using var rig = new ScreenRig();
        rig.Screen.UpdateStatus(ScreenRig.Capturing());
        rig.Screen.SetLibrary(new PlayingLibraryFake());
        rig.Screen.ShowSection(Section.Library);

        DumpScenesEditor.Scenes.Single(scene => scene.Name == name).Set(rig.Screen);
        rig.Screen.Paint(rig.Canvas);

        Assert.True(rig.Screen.EditorOpen);
        Assert.Contains(SettingsScreen.EditorTitle, rig.Canvas.Drawn);
        Assert.Equal(name == "edit-enregistrer", rig.Screen.EditorDialog is not null);
        Assert.Equal(name == "edit-illisible", rig.Screen.EditorSelection is null);
    }
}
