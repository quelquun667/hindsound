using System.Runtime.InteropServices;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The Now page, the wave's words, the first-launch note, the update card and the tray tips in French.</summary>
public sealed unsafe class LanguageNowTests
{
    private const int Rate = InternalFormat.SampleRate;

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

    private static AudioEngineStatus Status(CaptureActivity activity = CaptureActivity.Capturing, bool micOn = true, bool micCapturing = true) =>
        new(activity, true, micOn, micCapturing, "Speakers", "Headset", true, TimeSpan.FromMinutes(5), 86_688_000);

    private static WaveFacts Facts(int memorySeconds, double heldSeconds, params SourceChange[] changes)
    {
        var end = 5000L * Rate;
        return new WaveFacts(null, null, end - (long)(heldSeconds * Rate), end, memorySeconds * Rate, changes);
    }

    private static IReadOnlyList<HotkeyResult> Refused(HotkeyStatus status) =>
        [new HotkeyResult(HotkeyAction.SaveDefault, "Ctrl+Shift+F10", status, "x")];

    [Fact]
    public void The_Now_page_draws_in_French_with_no_English_left_on_it()
    {
        var scenes = Dump(UiLanguage.French);
        var page = scenes["now-app-apres-app@100"];

        Assert.All(["Capturer", "Micro", "Capturer ma voix", "maintenant", "Discord, puis Spotify", "Spotify seul à partir d'ici", "Enregistrement : dernières 0:30"], text => Assert.Contains(text, page));
        Assert.All(["Record", "Mic", "Record my voice", "now", "Discord, then Spotify", "Spotify only from here"], text => Assert.DoesNotContain(text, page));
        Assert.DoesNotContain(page, text => text.StartsWith("Save: last", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("now-app-absente@100", "(pas lancé)")]
    [InlineData("now-app-son-perdu@100", "(son perdu)")]
    [InlineData("now-micro-perdu@100", "Micro perdu")]
    [InlineData("now-micro-coupe@100", "Micro coupé")]
    [InlineData("now-pause@100", "en pause")]
    [InlineData("now-sans-peripherique@100", "Aucun périphérique audio")]
    [InlineData("now-nom-long@100", "Remplissage...")]
    public void Each_state_of_the_wave_says_itself_in_French(string scene, string word)
    {
        var page = Dump(UiLanguage.French)[scene];

        Assert.Contains(page, text => text.Contains(word, StringComparison.Ordinal));
        Assert.DoesNotContain(page, text => text.Contains("not running", StringComparison.Ordinal) || text.Contains("sound lost", StringComparison.Ordinal)
            || text.Contains("Mic lost", StringComparison.Ordinal) || text.Contains("Mic off", StringComparison.Ordinal) || text.Contains("Filling", StringComparison.Ordinal)
            || text == "paused" || text.Contains("No audio device", StringComparison.Ordinal));
    }

    [Fact]
    public void The_titles_the_axis_and_the_save_line_of_the_wave_are_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var moved = Facts(300, 130, new SourceChange(5000L * Rate - 10L * Rate, "Spotify.exe", "Discord.exe"));

        Assert.Equal("Micro", WaveView.MicLaneTitle(Status()));
        Assert.Equal("Micro perdu", WaveView.MicLaneTitle(Status(micCapturing: false)));
        Assert.Equal("Micro coupé", WaveView.MicLaneText(Status(micOn: false)));
        Assert.Equal("Micro non capté", WaveView.MicLaneText(Status(micCapturing: false), hasTrack: false));
        Assert.Equal(new WaveAxis("-5:00", "-2:30", "maintenant"), WaveView.AxisLabels(300, CaptureActivity.Capturing));
        Assert.Equal("en pause", WaveView.AxisLabels(300, CaptureActivity.Paused).Right);
        Assert.Equal("Enregistrement : dernières 0:30", WaveView.SaveLine(30, Facts(300, 100)));
        Assert.Equal("Enregistrement : dernières 0:12 sur 0:30", WaveView.SaveLine(30, Facts(300, 12)));
        Assert.StartsWith("Enregistrement : dernières 0:30 (0:", WaveView.SaveLine(30, moved));
        Assert.EndsWith(" d'avant Spotify)", WaveView.SaveLine(30, moved));
        Assert.Equal("Tout le PC à partir d'ici", WaveView.MarkerText(null));
        Assert.Equal("Discord seul à partir d'ici", WaveView.MarkerText("Discord.exe"));
        Assert.Equal("Discord (pas lancé)", WaveView.SystemLaneTitle(Status() with { SourceApp = "Discord.exe", SourceAppMissing = true }, WaveMarker.None));
        Assert.Equal("Capture impossible : Discord", WaveView.NoCaptureText(Status(CaptureActivity.NoDevice) with { SourceApp = "Discord.exe" }));
        Assert.Equal("Aucun périphérique audio", WaveView.NoCaptureText(Status(CaptureActivity.NoDevice)));
    }

    [Fact]
    public void The_wave_keeps_saying_it_in_English_when_the_language_is_English()
    {
        Assert.Equal("Mic lost", WaveView.MicLost);
        Assert.Equal("Save: last 0:12 of 0:30", WaveView.SaveLine(30, Facts(300, 12)));
        Assert.Equal(new WaveAxis("-5:00", "-2:30", "now"), WaveView.AxisLabels(300, CaptureActivity.Capturing));
    }

    [Fact]
    public void The_first_launch_note_the_update_card_and_the_clip_lengths_are_French()
    {
        using var scope = Tr.Use(UiLanguage.French);

        Assert.Equal("Enregistre jusqu'à 30 secondes, de partout, même en jeu.", StatusText.FirstRunHotkeyNote(30));
        Assert.Equal("Enregistre jusqu'à 2 minutes, de partout, même en jeu.", StatusText.FirstRunHotkeyNote(120));
        Assert.Equal("1 seconde", StatusText.ClipWords(1));
        Assert.Equal("1:30", StatusText.ClipWords(90));
        Assert.Equal("La mise à jour redémarre Hindsound. Ce qui est gardé en mémoire (5:00) sera perdu.", StatusText.UpdateCardBody(Status(), 300));
        Assert.Equal("La mise à jour redémarre Hindsound.", StatusText.UpdateCardBody(Status() with { Buffered = TimeSpan.FromSeconds(2) }, 300));
        Assert.Equal("+3 dB", StatusText.GainLabel(3));
        Assert.Equal("Enregistrer 5 min", StatusText.SaveLabel(300));
    }

    [Fact]
    public void The_hotkey_field_prompts_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var capture = new HotkeyCapture();
        capture.Begin(HotkeyAction.SaveDefault);
        capture.Key(Vk.Back, 0);

        Assert.Equal("Appuie sur ta combinaison...", HotkeyCapture.Prompt);
        Assert.Equal("Le raccourci principal d'enregistrement ne peut pas être vide", capture.Message);
    }

    [Fact]
    public void The_tip_of_the_icon_is_French_on_screen_and_stays_English_in_the_journal()
    {
        IReadOnlyList<HotkeyResult> none = [];
        using var scope = Tr.Use(UiLanguage.French);

        Assert.Equal("Hindsound - Aucun périphérique audio", StatusTransitions.TrayOf(Status(CaptureActivity.NoDevice), none, false).Tip);
        Assert.Equal("Hindsound - Un clip a un problème", StatusTransitions.TrayOf(Status(), none, true).Tip);
        Assert.Equal("Hindsound - Fichier de réglages non lu : valeurs par défaut, micro coupé", StatusTransitions.TrayOf(Status(), none, false, onDefaults: true).Tip);
        Assert.Equal("Hindsound - Micro non capté", StatusTransitions.TrayOf(Status(micCapturing: false), none, false).Tip);
        Assert.Equal("Hindsound - fin du clip en cours, puis fermeture", StatusTransitions.TrayOf(Status(), none, false, closingAfterClip: true).Tip);
        Assert.Equal("Raccourci d'enregistrement déjà pris", StatusTransitions.SaveHotkeyProblem(Refused(HotkeyStatus.InUse)));
        Assert.Equal("Raccourci d'enregistrement inutilisable", StatusTransitions.SaveHotkeyProblem(Refused(HotkeyStatus.Unavailable)));

        // The journal line is written in English whatever the language of the screen.
        using (Tr.Use(UiLanguage.English))
        {
            Assert.Equal("Hindsound - No audio device", StatusTransitions.TrayOf(Status(CaptureActivity.NoDevice), none, false).Tip);
            Assert.Equal("Hindsound - " + StatusTransitions.OnDefaultsProblem, StatusTransitions.TrayOf(Status(), none, false, onDefaults: true).Tip);
            Assert.Equal(StatusTransitions.ClosingTip, StatusTransitions.TrayOf(Status(), none, false, closingAfterClip: true).Tip);
        }
    }
}
