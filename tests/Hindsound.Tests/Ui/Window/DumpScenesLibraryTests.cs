using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The pictures of the clip tools reach what they are named after, from the page the dump leaves them.</summary>
public sealed class DumpScenesLibraryTests
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    public static TheoryData<string> Names() => [.. DumpScenesLibrary.Scenes.Select(scene => scene.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_scene_opens_what_its_picture_shows(string name)
    {
        using var rig = new ScreenRig();
        var library = new ActingLibrary(4);
        var twoFiles = new ClipGroup([new ClipFile(DumpScenesLibrary.TwoFilesClip, 1000, default), new ClipFile(DumpScenesLibrary.TwoFilesClip.Replace("_system", "_mic", StringComparison.Ordinal), 1000, default)]);
        library.Read([.. library.Rows, twoFiles]);
        rig.Screen.SetNow(new FakeNow());
        rig.Screen.SetLibrary(library);
        rig.Screen.ShowSection(Section.Library);
        rig.Screen.SelectClip(FakeLibrary.PathOf(1));

        DumpScenesLibrary.Scenes.Single(scene => scene.Name == name).Act(rig.Screen);

        if (name.StartsWith("library-menu", StringComparison.Ordinal))
        {
            Assert.Equal(FakeLibrary.PathOf(1), rig.Screen.OpenClipMenu!.Clip.Main.Path);
        }
        else
        {
            var box = rig.Screen.OpenBoxState!;
            Assert.Equal(name.Contains("renommer", StringComparison.Ordinal) ? NameBoxKind.RenameClip : NameBoxKind.DeleteClip, box.Kind);
            Assert.Equal(name.Contains("2-fichiers", StringComparison.Ordinal) ? DumpScenesLibrary.TwoFilesClip : FakeLibrary.PathOf(1), box.Clip!.Main.Path);
        }
    }
}
