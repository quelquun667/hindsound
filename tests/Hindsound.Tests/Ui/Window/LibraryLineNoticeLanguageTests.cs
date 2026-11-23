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

    [Fact]
    public void The_answer_of_a_collection_on_the_line_follows_a_change_of_language()
    {
        using var scope = Tr.Use(UiLanguage.English);
        using var rig = new ScreenRig();
        var clip = LabeledLibrary.Folder + "Goal replay.mp3";
        var library = new LabeledLibrary
        {
            Lines = new ClipLines([new ClipGroup([new ClipFile(clip, 720_000, default)])], 0),
            Index = new LibraryData { Collections = [new() { Id = "p", Name = "pets" }] },
        };
        rig.Screen.UpdateStatus(ScreenRig.Capturing());
        rig.Screen.SetNow(new FakeNow());
        rig.Screen.SetLibrary(library);
        rig.Screen.ShowSection(Section.Library);
        rig.Screen.SelectClip(clip);
        rig.Screen.CollectionTickAnswered("p", LibraryOutcome.NotWritten);

        string Painted()
        {
            rig.Canvas.Drawn.Clear();
            rig.Screen.Paint(rig.Canvas);
            return string.Join(" | ", rig.Canvas.Drawn);
        }

        Assert.Contains("Couldn't save that. Nothing changed.", Painted());

        Tr.Set(UiLanguage.French);
        rig.Screen.LanguageChanged();
        var french = Painted();

        Assert.Contains("Impossible d'enregistrer. Rien n'a changé.", french);
        Assert.DoesNotContain("Couldn't save", french);
    }
}
