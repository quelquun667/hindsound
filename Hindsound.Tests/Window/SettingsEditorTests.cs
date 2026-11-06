using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Window;

public class SettingsEditorTests
{
    private readonly List<SettingsChange> _changes = [];

    private SettingsEditor Editor(AppSettings? settings = null) =>
        new((settings ?? new AppSettings()).Normalize(), _changes.Add);

    [Theory]
    [InlineData("45", 45)]
    [InlineData(" 45s ", 45)]
    [InlineData("1:30", 90)]
    [InlineData("0:01", 1)]
    [InlineData("2m", 120)]
    [InlineData("2 min", 120)]
    [InlineData("30:00", 1800)]
    public void A_duration_is_read_in_the_forms_people_type(string text, int seconds)
    {
        Assert.True(SettingsEditor.TryParseDuration(text, out var parsed));
        Assert.Equal(seconds, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1801")]
    [InlineData("30:01")]
    [InlineData("1:5")]
    [InlineData("1:75")]
    [InlineData("-5")]
    [InlineData("nine")]
    [InlineData("1:2:3")]
    [InlineData("99999999999")]
    public void A_duration_outside_what_a_clip_can_be_is_refused(string text)
    {
        Assert.False(SettingsEditor.TryParseDuration(text, out _));
        Assert.Equal(SettingsEditor.CustomClipError, SettingsEditor.CheckCustomClip(text).Message);
    }

    [Fact]
    public void An_invalid_custom_clip_changes_nothing()
    {
        var editor = Editor();

        editor.CommitCustomClip("nine");

        Assert.Equal(30, editor.Settings.ClipSeconds);
        Assert.Empty(_changes);
    }

    [Fact]
    public void A_clip_longer_than_the_buffer_takes_the_buffer_up()
    {
        var editor = Editor(new AppSettings { BufferSeconds = 60 });

        editor.SetClipSeconds(120);

        Assert.Equal(120, editor.Settings.BufferSeconds);
        Assert.Equal(SettingsChange.Clip | SettingsChange.Buffer, _changes.Single());
    }

    [Fact]
    public void A_clip_that_fits_leaves_the_buffer_alone()
    {
        var editor = Editor();

        editor.SetClipSeconds(60);

        Assert.Equal(300, editor.Settings.BufferSeconds);
        Assert.Equal(SettingsChange.Clip, _changes.Single());
    }

    [Theory]
    [InlineData(30, 60, 60)]
    [InlineData(90, 60, 90)]
    [InlineData(300, 120, 300)]
    public void The_buffer_never_goes_under_the_longest_clip(int clip, int asked, int kept)
    {
        var editor = Editor(new AppSettings { ClipSeconds = clip, BufferSeconds = 600 });

        editor.SetBufferSeconds(asked);

        Assert.Equal(kept, editor.Settings.BufferSeconds);
    }

    [Fact]
    public void A_save_5_min_hotkey_counts_as_the_longest_clip()
    {
        var editor = Editor(new AppSettings { BufferSeconds = 60 });

        editor.SetHotkeys(editor.WithGesture(Hindsound.Input.HotkeyAction.Save5m, "Ctrl+Alt+F5"));

        Assert.Equal(300, editor.Settings.BufferSeconds);
        Assert.Equal(SettingsChange.Hotkeys | SettingsChange.Buffer, _changes.Single());
    }

    [Fact]
    public void The_slider_scale_stretches_for_a_long_clip_or_a_long_buffer()
    {
        Assert.Equal(600, Editor().BufferSliderMax);
        Assert.Equal(780, Editor(new AppSettings { ClipSeconds = 750, BufferSeconds = 750 }).BufferSliderMax);
        Assert.Equal(1800, Editor(new AppSettings { BufferSeconds = 1800 }).BufferSliderMax);
    }

    [Fact]
    public void The_ram_text_follows_the_value_and_the_microphone()
    {
        var editor = Editor();
        Assert.Equal(RamEstimate.Format(300, true) + " of RAM", editor.RamText(300));
        Assert.Equal(RamEstimate.Format(600, true) + " of RAM", editor.RamText(600));

        editor.SetMic(AudioSettings.DisabledMic);

        Assert.Equal(RamEstimate.Format(300, false) + " of RAM", editor.RamText(300));
    }

    [Theory]
    [InlineData(15, 0)]
    [InlineData(300, 4)]
    [InlineData(90, SettingsEditor.CustomClip)]
    public void The_clip_button_comes_from_the_value(int seconds, int segment)
    {
        Assert.Equal(segment, Editor(new AppSettings { ClipSeconds = seconds }).ClipSegment);
    }

    [Theory]
    [InlineData("#35D6FF", null)]
    [InlineData("35d6ff", null)]
    [InlineData("#fff", null)]
    [InlineData("#202020", SettingsEditor.AccentDarkError)]
    [InlineData("#000000", SettingsEditor.AccentDarkError)]
    [InlineData("#12345", SettingsEditor.AccentFormatError)]
    [InlineData("lime", SettingsEditor.AccentFormatError)]
    [InlineData("", SettingsEditor.AccentFormatError)]
    public void The_custom_colour_gets_its_verdict(string text, string? message)
    {
        Assert.Equal(message, SettingsEditor.CheckAccent(text).Message);
    }

    [Fact]
    public void A_colour_too_dark_is_not_kept()
    {
        var editor = Editor();

        var check = editor.SetAccentHex("#202020");

        Assert.False(check.Ok);
        Assert.Equal(AccentColors.LimeHex, editor.Settings.General.AccentColor);
        Assert.Empty(_changes);
    }

    [Fact]
    public void A_valid_colour_is_kept_in_its_canonical_form_and_announced()
    {
        var editor = Editor();

        editor.SetAccentHex("35d6ff");

        Assert.Equal("#35D6FF", editor.Settings.General.AccentColor);
        Assert.Equal(SettingsChange.Accent, _changes.Single());
        Assert.Equal(0x35D6FFu, editor.AccentRgb);
        Assert.Equal("Cyan", editor.AccentName);
    }

    [Theory]
    [InlineData("Hindsound_{app}_{date}_{time}", null)]
    [InlineData("{DATE} clip", null)]
    [InlineData("clip", null)]
    [InlineData("", SettingsEditor.TemplateEmptyError)]
    [InlineData("   ", SettingsEditor.TemplateEmptyError)]
    [InlineData("a/b", SettingsEditor.TemplateCharsError)]
    [InlineData("a:b", SettingsEditor.TemplateCharsError)]
    [InlineData("{nope}", "Unknown tag {nope}. Use {app}, {date} or {time}")]
    [InlineData("x{app", "Unknown tag {app. Use {app}, {date} or {time}")]
    public void The_name_template_gets_its_verdict(string template, string? message)
    {
        Assert.Equal(message, SettingsEditor.CheckTemplate(template).Message);
    }

    [Fact]
    public void An_invalid_template_is_not_kept()
    {
        var editor = Editor();

        editor.SetTemplate("a/b");

        Assert.Equal(ExportSettings.DefaultTemplate, editor.Settings.Export.FileNameTemplate);
        Assert.Empty(_changes);
    }

    [Fact]
    public void The_preview_shows_the_file_a_clip_would_get()
    {
        var editor = Editor();
        var now = new DateTime(2026, 11, 6, 14, 32, 5);

        Assert.Equal("Hindsound_Discord_2026-11-06_14-32-05.mp3", editor.NamePreview(ExportSettings.DefaultTemplate, now));

        editor.SetFormat("wav");

        Assert.Equal("clip 2026-11-06.wav", editor.NamePreview("clip {date}", now));
    }

    [Theory]
    [InlineData(@"D:\Clips", true)]
    [InlineData(@"%USERPROFILE%\Clips", true)]
    [InlineData("", false)]
    [InlineData("Clips", false)]
    public void Only_a_full_folder_path_is_kept(string path, bool kept)
    {
        var editor = Editor();

        var check = editor.SetFolder(path);

        Assert.Equal(kept, check.Ok);
        Assert.Equal(kept ? path : ExportSettings.DefaultFolder, editor.Settings.Export.OutputFolder);
    }

    [Fact]
    public void Setting_a_value_to_what_it_already_is_writes_nothing()
    {
        var editor = Editor();

        editor.SetClipSeconds(30);
        editor.SetBufferSeconds(300);
        editor.SetMic(AudioSettings.DefaultDevice);
        editor.SetFormat("mp3");
        editor.SetAccentPreset(AccentPreset.Lime);
        editor.SetStartWithWindows(false);

        Assert.Empty(_changes);
    }

    [Fact]
    public void Each_change_says_what_it_touches()
    {
        var editor = Editor();

        editor.SetMic(AudioSettings.DisabledMic);
        editor.SetOutput("out-1");
        editor.SetSourceApp("Discord.exe");
        editor.SetSystemGain(3);
        editor.SetFormat("wav");
        editor.SetStartWithWindows(true);
        editor.SetFullscreenGames("soundOnly");

        Assert.Equal(
            [SettingsChange.Devices, SettingsChange.Devices, SettingsChange.SourceApp, SettingsChange.Export, SettingsChange.Export, SettingsChange.Startup, SettingsChange.General],
            _changes);
    }

    [Fact]
    public void A_gain_stays_in_its_range()
    {
        var editor = Editor();

        editor.SetMicGain(99);
        editor.SetSystemGain(-99);

        Assert.Equal(AudioSettings.MaxGainDb, editor.Settings.Audio.MicGainDb);
        Assert.Equal(AudioSettings.MinGainDb, editor.Settings.Audio.SystemGainDb);
    }

    [Fact]
    public void The_lists_offer_default_devices_and_off()
    {
        var devices = new[] { new AudioDeviceChoice("a", "Speakers") };

        var outputs = SettingsEditor.OutputChoices(devices, "default");
        var mics = SettingsEditor.MicChoices(devices, "disabled");

        Assert.Equal(["default", "a"], outputs.Select(i => i.Value));
        Assert.Equal(["default", "a", "disabled"], mics.Select(i => i.Value));
        Assert.Equal("Off", mics[^1].Label);
    }

    [Fact]
    public void A_device_that_went_away_stays_listed()
    {
        var outputs = SettingsEditor.OutputChoices([], "gone");
        var mics = SettingsEditor.MicChoices([], "gone");

        Assert.Equal("gone", outputs[^1].Value);
        Assert.Contains(mics, i => i.Value == "gone");
    }

    [Fact]
    public void The_record_list_starts_on_the_whole_pc_and_keeps_an_app_not_running()
    {
        var apps = new[] { new AudioAppInfo("Discord.exe", "Discord") };

        var items = SettingsEditor.RecordChoices(apps, "Game.exe");

        Assert.Equal([string.Empty, "Discord.exe", "Game.exe"], items.Select(i => i.Value));
        Assert.Equal("Whole PC", items[0].Label);
        Assert.Equal("Game.exe (not running)", items[2].Label);
    }

    [Fact]
    public void Until_the_engine_lists_them_no_app_is_offered()
    {
        Assert.Empty(new EmptyAudioAppList().List());
    }

    [Fact]
    public void Source_app_goes_through_the_file_and_null_means_the_whole_pc()
    {
        var folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            var settings = new AppSettings();
            settings.Audio.SourceApp = "  Discord.exe ";
            store.Save(settings);

            Assert.Contains("\"sourceApp\": \"Discord.exe\"", File.ReadAllText(path));
            Assert.Equal("Discord.exe", store.Load().Audio.SourceApp);

            settings.Audio.SourceApp = "   ";
            store.Save(settings);

            Assert.Contains("\"sourceApp\": null", File.ReadAllText(path));
            Assert.Null(store.Load().Audio.SourceApp);

            File.WriteAllText(path, "{ \"audio\": { \"micDeviceId\": \"disabled\" } }");
            Assert.Null(store.Load().Audio.SourceApp);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
