using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The sentence on the amber line of the Library is kept as what happened, and said in the language of the moment.</summary>
public sealed class LibraryLineNoticeLanguageTests
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    [Fact]
    public void A_sentence_on_the_line_of_the_Library_follows_a_change_of_language()
    {
        using var scope = Tr.Use(UiLanguage.English);
        using var rig = new ScreenRig();
        var library = new ActingLibrary(3);
        rig.Screen.UpdateStatus(ScreenRig.Capturing());
        rig.Screen.SetNow(new FakeNow());
        rig.Screen.SetLibrary(library);
        rig.Screen.ShowSection(Section.Library);
        rig.Screen.SelectClip(FakeLibrary.PathOf(1));
        rig.Screen.ClipDeleteAnswered(library.Rows[1], ClipDeleteOutcome.CannotRecycle);
        rig.Screen.LibraryReadingArrived();

        string Painted()
        {
            rig.Canvas.Drawn.Clear();
            rig.Screen.Paint(rig.Canvas);
            return string.Join(" | ", rig.Canvas.Drawn);
        }

        Assert.Contains("The Recycle Bin can't take this clip.", Painted());

        Tr.Set(UiLanguage.French);
        rig.Screen.LanguageChanged();
        var french = Painted();

        Assert.Contains("La Corbeille ne peut pas prendre ce clip.", french);
        Assert.DoesNotContain("Recycle Bin", french);
    }
}
