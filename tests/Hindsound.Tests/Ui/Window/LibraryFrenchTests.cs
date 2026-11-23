using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The Library in French: the page, the menus, the boxes of a name, the messages of the collection line and the reserved names.</summary>
public sealed class LibraryFrenchTests
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private static List<string> PaintedLibrary(ScreenRig rig, ILibraryPanel library)
    {
        rig.Screen.UpdateStatus(ScreenRig.Capturing());
        rig.Screen.SetNow(new FakeNow());
        rig.Screen.SetLibrary(library);
        rig.Screen.ShowSection(Section.Library);
        rig.Canvas.Drawn.Clear();
        rig.Screen.Paint(rig.Canvas);
        return rig.Canvas.Drawn;
    }

    [Fact]
    public void The_page_foot_and_notes_of_the_list_are_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        using var rig = new ScreenRig();
        var library = new FakeLibrary
        {
            Lines = new ClipLines(
            [
                new ClipGroup([new ClipFile(FakeLibrary.Folder + "a_system.mp3", 1000, default), new ClipFile(FakeLibrary.Folder + "a_mic.mp3", 1000, default)]),
                FakeLibrary.Clip("b.mp3"),
            ], 0),
        };
        library.Rescued.Add(FakeLibrary.Folder + "b.mp3");

        var drawn = PaintedLibrary(rig, library);

        foreach (var word in new[] { "Clip choisi", "Aucun clip choisi", "Copier", "Afficher dans le dossier", "Lire", "Rechercher", "2 fichiers", "Récupéré" })
        {
            Assert.Contains(word, drawn);
        }

        foreach (var english in new[] { "Selected clip", "Nothing selected", "Copy", "Show in folder", "Play", "Search", "2 files", "Rescued" })
        {
            Assert.DoesNotContain(english, drawn);
        }
    }

    [Fact]
    public void The_empty_view_the_end_note_and_the_collection_line_are_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        using var rig = new ScreenRig();

        var empty = PaintedLibrary(rig, new FakeLibrary { Lines = new ClipLines([], 0) });
        Assert.Contains("Aucun clip pour l'instant", empty);
        Assert.Contains("Enregistre un clip avec ton raccourci\u00A0: il apparaîtra ici.", empty);
        Assert.DoesNotContain("No clips yet", empty);

        var capped = PaintedLibrary(rig, new FakeLibrary().With(3, leftOut: 5));
        Assert.Contains("Affichage des 2\u00A0000 clips les plus récents.", capped);

        // The line of collections stands under the list once a clip is chosen.
        PaintedLibrary(rig, CollectionLibrary.Sample());
        rig.Screen.SelectClip(CollectionLibrary.PathOf(1));
        rig.Canvas.Drawn.Clear();
        rig.Screen.Paint(rig.Canvas);
        var withCollections = rig.Canvas.Drawn;
        Assert.Contains("Clip choisi dans\u00A0:", withCollections);
        Assert.Contains("Nouvelle collection...", withCollections);
        Assert.DoesNotContain("Selected clip in:", withCollections);
    }

    [Fact]
    public void The_menus_of_a_clip_and_of_a_collection_are_French()
    {
        using var scope = Tr.Use(UiLanguage.French);

        Assert.Equal(
            ["Renommer", "Modifier...", "Envoyer vers la soundboard...", "Copier", "Afficher dans le dossier", "Supprimer"],
            ClipMenu.ItemsFor(editor: true).Select(ClipMenu.LabelOf));
        Assert.Equal(["Renommer...", "Supprimer..."], new[] { CollectionMenuItem.Rename, CollectionMenuItem.Delete }.Select(CollectionMenu.LabelOf));
        Assert.Equal("F2", ClipMenu.KeyOf(ClipMenuItem.Rename));
    }

    [Fact]
    public void The_filter_menu_and_the_empty_views_of_a_choice_are_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var data = new LibraryData { Collections = [new LibraryCollection { Id = "c0", Name = "pets" }] };

        Assert.Equal(["Tous les clips", "Favoris", "pets"], LibraryFilter.Menu(data).Select(item => item.Label));
        Assert.Equal("Aucun clip favori", LibraryFilter.EmptyText(LibraryChoice.Favorites).Title);
        Assert.Equal("Aucun clip dans cette collection", LibraryFilter.EmptyText(LibraryChoice.Of("c0")).Title);

        var none = LibraryFilter.NoMatch("zebra");
        Assert.Equal("Aucun clip ne correspond à «\u00A0zebra\u00A0»", none.Title);
        Assert.Equal("Essaie d'autres mots, ou efface la recherche.", none.Body);
    }

    [Theory]
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.French)]
    public void A_collection_cannot_take_a_reserved_name_of_either_language(UiLanguage language)
    {
        using var scope = Tr.Use(language);

        foreach (var name in new[] { "All clips", "Favorites", "Tous les clips", "FAVORIS", " favoris " })
        {
            Assert.Equal(LibraryOutcome.NameTaken, LibraryStore.NameRefusal([], name));
        }

        Assert.Null(LibraryStore.NameRefusal([], "Favoris de Sam"));
    }

    [Fact]
    public void The_boxes_of_a_name_say_their_words_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var canvas = new FakeCanvas();
        List<LibraryCollection> taken = [new() { Id = "c1", Name = "pets" }];

        var create = NameBoxState.ForCreate(ChipLine.NewId, () => taken);
        Assert.Equal("Nouvelle collection", create.Title);
        Assert.Equal("Nom de la collection", create.FieldLabel);
        Assert.Equal("Créer", create.ConfirmLabel);
        Assert.Equal("Ce nom est déjà utilisé", create.Check("pets").Message);
        Assert.Equal("40 caractères au maximum", create.Check(new string('a', 41)).Message);

        var rename = NameBoxState.ForRename("c1", "pets", CollectionActions.MoreId, () => taken);
        Assert.Equal("Renommer la collection", rename.Title);
        Assert.Equal("Renommer", rename.ConfirmLabel);
        Assert.Equal("Supprimer", NameBoxState.ForDelete("c1", "pets", CollectionActions.MoreId).ConfirmLabel);
        Assert.Equal("Recommencer", NameBoxState.ForStartOver(SettingsScreen.StartOverId).ConfirmLabel);
        Assert.Equal("Recommencer sans collection\u00A0? Tes clips restent.", NameBoxState.StartOverQuestion);

        Assert.Equal("Supprimer la collection «\u00A0pets\u00A0»\u00A0? Tes clips restent.", NameBox.DeleteQuestion(canvas, "pets"));
        Assert.Equal("Déplacer «\u00A0a.mp3\u00A0» vers la Corbeille\u00A0?", NameBox.ClipDeleteQuestion(canvas, "a.mp3"));

        var clip = NameBoxState.ForClipRename(FakeLibrary.Clip("a.mp3"), "x", _ => ClipRenameOutcome.Taken);
        Assert.Equal("Renommer le clip", clip.Title);
        Assert.Equal("Nom du clip", clip.FieldLabel);
        Assert.Equal("Un autre clip porte ce nom", clip.Check("b").Message);
        Assert.Equal("Le nom ne peut pas être vide", NameBoxState.ClipRefusalText(ClipRenameOutcome.Empty));
        Assert.Equal("Ce nom est trop long", NameBoxState.ClipRefusalText(ClipRenameOutcome.TooLong));
        Assert.Equal("Un nom ne peut pas finir par un point ou un espace", NameBoxState.ClipRefusalText(ClipRenameOutcome.TrailingDotOrSpace));
        Assert.Equal("Un nom ne peut pas finir par _system ou _mic", NameBoxState.ClipRefusalText(ClipRenameOutcome.TrackSuffix));
        Assert.StartsWith("La Corbeille", NameBoxState.CannotRecycleShown);
        Assert.Contains("la Corbeille", NameBoxState.TwoFilesText);
    }

    [Fact]
    public void The_two_files_sentence_names_the_computer_of_the_system_in_French_only()
    {
        using (Tr.Use(UiLanguage.French))
        {
            Assert.Contains("le son du PC et le micro", NameBoxState.TwoFilesText);
            Assert.Contains("le son du Mac et le micro", NameBoxState.TwoFilesTextOf("Mac"));
            Assert.DoesNotContain("PC", NameBoxState.TwoFilesTextOf("Mac"));
        }

        Assert.Equal(NameBoxState.TwoFilesTextOf("PC"), NameBoxState.TwoFilesTextOf("Mac"));
        Assert.Contains("the system sound and the microphone", NameBoxState.TwoFilesTextOf("Mac"));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void The_French_messages_of_the_field_fit_its_width(double scale)
    {
        using var scope = Tr.Use(UiLanguage.French);
        using var canvas = new GdiCanvas((int)(760 * scale), (int)(488 * scale), scale);

        foreach (var text in new[] { NameBoxState.TakenShown, NameBoxState.TooLongShown })
        {
            Assert.True(canvas.Measure(text, Face.Body, 13) + 18 <= NameBox.Width - 48);
        }
    }

    [Fact]
    public void The_messages_of_the_collection_line_and_of_the_preview_are_French()
    {
        using var scope = Tr.Use(UiLanguage.French);

        Assert.StartsWith("Une version plus récente", ChipLine.ReadOnlyMessage(LibraryStatus.TooNew));
        Assert.StartsWith("Ton fichier de collections", ChipLine.ReadOnlyMessage(LibraryStatus.Corrupt));
        Assert.StartsWith("Impossible de lire les collections", ChipLine.ReadOnlyMessage(LibraryStatus.Unreadable));
        Assert.Equal("Impossible d'enregistrer. Rien n'a changé.", ChipLine.OutcomeMessage(LibraryOutcome.NotWritten));
        Assert.StartsWith("Impossible de modifier les collections", ChipLine.OutcomeMessage(LibraryOutcome.ReadOnly));
        Assert.Equal("Cette collection n'existe plus.", ChipLine.OutcomeMessage(LibraryOutcome.UnknownCollection));
        Assert.StartsWith("Impossible de vérifier le dossier", ChipLine.OutcomeMessage(LibraryOutcome.FolderNotRead));
        Assert.Equal("Ce clip a été déplacé ou supprimé", ChipLine.OutcomeMessage(LibraryOutcome.ClipGone));
        Assert.Equal("Ce nom est déjà utilisé", ChipLine.OutcomeMessage(LibraryOutcome.NameTaken));
        Assert.Equal("Impossible de lire ce fichier", ClipPreview.CantPlayText);
        Assert.Equal("Ici, la lecture peut finir dans ton prochain clip.", ClipPreview.CanEndUpInClipShown);
        Assert.Equal("Ça n'a pas marché. Ton clip n'a pas été touché.", SettingsScreen.ClipFailedShown);
    }
}
