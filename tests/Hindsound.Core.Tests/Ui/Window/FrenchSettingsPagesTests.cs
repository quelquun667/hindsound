using System.Runtime.InteropServices;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The pages of the settings in French: Hotkeys, Durations, Audio and Export, the first-run card, the hotkey messages, the
/// choices of the lists and the words of each system. Each page is drawn as the dump draws it with <c>--lang fr</c>, and read
/// for its French words and for the English ones that must be gone.
/// </summary>
public sealed unsafe class FrenchSettingsPagesTests
{
    private sealed class RecordingText : ICanvasText
    {
        public List<string> Drawn { get; } = [];

        public float Measure(FontSpec font, string text) => text.Length * 0.6f * MathF.Floor(font.Size + 0.5f);

        public float LineHeight(FontSpec font) => MathF.Round(1.25f * MathF.Floor(font.Size + 0.5f));

        public void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb) => Drawn.Add(text);

        public void Dispose()
        {
        }
    }

    private sealed class Host : IWindowHost
    {
        public void Invalidate()
        {
        }

        public void ShowEdit(Box box, string text, Face face, float size, bool resetText)
        {
        }

        public void HideEdit()
        {
        }

        public bool IsForeground => false;

        public void FocusEdit()
        {
        }

        public void CaptureMouse()
        {
        }

        public void ReleaseMouse()
        {
        }

        public void MeterWanted(bool wanted)
        {
        }

        public string? PickFolder(string startFolder) => null;

        public void CloseWindow()
        {
        }
    }

    private sealed class Backend : ISettingsBackend
    {
        public void Applied(SettingsChange change)
        {
        }

        public void SaveNow()
        {
        }

        public void TogglePause()
        {
        }

        public void CopyLast()
        {
        }

        public void OpenFolder()
        {
        }

        public void ShowTestNotification()
        {
        }

        public void SuspendHotkeys()
        {
        }

        public IReadOnlyList<HotkeyResult> ApplyHotkeys(HotkeySettings hotkeys) => [];

        public IReadOnlyList<HotkeyResult> ResumeHotkeys() => [];
    }

    private sealed class Rig : IDisposable
    {
        private readonly uint* _bits;
        private readonly RecordingText _text = new();
        private readonly PixelCanvas _canvas;

        public Rig(PlatformFeatures features, bool firstRun = false)
        {
            _bits = (uint*)NativeMemory.AllocZeroed((nuint)(760 * 488), sizeof(uint));
            _canvas = new PixelCanvas(_bits, 760, 488, 1, _text);
            Screen = new SettingsScreen(new AppSettings().Normalize(), new Backend(), new Host(), _canvas, firstRun, features) { NoNativeEdit = true };
        }

        public SettingsScreen Screen { get; }

        public string Texts()
        {
            _text.Drawn.Clear();
            Screen.Paint(_canvas);
            return string.Join(' ', _text.Drawn);
        }

        public void Dispose() => NativeMemory.Free(_bits);
    }

    private static Dictionary<string, string> Dump(UiLanguage language)
    {
        var scenes = new Dictionary<string, string>();
        var texts = new RecordingText();
        uint* bits = null;
        try
        {
            using var scope = Tr.Use(language);
            SettingsDump.Render(1, (width, height, scale) =>
            {
                bits = (uint*)NativeMemory.AllocZeroed((nuint)(width * height), sizeof(uint));
                return new PixelCanvas(bits, width, height, scale, texts);
            }, (name, _) =>
            {
                scenes[name] = string.Join(" | ", texts.Drawn);
                texts.Drawn.Clear();
            });
        }
        finally
        {
            NativeMemory.Free(bits);
        }

        return scenes;
    }

    private static readonly Lazy<Dictionary<string, string>> French = new(() => Dump(UiLanguage.French));

    private static void Reads(string scene, string[] frenchWords, string[] englishWords)
    {
        var text = French.Value[scene];
        Assert.All(frenchWords, word => Assert.Contains(word, text));
        Assert.All(englishWords, word => Assert.DoesNotContain(word, text));
    }

    [Fact]
    public void The_Hotkeys_page_is_in_French()
    {
        Reads(
            "hotkeys@100",
            ["Raccourcis", "Enregistrer un clip", "Enregistrer et modifier", "Enregistrer 15\u00A0s", "Enregistrer 1\u00A0min", "Enregistrer 5\u00A0min", "Pause ou reprise", "Copier le dernier clip", "Réinitialiser"],
            ["Reset to defaults", "Save clip", "Save and edit", "Pause or resume", "Copy last clip", "Save up to"]);
    }

    [Fact]
    public void A_field_with_no_key_says_Aucun_and_one_being_set_asks_in_French()
    {
        Reads("hotkeys@100", ["Aucun"], ["Not set"]);
        Reads("hotkeys-listening@100", ["Appuie sur ta combinaison...", "Échap annule, Retour arrière efface"], ["Press your combo", "cancels"]);
    }

    [Fact]
    public void The_refusals_of_a_hotkey_are_in_French()
    {
        Reads(
            "hotkeys-errors@100",
            ["Raccourci déjà pris par une autre application", "Même raccourci qu'une autre action"],
            ["already in use", "Same hotkey"]);
    }

    [Fact]
    public void The_Durations_page_is_in_French()
    {
        Reads(
            "durations@100",
            ["Durée du clip", "15\u00A0s", "30\u00A0s", "1\u00A0min", "5\u00A0min", "Autre", "Garder en mémoire", "~87\u00A0Mo de RAM", "Une mémoire plus courte supprime l'audio le plus ancien"],
            ["Clip length", "Keep in memory", "of RAM", "shorter buffer", "Custom"]);
        Reads(
            "durations-custom@100",
            ["Durée perso", "Utilise m:ss ou des secondes, de 0:01 à 30:00"],
            ["Custom length", "Use m:ss"]);
    }

    [Fact]
    public void The_Audio_page_is_in_French()
    {
        Reads(
            "audio@100",
            ["Capturer", "Sortie", "Micro", "Niveau du micro", "Pistes", "Mixées", "Séparées", "Gain du son du PC", "Gain du micro"],
            ["Record", "Output", "Microphone", "Mic level", "Tracks", "Separate", "sound gain", "Mic gain"]);
        Reads("audio-list@100", ["Sortie par défaut"], ["Default output"]);
    }

    [Fact]
    public void The_Export_page_is_in_French()
    {
        Reads(
            "export@100",
            ["Format", "Qualité MP3", "Volume", "Aucune", "Pic", "Loudness", "Dossier", "Parcourir", "Nom", "Copier le clip dans le presse-papiers"],
            ["MP3 quality", "Folder", "Browse", "File name", "Copy clip to clipboard", "Peak"]);
    }

    [Fact]
    public void The_first_run_card_is_in_French_at_each_step()
    {
        Reads(
            "firstrun-1@100",
            ["Bienvenue dans Hindsound", "Choisis ton raccourci", "Vérifie ton micro", "Essaie", "Suivant", "Préviens-les"],
            ["Welcome to", "Pick your hotkey", "Check your mic", "Try it", "Next", "Let them know"]);
        Reads(
            "firstrun-2@100",
            ["Parle\u00A0: la barre doit bouger. Coupé pour le son du PC seul.", "Retour", "Suivant"],
            ["Speak:", "Back"]);
        Reads(
            "firstrun-3-done@100",
            ["Attends un peu.", "Dis un mot, puis appuie sur ton raccourci.", "Colle-le maintenant n'importe où.", "Terminer", "Retour"],
            ["Wait a few", "Say something", "paste it", "Done"]);
    }

    [Fact]
    public void The_updates_note_of_the_card_and_the_three_notes_of_the_General_page_say_tu()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Hindsound vérifie les mises à jour sur GitHub une fois par jour. GitHub voit ton adresse IP. Change ça dans Réglages > Général.", SettingsScreen.FirstRunUpdatesNote);
        Assert.Equal("Le bip peut s'entendre dans ton prochain clip si tu captures une sortie précise.", SettingsScreen.SoundHelp);
        Assert.Equal("Une fois par jour, Hindsound demande la dernière version à GitHub. GitHub voit ton adresse IP et l'heure, rien d'autre.", SettingsScreen.GitHubNote);
        Assert.Contains("Préviens-les", SettingsScreen.LegalNote);
    }

    [Fact]
    public void The_refusals_of_the_fields_are_in_French_and_stay_English_by_default()
    {
        Assert.Equal("Use a color code like #D4F53C", SettingsEditor.CheckAccent("lime").Message);
        Assert.Equal("The name can't be empty", SettingsEditor.CheckTemplate("").Message);

        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Utilise un code couleur comme #D4F53C", SettingsEditor.CheckAccent("lime").Message);
        Assert.Equal("Trop sombre pour un jeu. Choisis une couleur plus claire.", SettingsEditor.CheckAccent("#202020").Message);
        Assert.Equal("Le nom ne peut pas être vide", SettingsEditor.CheckTemplate("").Message);
        Assert.Equal("Un nom de fichier ne peut pas contenir \\ / : * ? \" < > |", SettingsEditor.CheckTemplate("a/b").Message);
        Assert.Equal("Balise {nope} inconnue. Utilise {app}, {date} ou {time}", SettingsEditor.CheckTemplate("{nope}").Message);
        Assert.Equal("Utilise m:ss ou des secondes, de 0:01 à 30:00", SettingsEditor.CheckCustomClip("nine").Message);
        Assert.Equal("Choisis un chemin de dossier complet", SettingsEditor.FolderError);
    }

    [Fact]
    public void The_choices_of_the_lists_are_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Tout le PC", SettingsEditor.RecordChoices([], null)[0].Label);
        Assert.Equal("Tout le Mac", SettingsEditor.RecordChoices([], null, machine: "Mac")[0].Label);
        Assert.Equal("Discord.exe (pas lancé)", SettingsEditor.RecordChoices([], "Discord.exe")[1].Label);
        Assert.Equal("Sortie par défaut", SettingsEditor.OutputChoices([], AudioSettings.DefaultDevice)[0].Label);
        Assert.Equal("Périphérique débranché", SettingsEditor.OutputChoices([], "gone")[1].Label);
        var mics = SettingsEditor.MicChoices([], AudioSettings.DefaultDevice);
        Assert.Equal("Micro par défaut", mics[0].Label);
        Assert.Equal("Coupé", mics[^1].Label);
        Assert.Equal("~87\u00A0Mo de RAM", new SettingsEditor(new AppSettings().Normalize(), _ => { }).RamText(300));
        Assert.Equal(["15\u00A0s", "30\u00A0s", "1\u00A0min", "2\u00A0min", "5\u00A0min", "Autre"], SettingsEditor.ClipLabels);
    }

    [Fact]
    public void The_messages_of_the_hotkey_layer_follow_the_language_when_they_are_read()
    {
        var english = HotkeyResult.InUseMessage;
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Hotkey already in use by another app", english);
        Assert.Equal("Raccourci déjà pris par une autre application", HotkeyResult.MessageOf(HotkeyStatus.InUse));
        Assert.Equal("Raccourci invalide", HotkeyResult.InvalidMessage);
        Assert.Equal("Les raccourcis n'ont pas répondu à temps\u00A0; réessaie", HotkeyResult.UnavailableMessage);
        Assert.Null(HotkeyResult.MessageOf(HotkeyStatus.Registered));
        Assert.Equal("Appuie sur ta combinaison...", HotkeyCapture.Prompt);
    }

    [Fact]
    public void A_refusal_already_on_screen_changes_with_the_language()
    {
        using var scope = Tr.Use(UiLanguage.English);
        using var rig = new Rig(PlatformFeatures.Windows);
        rig.Screen.ShowSection(Section.Hotkeys);
        rig.Screen.SetHotkeyResults([new HotkeyResult(HotkeyAction.SaveDefault, "Ctrl+F10", HotkeyStatus.InUse, HotkeyResult.InUseMessage)]);
        Assert.Contains("Hotkey already in use by another app", rig.Texts());

        Tr.Set(UiLanguage.French);
        rig.Screen.LanguageChanged();
        var french = rig.Texts();
        Assert.Contains("Raccourci déjà pris par une autre application", french);
        Assert.DoesNotContain("already in use", french);
    }

    [Fact]
    public void The_desktop_pages_of_the_shortcuts_are_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        using var portal = new Rig(PlatformFeatures.Linux(() => ShortcutMode.Portal));
        portal.Screen.ShowSection(Section.Hotkeys);
        var waiting = portal.Texts();
        Assert.Contains("En attente du bureau", waiting);
        Assert.Contains("Ton bureau définit ces raccourcis.", waiting);
        Assert.DoesNotContain("Waiting for your desktop", waiting);

        portal.Screen.SetHotkeyResults([.. Enum.GetValues<HotkeyAction>().Select(a => new HotkeyResult(a, null, HotkeyStatus.NotSet, null))]);
        Assert.Contains("Aucun", portal.Texts());

        using var command = new Rig(PlatformFeatures.Linux(() => ShortcutMode.Command, () => "hindsound --save"));
        command.Screen.ShowSection(Section.Hotkeys);
        var page = command.Texts();
        Assert.Contains("Ce bureau n'a pas de raccourci global pour les applications.", page);
        Assert.Contains("associe cette commande à une touche\u00A0:", page);
        Assert.Contains("Copier", page);
        Assert.Contains("La touche enregistre alors un clip, comme le bouton Enregistrer.", page);
        Assert.DoesNotContain("This desktop", page);

        using var generic = new Rig(PlatformFeatures.Linux(() => ShortcutMode.Command, () => null));
        generic.Screen.ShowSection(Section.Hotkeys);
        Assert.Contains("le chemin complet du programme Hindsound suivi de --save.", generic.Texts());
    }

    [Fact]
    public void The_words_of_each_system_follow_the_language()
    {
        var mac = PlatformFeatures.Mac(static () => null);
        var linux = PlatformFeatures.Linux(() => ShortcutMode.Capture);
        var windows = PlatformFeatures.Windows;
        Assert.Equal("Trash", mac.TrashName);
        Assert.Equal("Whole Mac", mac.WholeMachine);
        Assert.Equal("This name is reserved", linux.ReservedNameText);

        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Corbeille", windows.TrashName);
        Assert.Equal("Corbeille", mac.TrashName);
        Assert.Equal("Corbeille", linux.TrashName);
        Assert.Equal("Tout le Mac", mac.WholeMachine);
        Assert.Equal("Tout le PC", windows.WholeMachine);
        Assert.Equal("Ce nom est réservé", mac.ReservedNameText);
        Assert.Equal("Windows garde ce nom pour lui", windows.ReservedNameText);
        Assert.Equal("Lancer avec la session", linux.StartupLabel);
        Assert.Equal("Ouvrir à la connexion", mac.StartupLabel);

        // A French text names the Corbeille already: the words of the system leave it as it is, and still change the English one.
        Assert.Equal("Déplacer dans la Corbeille", mac.Words("Déplacer dans la Corbeille"));
    }

    [Fact]
    public void The_memory_estimate_has_its_unit_in_French()
    {
        Assert.Equal("~87 MB", RamEstimate.Format(300, micActive: true));
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("~87\u00A0Mo", RamEstimate.Format(300, micActive: true));
        Assert.Equal("~58\u00A0Mo", RamEstimate.Format(300, micActive: false));
    }

    [Fact]
    public void The_update_card_is_in_French()
    {
        Reads("update-card@100", ["Mettre à jour vers", "Enregistrer d'abord", "Mettre à jour", "Annuler"], ["Update to", "Save it, then update", "Cancel"]);
        Reads("update-card-clip@100", ["Un clip s'enregistre encore"], ["A clip is still being saved"]);
    }
}
