using System.Runtime.InteropServices;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Updates;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// A change of language while the window is open: what a page keeps between two builds (the wave's words, a refusal under a
/// field, the message under the folder) is made again in the new language, not kept in the old one.
/// </summary>
public sealed unsafe class LanguageLiveChangeTests
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

    private sealed class Host : IWindowHost
    {
        public string? Picked { get; set; }

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

        public string? PickFolder(string startFolder) => Picked;

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

    private sealed class Memory(WaveFacts facts) : INowPanel
    {
        public WaveFacts WaveFacts => facts;
    }

    private sealed class Rig : IDisposable
    {
        private readonly uint* _bits = (uint*)NativeMemory.AllocZeroed((nuint)(760 * 488), sizeof(uint));
        private readonly RecordingText _text = new();
        private readonly PixelCanvas _canvas;

        public Rig()
        {
            _canvas = new PixelCanvas(_bits, 760, 488, 1, _text);
            Screen = new SettingsScreen(new AppSettings().Normalize(), new Backend(), Host, _canvas, false) { NoNativeEdit = true };
        }

        public Host Host { get; } = new();

        public SettingsScreen Screen { get; }

        public string Texts()
        {
            _text.Drawn.Clear();
            Screen.Paint(_canvas);
            return string.Join(" | ", _text.Drawn);
        }

        public void Dispose() => NativeMemory.Free(_bits);
    }

    private static AudioEngineStatus Status() =>
        new(CaptureActivity.Capturing, true, true, true, "Speakers", "Headset", true, TimeSpan.FromMinutes(5), 86_688_000);

    // A memory that is full: the engine has nothing new to say, so no status comes after the change of language.
    private static WaveFacts FullMemory()
    {
        var end = 5000L * Rate;
        return new WaveFacts(null, null, end - 300L * Rate, end, 300 * Rate, []);
    }

    [Fact]
    public void The_system_that_refused_an_update_is_told_by_what_it_is_and_not_by_the_words_it_uses()
    {
        var windows = PlatformFeatures.Windows;
        var linux = PlatformFeatures.Linux(() => ShortcutMode.Command);
        var mac = PlatformFeatures.Mac(() => null);
        Assert.True(windows.InstalledWindows);
        Assert.False(linux.InstalledWindows);
        Assert.False(mac.InstalledWindows);

        // The words of the toggle do not say the system: a desktop that words it like Windows is still not Windows, and the reverse.
        var linuxWithWindowsWords = linux with { StartupWords = windows.StartupWords };
        var windowsWithOtherWords = windows with { StartupWords = ("Open at login", "Ouvrir à la connexion") };
        Assert.Equal("The system refused to start the update.", UpdatePanel.RunMessage(RunStatus.StartFailed, linuxWithWindowsWords));
        Assert.Equal("Windows refused to start the update.", UpdatePanel.RunMessage(RunStatus.StartFailed, windowsWithOtherWords));
    }

    [Fact]
    public void The_wave_seen_in_English_says_its_titles_and_its_save_line_in_French_once_the_language_changes()
    {
        using var scope = Tr.Use(UiLanguage.English);
        using var rig = new Rig();
        rig.Screen.SetNow(new Memory(FullMemory()));
        rig.Screen.UpdateStatus(Status());
        rig.Screen.ShowSection(Section.Now);
        var english = rig.Texts();
        Assert.Contains("Whole PC", english);
        Assert.Contains("Save", english);

        Tr.Set(UiLanguage.French);
        rig.Screen.LanguageChanged();
        var french = rig.Texts();

        Assert.Contains("Tout le PC", french);
        Assert.Contains("Prochain clip", french);
        Assert.DoesNotContain("Whole PC", french);
        Assert.DoesNotContain("Save", french);
    }

    [Fact]
    public void A_wave_made_again_after_the_language_changes_remakes_its_save_line()
    {
        using var scope = Tr.Use(UiLanguage.English);
        var wave = new NowWave();
        var facts = FullMemory();
        wave.Follow(facts);
        wave.Look(1, facts, 30, Status());
        Assert.StartsWith("Save: last", wave.SaveText, StringComparison.Ordinal);

        Tr.Set(UiLanguage.French);
        wave.RemakeWords();
        wave.Look(1, facts, 30, Status());

        Assert.StartsWith("Prochain clip", wave.SaveText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_under_the_file_name_field_follows_the_language()
    {
        using var scope = Tr.Use(UiLanguage.English);
        using var rig = new Rig();
        rig.Screen.ShowSection(Section.Export);
        var field = (TextField)rig.Screen.WidgetById("export.template")!;
        field.Typed("{wrong}", null);
        Assert.Contains("Unknown tag {wrong}", rig.Texts());

        Tr.Set(UiLanguage.French);
        rig.Screen.LanguageChanged();
        var french = rig.Texts();

        Assert.Contains("Balise {wrong} inconnue", french);
        Assert.DoesNotContain("Unknown tag", french);
    }

    [Fact]
    public void The_message_under_the_folder_follows_the_language()
    {
        using var scope = Tr.Use(UiLanguage.English);
        using var rig = new Rig();
        rig.Host.Picked = "not a full path";
        rig.Screen.ShowSection(Section.Export);
        var browse = rig.Screen.WidgetById("export.browse")!.Bounds;
        rig.Screen.MouseDown(browse.MidX, browse.MidY);
        rig.Screen.MouseUp(browse.MidX, browse.MidY);
        Assert.Contains("Pick a full folder path", rig.Texts());

        Tr.Set(UiLanguage.French);
        rig.Screen.LanguageChanged();
        var french = rig.Texts();

        Assert.Contains("Choisis un chemin de dossier complet", french);
        Assert.DoesNotContain("Pick a full folder path", french);
    }
}
