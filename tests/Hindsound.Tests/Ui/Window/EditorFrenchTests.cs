using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The editor of a clip in French: the page, the soundboard variant, the dialog of Save, the sentences after a save and the
/// numbers it writes. Each test opens its own French scope, so that the tests beside it keep reading English.
/// </summary>
public sealed class EditorFrenchTests : IDisposable
{
    private const string Name = "Hindsound_Discord_2026-11-08_20-47-51";
    private const string Nb = "\u00A0";

    private readonly ScreenRig _rig = new();
    private readonly PlayingLibraryFake _library = new();
    private readonly FakeEditorPanel _panel = new();

    public EditorFrenchTests()
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

    private void Open(EditorMode mode = EditorMode.Normal, ClipGroup? clip = null, int files = 1)
    {
        Screen.OpenEditor(clip ?? OneFile, mode);
        Screen.EditorLoaded(new EditorLoad(EditorLoadOutcome.Loaded, TimeSpan.FromSeconds(30), [.. Enumerable.Range(0, files).Select(_ => (IReadOnlyList<float>)Peaks)], ExportFormat.Mp3));
    }

    private void Click(string id)
    {
        var box = Screen.WidgetById(id)!.Bounds;
        Screen.MouseDown(box.MidX, box.MidY);
        Screen.MouseUp(box.MidX, box.MidY);
    }

    private string Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return string.Join("\n", _rig.Canvas.Drawn);
    }

    /// <summary>The page as one text: the amber line of the Library wraps a long sentence in two, at a space.</summary>
    private string PaintedFlat() => Painted().Replace("\n", " ", StringComparison.Ordinal);

    [Fact]
    public void The_page_speaks_French_and_no_English_is_left_on_it()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Open();
        var drawn = Painted();

        foreach (var word in new[] { "Modifier le clip", "Lire", "Début", "Fin", "Gardé", "Taille", "environ 115" + Nb + "Ko", "Volume", "Rien ne change tant que tu n'as pas enregistré", "Enregistrer", "Annuler", "Trop long pour la soundboard" + Nb + ": 5,2" + Nb + "s max" })
        {
            Assert.Contains(word, drawn);
        }

        foreach (var word in new[] { "Edit clip", "Play", "Start", "End", "Kept", "Size", "about", "Nothing changes", "Save", "Cancel", "Too long" })
        {
            Assert.DoesNotContain(word, drawn);
        }

        Assert.Contains("30,0" + Nb + "s", drawn);
    }

    [Fact]
    public void The_soundboard_page_has_its_own_title_and_says_when_the_part_is_too_long()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Open(EditorMode.Soundboard);
        var drawn = Painted();
        Assert.Contains("Vers la soundboard", drawn);
        Assert.DoesNotContain("Send to soundboard", drawn);
        Assert.Contains("Compatible avec la soundboard de Discord (5,2" + Nb + "s max)", drawn);
        Assert.DoesNotContain("Fits the Discord", drawn);

        Screen.CloseEditor();
        Open();
        Assert.Contains("Trop long pour la soundboard" + Nb + ": 5,2" + Nb + "s max", Painted());
        Assert.DoesNotContain("Too long", Painted());
    }

    [Fact]
    public void A_clip_in_two_files_names_its_lanes_and_its_volumes_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Open(clip: TwoFiles, files: 2);
        _rig.FocusOn(SettingsScreen.EditorVolumePrefix + 0);
        _rig.Press(Vk.Right);
        _rig.Press(Vk.Right);
        var drawn = Painted();

        foreach (var word in new[] { "Son du PC", "Micro", "Volume du PC", "Volume du micro", "Pics sous -1" + Nb + "dB" })
        {
            Assert.Contains(word, drawn);
        }

        foreach (var word in new[] { "Whole PC", "Mic volume", "PC volume", "Peaks held" })
        {
            Assert.DoesNotContain(word, drawn);
        }
    }

    [Fact]
    public void The_numbers_the_page_writes_take_the_comma_and_the_no_break_space_of_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("environ 115" + Nb + "Ko", SettingsScreen.SizeText(115_000));
        Assert.Equal("environ 1" + Nb + "Ko", SettingsScreen.SizeText(40));
        Assert.Equal("environ 2,5" + Nb + "Mo", SettingsScreen.SizeText(2_500_000));
        Assert.Equal("3,2" + Nb + "s", SettingsScreen.KeptText(3_200));
        Assert.Equal("5,23" + Nb + "s", SettingsScreen.KeptText(EditorSelection.SoundboardMs + 30));
        Assert.Equal("Pics sous -1" + Nb + "dB", SettingsScreen.HeldText(-1));
        Assert.Equal("Pics sous -0,5" + Nb + "dB", SettingsScreen.HeldText(-0.5));
        Assert.Equal("0:03,2", SettingsScreen.EditClock(3_200));
        Assert.Equal("1:00,0", SettingsScreen.EditClock(59_960));
        using (Tr.Use(UiLanguage.English))
        {
            Assert.Equal("0:03.2", SettingsScreen.EditClock(3_200));
        }
    }

    [Fact]
    public void The_default_name_of_an_edited_copy_ends_in_its_own_word_of_each_language()
    {
        Assert.Equal(" (cut)", EditLimits.CutSuffix);
        using (Tr.Use(UiLanguage.French))
        {
            Assert.Equal(" (modifié)", EditLimits.CutSuffix);
        }

        // The soundboard copy keeps its English word in both languages.
        Assert.Equal(" (soundboard)", Soundboard.CopySuffix);
    }

    [Fact]
    public void The_names_of_the_volumes_and_of_the_lanes_follow_the_machine_of_the_system()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Volume du Mac", SettingsScreen.VolumeLabel(0, 2, "Mac"));
        Assert.Equal("Volume du micro", SettingsScreen.VolumeLabel(1, 2, "Mac"));
        Assert.Equal("Volume", SettingsScreen.VolumeLabel(0, 1, "Mac"));
        Assert.Equal("Son du Mac", EditorWave.LaneName(0, "Mac"));
        Assert.Equal("Micro", EditorWave.LaneName(1, "Mac"));
        Assert.Equal("", EditorWave.LaneName(2, "Mac"));
    }

    [Fact]
    public void The_dialog_of_Save_speaks_French_with_the_summary_of_the_edit()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Open();
        _rig.FocusOn(SettingsScreen.EditorVolumePrefix + 0);
        for (var i = 0; i < 6; i++)
        {
            _rig.Press(Vk.Right);
        }

        Click(SettingsScreen.EditorSaveId);
        var drawn = Painted();

        foreach (var word in new[] { "Enregistrer ta modification", "Nom de la copie", "L'original reste tel quel.", "Annuler", "Remplacer l'original", "Enregistrer une copie", "Gardé de 0:00,0 à 0:30,0 (30,0" + Nb + "s), volume +6" + Nb + "dB" })
        {
            Assert.Contains(word, drawn);
        }

        foreach (var word in new[] { "Save your edit", "Name of the copy", "Replace the original", "Save as a copy", "Kept 0:00.0" })
        {
            Assert.DoesNotContain(word, drawn);
        }

        // The suffix of the copy's name is its own word in each language (it is only appended, never read back).
        Assert.Equal(Name + " (modifié)", Screen.EditorDialog!.Name.Draft);
    }

    [Fact]
    public void The_summary_names_each_volume_that_moved_in_a_clip_of_two_files()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Open(clip: TwoFiles, files: 2);
        foreach (var file in new[] { 0, 1 })
        {
            _rig.FocusOn(SettingsScreen.EditorVolumePrefix + file);
            _rig.Press(Vk.Right);
            _rig.Press(Vk.Right);
        }

        Click(SettingsScreen.EditorSaveId);
        Assert.Contains("son du PC +2" + Nb + "dB, micro +2" + Nb + "dB", Painted());
    }

    [Fact]
    public void The_name_of_the_copy_is_refused_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Tape un nom.", EditorSaveDialogState.MessageOf(ClipRenameOutcome.Empty));
        Assert.Equal("Un nom ne peut pas finir par un point ou un espace.", EditorSaveDialogState.MessageOf(ClipRenameOutcome.TrailingDotOrSpace));
        Assert.Equal("Ce nom est trop long.", EditorSaveDialogState.MessageOf(ClipRenameOutcome.TooLong));
        Assert.StartsWith("Un nom ne peut pas contenir", EditorSaveDialogState.MessageOf(ClipRenameOutcome.BadCharacters));
        Assert.Equal("Impossible d'utiliser ce nom.", EditorSaveDialogState.MessageOf((ClipRenameOutcome)99));
    }

    [Theory]
    [InlineData(EditSaveOutcome.CannotRecycle, "La Corbeille ne peut pas prendre l'original. Rien n'a changé.")]
    [InlineData(EditSaveOutcome.TooBig, "Trop gros pour la soundboard" + Nb + ": 512" + Nb + "Ko max. Rien n'a été enregistré.")]
    [InlineData(EditSaveOutcome.SourceGone, "Le clip n'est plus là. Rien n'a été enregistré.")]
    [InlineData(EditSaveOutcome.Failed, "Impossible d'enregistrer la modification. L'original n'a pas changé.")]
    public void A_save_that_fails_says_why_in_French_and_the_editor_stays(EditSaveOutcome outcome, string text)
    {
        using var scope = Tr.Use(UiLanguage.French);
        Open(EditorMode.Soundboard);
        Click(SettingsScreen.EditorSaveId);
        Assert.Contains("Enregistrement...", Painted());

        Screen.EditorSaved(new EditSaveResult(outcome, []));
        var drawn = Painted();
        Assert.True(Screen.EditorOpen);
        Assert.Contains(text, drawn);
        Assert.DoesNotContain("Rien ne change", drawn);
        Assert.DoesNotContain("Nothing", drawn);
    }

    [Fact]
    public void The_problem_of_the_foot_is_worded_again_when_the_language_changes()
    {
        using var scope = Tr.Use(UiLanguage.English);
        Open(EditorMode.Soundboard);
        Click(SettingsScreen.EditorSaveId);
        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.TooBig, []));
        Assert.Contains(SettingsScreen.EditorTooBig, Painted());

        Tr.Set(UiLanguage.French);
        Screen.LanguageChanged();
        var drawn = Painted();
        Assert.Contains("Trop gros pour la soundboard", drawn);
        Assert.Contains("Vers la soundboard", drawn);
        Assert.DoesNotContain("Too big", drawn);
    }

    [Fact]
    public void A_playback_that_fails_says_so_in_French_and_the_sentence_goes_when_it_plays_again()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Open();
        Click(SettingsScreen.EditorPlayId);
        Screen.EditorPreviewChanged(PreviewState.Error);
        Assert.Contains("Impossible de lire la sélection.", Painted());

        Screen.EditorPreviewChanged(PreviewState.Playing);
        Assert.DoesNotContain("Impossible de lire", Painted());
        Assert.Contains("Arrêter", Painted());
    }

    [Fact]
    public void The_notices_of_a_save_are_said_in_French_on_the_line_of_the_Library()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Open();
        Click(SettingsScreen.EditorSaveId);
        Click(EditorSaveDialogState.ReplaceId);
        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.SavedAside, [@"C:\Clips\clip0002.mp3"], OriginalInBin: true));
        var drawn = PaintedFlat();
        Assert.Contains("L'original est dans la Corbeille. La modification a été enregistrée comme nouveau clip.", drawn);
        Assert.DoesNotContain("The original", drawn);
    }

    [Fact]
    public void A_replacement_that_could_not_be_kept_says_so_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var edited = _library.Lines!.Lines[2];
        Screen.SelectClip(edited.Main.Path);
        Open(clip: edited);
        Click(SettingsScreen.EditorSaveId);
        Click(EditorSaveDialogState.ReplaceId);
        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.SavedAside, []));
        Assert.Contains("L'original est dans la Corbeille. La modification n'a pas pu être gardée.", PaintedFlat());
    }

    [Fact]
    public void A_soundboard_copy_gives_its_hint_for_Discord_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Open(EditorMode.Soundboard);
        Click(SettingsScreen.EditorSaveId);
        Screen.EditorSaved(new EditSaveResult(EditSaveOutcome.Saved, [@"C:\Clips\clip0002.mp3"]));
        var drawn = PaintedFlat();
        Assert.Contains("Dans Discord" + Nb + ": Paramètres du serveur > Soundboard", drawn);
        Assert.DoesNotContain("In Discord", drawn);
    }

    [Theory]
    [InlineData(EditorLoadOutcome.Gone)]
    [InlineData(EditorLoadOutcome.Unreadable)]
    public void A_clip_that_can_t_be_read_says_so_in_French(EditorLoadOutcome outcome)
    {
        using var scope = Tr.Use(UiLanguage.French);
        Screen.OpenEditor(OneFile, EditorMode.Normal);
        Assert.Contains("Chargement du clip...", Painted());

        Screen.EditorLoaded(new EditorLoad(outcome, TimeSpan.Zero, [], ExportFormat.Mp3));
        var drawn = Painted();
        Assert.Contains("Impossible de lire ce clip", drawn);
        Assert.DoesNotContain("Can't read", drawn);
        Assert.DoesNotContain("Reading the clip", drawn);
    }

    [Fact]
    public void The_Recycle_Bin_of_the_sentences_is_the_Corbeille_on_every_system()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Contains("Corbeille", SettingsScreen.EditorSavedAside);
        Assert.DoesNotContain("Recycle", SettingsScreen.EditorSavedAside);
        Assert.Equal(SettingsScreen.EditorSavedAside, Hindsound.App.PlatformFeatures.Windows.Words(SettingsScreen.EditorSavedAside));
    }
}
