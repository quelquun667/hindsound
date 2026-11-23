using System.Runtime.InteropServices;
using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The pages in French: the side bar, the band and the General page, as the dump draws them with <c>--lang fr</c>, and the
/// window drawn again at once when the language changes.
/// </summary>
public sealed unsafe class LanguagePagesTests
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

    /// <summary>Applies a change of the language as the app does: the language is set before the window builds again.</summary>
    private sealed class Backend(AppSettings settings) : ISettingsBackend
    {
        public void Applied(SettingsChange change)
        {
            if ((change & SettingsChange.Language) != 0)
            {
                Tr.Set(UiLanguages.Resolve(settings.General.Language, []));
            }
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

    private static Dictionary<string, List<string>> Dump(UiLanguage language)
    {
        var scenes = new Dictionary<string, List<string>>();
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
                scenes[name] = [.. texts.Drawn];
                texts.Drawn.Clear();
            });
        }
        finally
        {
            NativeMemory.Free(bits);
        }

        return scenes;
    }

    // The title of the band is cut by the wide letters of this canvas: its words are read from the band itself, below.
    private static readonly string[] FrenchPilot =
    [
        "Maintenant", "Bibliothèque", "Réglages", "Raccourcis", "Durées", "Audio", "Export", "Général",
        "micro actif", "Pause", "Enregistrer 30\u00A0s", "~87\u00A0Mo",
        "Couleur d'accent", "Lancer avec Windows", "Langue", "Système (Français)", "Taille de la fenêtre", "100\u00A0%", "120\u00A0%", "Notifications d'enregistrement",
        "Dernier clip", "720\u00A0Ko", "Copier", "Ouvrir le dossier",
    ];

    private static readonly string[] EnglishPilot =
    [
        "Now", "Library", "Settings", "Hotkeys", "Durations", "General", "Capturing", "mic on", "Save up to 30s", "~87\u00A0MB",
        "Accent color", "Start with Windows", "Language", "System (English)", "Window size", "100%", "120%", "Save notifications", "Last clip", "Copy", "Open folder",
    ];

    [Fact]
    public void The_dump_draws_the_pilot_in_French_with_lang_fr_and_in_English_by_default()
    {
        var french = Dump(UiLanguage.French)["general@100"];
        var english = Dump(UiLanguage.English)["general@100"];

        Assert.All(FrenchPilot, text => Assert.Contains(text, french));
        Assert.All(EnglishPilot.Except(["Audio", "Export"]), text => Assert.DoesNotContain(text, french));
        Assert.All(EnglishPilot, text => Assert.Contains(text, english));

        // The dump leaves the language as it found it.
        Assert.Equal(UiLanguage.English, Tr.Language);
    }

    [Fact]
    public void The_band_says_its_states_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var capturing = new AudioEngineStatus(CaptureActivity.Capturing, true, true, true, "Speakers", "Headset", true, TimeSpan.FromMinutes(5), 86_688_000);
        var band = StatusText.From(capturing, 300);

        Assert.Equal("Capture", band.Title);
        Assert.Equal("micro actif", band.Mic);
        Assert.Equal("Pause", band.PauseLabel);
        Assert.Equal("~87\u00A0Mo", band.Ram);
        Assert.Equal("En pause", StatusText.From(capturing with { Activity = CaptureActivity.Paused }, 300).Title);
        Assert.Equal("Reprendre", StatusText.From(capturing with { Activity = CaptureActivity.Paused }, 300).PauseLabel);
        Assert.Equal("Démarrage", StatusText.From(null).Title);
        Assert.Equal("Enregistrer 30\u00A0s", StatusText.SaveLabel(30));
        Assert.Equal("Enregistrer 2\u00A0min", StatusText.SaveLabel(120));
        Assert.Equal("Enregistrer 1:30", StatusText.SaveLabel(90));
    }

    [Fact]
    public void Each_language_is_named_in_its_own_words_and_System_in_the_words_of_the_moment()
    {
        uint* bits = (uint*)NativeMemory.AllocZeroed((nuint)(760 * 488), sizeof(uint));
        try
        {
            using var scope = Tr.Use(UiLanguage.English);
            var texts = new RecordingText();
            var canvas = new PixelCanvas(bits, 760, 488, 1, texts);
            var settings = new AppSettings().Normalize();
            var screen = new SettingsScreen(settings, new Backend(settings), new Host(), canvas, false) { SystemLanguage = () => UiLanguage.French };
            screen.ShowSection(Section.General);
            List<string> Painted()
            {
                texts.Drawn.Clear();
                screen.Paint(canvas);
                return [.. texts.Drawn];
            }

            Assert.Contains("System (Français)", Painted());
            screen.Editor.SetLanguage(GeneralSettings.LanguageEnglish);
            Assert.Contains("English", Painted());
            screen.Editor.SetLanguage(GeneralSettings.LanguageFrench);
            Assert.Contains("Français", Painted());
        }
        finally
        {
            NativeMemory.Free(bits);
        }
    }

    [Fact]
    public void A_change_of_the_setting_draws_the_window_again_in_the_new_language_at_once()
    {
        uint* bits = (uint*)NativeMemory.AllocZeroed((nuint)(760 * 488), sizeof(uint));
        try
        {
            using var scope = Tr.Use(UiLanguage.English);
            var texts = new RecordingText();
            var canvas = new PixelCanvas(bits, 760, 488, 1, texts);
            var settings = new AppSettings().Normalize();
            var screen = new SettingsScreen(settings, new Backend(settings), new Host(), canvas, false) { SystemLanguage = () => UiLanguage.English };
            screen.UpdateStatus(new AudioEngineStatus(CaptureActivity.Capturing, true, true, true, "Speakers", "Headset", true, TimeSpan.FromMinutes(5), 86_688_000));
            screen.ShowSection(Section.General);
            List<string> Painted()
            {
                texts.Drawn.Clear();
                screen.Paint(canvas);
                return [.. texts.Drawn];
            }

            Assert.Contains("General", Painted());

            // Through the page, as a pick in the list does: the screen applies, then builds again.
            screen.Editor.SetLanguage(GeneralSettings.LanguageFrench);
            var french = Painted();
            Assert.Contains("Général", french);
            Assert.Contains("Langue", french);
            Assert.Contains("micro actif", french);
            Assert.Contains("Copier", french);
            Assert.Contains("Français", french);
            Assert.DoesNotContain("General", french);

            // From the app, as a hand edit of the file does: the language is set, then the window is told.
            Tr.Set(UiLanguage.English);
            screen.LanguageChanged();
            var english = Painted();
            Assert.Contains("General", english);
            Assert.Contains("Capturing", english);
            Assert.Contains("mic on", english);
            Assert.DoesNotContain("Général", english);
        }
        finally
        {
            NativeMemory.Free(bits);
        }
    }
}
