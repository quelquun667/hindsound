using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// What the pages call the computer: "PC" on Windows and on a Linux desktop, exactly as the pages have always said it, and "Mac" on a
/// Mac. Each text is fixed on each system, then every scene of the dump is drawn with the features of each system and read for the
/// word that is not its own.
/// </summary>
public sealed unsafe class MachineNameTests
{
    private const int Rate = InternalFormat.SampleRate;

    public static TheoryData<string, string> Systems => new()
    {
        { "Windows", "PC" },
        { "Linux", "PC" },
        { "Mac", "Mac" },
    };

    private static PlatformFeatures Of(string system) => system switch
    {
        "Windows" => PlatformFeatures.Windows,
        "Linux" => PlatformFeatures.Linux(() => ShortcutMode.Capture),
        _ => PlatformFeatures.Mac(static () => null),
    };

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

    private static Dictionary<string, List<string>> Scenes(PlatformFeatures features)
    {
        var scenes = new Dictionary<string, List<string>>();
        var texts = new RecordingText();
        uint* bits = null;
        try
        {
            SettingsDump.Render(1, (width, height, scale) =>
            {
                bits = (uint*)NativeMemory.AllocZeroed((nuint)(width * height), sizeof(uint));
                return new PixelCanvas(bits, width, height, scale, texts);
            }, (name, _) =>
            {
                scenes[name] = [.. texts.Drawn];
                texts.Drawn.Clear();
            }, features);
        }
        finally
        {
            NativeMemory.Free(bits);
        }

        return scenes;
    }

    private static WaveFacts FactsWithChangeToWhole(long end) =>
        new(null, null, end - 60L * Rate, end, 300 * Rate, [new SourceChange(end - 20L * Rate, null)]);

    [Theory]
    [MemberData(nameof(Systems))]
    public void The_features_name_the_machine(string system, string machine)
    {
        var features = Of(system);
        Assert.Equal(machine, features.MachineName);
        Assert.Equal("Whole " + machine, features.WholeMachine);
    }

    [Fact]
    public void Windows_keeps_the_PC_words_it_has_always_had()
    {
        Assert.Equal("PC", new PlatformFeatures().MachineName);
        Assert.Equal("Whole PC", PlatformFeatures.Windows.WholeMachine);
        Assert.Equal("Whole PC from here", WaveView.MarkerText(null));
        Assert.Equal("PC volume", ScreenEditor.VolumeLabel(0, 2));
        Assert.Equal("Whole PC", SettingsEditor.RecordChoices([], null)[0].Label);
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void The_line_of_a_change_and_the_title_of_the_lane_name_the_machine(string system, string machine)
    {
        var features = Of(system);
        Assert.Equal("Whole " + machine + " from here", WaveView.MarkerText(null, features));
        Assert.Equal("Discord only from here", WaveView.MarkerText("Discord.exe", features));
        Assert.Equal("Whole " + machine, WaveView.SystemLaneTitle(null, WaveMarker.None, features));
        Assert.Equal("Save: last 1:00 (0:20 from before Whole " + machine + ")", WaveView.SaveLine(60, FactsWithChangeToWhole(5000L * Rate), features));
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void The_lists_and_the_labels_of_the_editor_name_the_machine(string system, string machine)
    {
        var features = Of(system);
        Assert.Equal("Whole " + machine, SettingsEditor.RecordChoices([], null, machine: features.MachineName)[0].Label);
        Assert.Equal("Whole " + machine, EditorWave.LaneName(0, features.MachineName));
        Assert.Equal("Mic", EditorWave.LaneName(1, features.MachineName));
        Assert.Equal("", EditorWave.LaneName(2, features.MachineName));
        Assert.Equal(machine + " volume", ScreenEditor.VolumeLabel(0, 2, features.MachineName));
        Assert.Equal("Mic volume", ScreenEditor.VolumeLabel(1, 2, features.MachineName));
        Assert.Equal("Volume", ScreenEditor.VolumeLabel(0, 1, features.MachineName));
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void The_journal_says_which_machine_is_recorded_whole(string system, string machine)
    {
        var line = HindsoundApp.EngineLine(new AudioEngineOptions(TimeSpan.FromMinutes(5)), Of(system));
        Assert.Contains("app (whole " + machine + "),", line);
        Assert.Contains("app Game.exe,", HindsoundApp.EngineLine(new AudioEngineOptions(TimeSpan.FromMinutes(5), SourceApp: "Game.exe"), Of(system)));
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void Every_scene_of_the_dump_says_the_machine_of_its_system_and_never_the_other(string system, string machine)
    {
        var scenes = Scenes(Of(system));
        Assert.True(scenes.Count >= 80, "the dump has its scenes: " + scenes.Count);
        var all = scenes.SelectMany(s => s.Value.Select(text => (Scene: s.Key, Text: text))).ToList();

        var other = machine == "Mac" ? new Regex(@"\bPC\b") : new Regex(@"\bMac\b");
        Assert.Empty(all.Where(t => other.IsMatch(t.Text)).Select(t => t.Scene + ": " + t.Text));

        string[] expected =
        [
            "Whole " + machine,
            machine + " sound gain",
            "Speak: the bar should move. Off if you only want the " + machine + " sound.",
            "Whole " + machine + " from here",
            machine + " volume",
        ];
        foreach (var text in expected)
        {
            Assert.Contains(all, t => t.Text == text);
        }
    }
}
