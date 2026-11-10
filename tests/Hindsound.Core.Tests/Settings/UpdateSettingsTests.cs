using System.Text.Json;
using Hindsound.App;
using Hindsound.Settings;
using Hindsound.Ui.Window;
using Hindsound.Updates;

namespace Hindsound.Tests.Settings;

/// <summary>general.updates in the settings file: a file that never chose updates by itself, and anything unreadable means no network.</summary>
public sealed class UpdateSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));

    public UpdateSettingsTests() => Directory.CreateDirectory(_folder);

    private string FilePath => Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private SettingsRead ReadJson(string json)
    {
        File.WriteAllText(FilePath, json);
        return new SettingsStore(FilePath).Read();
    }

    private UpdateSettings UpdatesOf(string json)
    {
        var read = ReadJson(json);
        Assert.Equal(SettingsLoadStatus.Loaded, read.Status);
        return read.Settings!.General.Updates;
    }

    [Fact]
    public void The_default_installs_automatically_and_skips_nothing()
    {
        var updates = new AppSettings().General.Updates;

        Assert.Equal("auto", updates.Mode);
        Assert.Null(updates.SkippedVersion);
    }

    [Fact]
    public void A_file_without_the_key_installs_automatically()
    {
        Assert.Equal("auto", UpdatesOf("""{ "general": { "notifications": true } }""").Mode);
        Assert.Equal("auto", UpdatesOf("{}").Mode);
    }

    [Fact]
    public void A_null_updates_gives_the_defaults_back()
    {
        var updates = UpdatesOf("""{ "general": { "updates": null } }""");

        Assert.Equal("auto", updates.Mode);
        Assert.Null(updates.SkippedVersion);
    }

    [Theory]
    [InlineData("ASK", "ask")]
    [InlineData("Off", "off")]
    [InlineData("Auto", "auto")]
    public void The_mode_is_read_whatever_its_case(string written, string expected)
    {
        Assert.Equal(expected, UpdatesOf($$"""{ "general": { "updates": { "mode": "{{written}}" } } }""").Mode);
    }

    [Theory]
    [InlineData("never")]
    [InlineData("")]
    [InlineData(" auto")]
    [InlineData("automatic")]
    public void Unknown_mode_means_off(string written)
    {
        Assert.Equal("off", UpdatesOf($$"""{ "general": { "updates": { "mode": "{{written}}" } } }""").Mode);
    }

    [Fact]
    public void A_null_mode_means_off()
    {
        Assert.Equal("off", UpdatesOf("""{ "general": { "updates": { "mode": null } } }""").Mode);
    }

    [Fact]
    public void An_unknown_mode_set_in_code_means_off_once_normalized()
    {
        var settings = new AppSettings();
        settings.General.Updates.Mode = "sometimes";

        settings.Normalize();

        Assert.Equal("off", settings.General.Updates.Mode);
    }

    [Theory]
    [InlineData("v0.2.0")]
    [InlineData("0.2")]
    [InlineData("0.2.0-beta")]
    [InlineData("")]
    [InlineData("latest")]
    public void An_invalid_skipped_version_is_dropped(string written)
    {
        var updates = UpdatesOf($$"""{ "general": { "updates": { "mode": "ask", "skippedVersion": "{{written}}" } } }""");

        Assert.Null(updates.SkippedVersion);
        Assert.Equal("ask", updates.Mode);
    }

    [Fact]
    public void A_valid_skipped_version_is_kept()
    {
        Assert.Equal("0.2.0", UpdatesOf("""{ "general": { "updates": { "skippedVersion": "0.2.0" } } }""").SkippedVersion);
    }

    [Fact]
    public void The_file_holds_general_updates_mode_and_skipped_version_and_reads_back()
    {
        var store = new SettingsStore(FilePath);
        var settings = new AppSettings();
        settings.General.Updates.Mode = UpdateSettings.Ask;
        settings.General.Updates.SkippedVersion = "0.3.1";

        store.Save(settings);

        using (var json = JsonDocument.Parse(File.ReadAllText(FilePath)))
        {
            var updates = json.RootElement.GetProperty("general").GetProperty("updates");
            Assert.Equal("ask", updates.GetProperty("mode").GetString());
            Assert.Equal("0.3.1", updates.GetProperty("skippedVersion").GetString());
        }

        var back = store.Load().General.Updates;
        Assert.Equal("ask", back.Mode);
        Assert.Equal("0.3.1", back.SkippedVersion);
    }

    [Fact]
    public void A_key_at_the_root_is_ignored()
    {
        var read = ReadJson("""{ "updates": { "mode": "off" }, "mode": "off" }""");

        Assert.Equal(SettingsLoadStatus.Loaded, read.Status);
        Assert.Equal("auto", read.Settings!.General.Updates.Mode);
    }

    [Theory]
    [InlineData("""{ "general": { "updates": "auto" } }""")]
    [InlineData("""{ "general": { "updates": { "mode": 3 } } }""")]
    [InlineData("""{ "general": { "updates": { "skippedVersion": 2 } } }""")]
    public void A_value_of_the_wrong_type_is_a_damaged_file(string json)
    {
        Assert.Equal(SettingsLoadStatus.Corrupt, ReadJson(json).Status);
    }

    [Theory]
    [InlineData("auto", "Auto")]
    [InlineData("ask", "Ask")]
    [InlineData("off", "Off")]
    [InlineData("never", "Off")]
    [InlineData("AUTO", "Off")]
    public void The_mode_given_to_the_updater_is_off_unless_it_is_known(string mode, string expected)
    {
        // Not normalized on purpose: a value that slipped past Normalize still never reaches the network.
        Assert.Equal(Enum.Parse<UpdateMode>(expected), SettingsMapping.ToUpdateMode(new GeneralSettings { Updates = new UpdateSettings { Mode = mode } }));
    }

    [Fact]
    public void The_updater_gets_off_when_updates_are_missing_from_the_object()
    {
        Assert.Equal(UpdateMode.Off, SettingsMapping.ToUpdateMode(new GeneralSettings { Updates = null! }));
        Assert.Null(SettingsMapping.ToSkipped(new GeneralSettings { Updates = null! }));
    }

    [Fact]
    public void The_skipped_version_is_given_as_a_version()
    {
        var general = new GeneralSettings { Updates = new UpdateSettings { SkippedVersion = "1.2.3" } };

        Assert.Equal(new UpdateVersion(1, 2, 3), SettingsMapping.ToSkipped(general));
        Assert.Null(SettingsMapping.ToSkipped(new GeneralSettings()));
        Assert.Null(SettingsMapping.ToSkipped(new GeneralSettings { Updates = new UpdateSettings { SkippedVersion = "1.2" } }));
    }

    [Fact]
    public void A_new_mode_tells_general_and_updates_and_the_same_one_tells_nothing()
    {
        var changes = new List<SettingsChange>();
        var editor = new SettingsEditor(new AppSettings(), changes.Add);

        editor.SetUpdateMode(UpdateSettings.Ask);
        editor.SetUpdateMode(UpdateSettings.Ask);

        Assert.Equal([SettingsChange.General | SettingsChange.Updates], changes);
        Assert.Equal("ask", editor.Settings.General.Updates.Mode);
    }
}
