using System.Text.Json;
using Hindsound.App;
using Hindsound.Settings;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>A damaged or unreadable settings file: the user may have turned updates off, and nothing says so any more. Like the microphone, they go off.</summary>
public sealed class UpdateSafeSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));

    public UpdateSafeSettingsTests() => Directory.CreateDirectory(_folder);

    private string FilePath => Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Make_safe_turns_updates_off_and_forgets_the_skipped_version()
    {
        var settings = new AppSettings();
        settings.General.Updates.SkippedVersion = "0.2.0";

        SettingsFileState.MakeSafe(settings, runKeyEnabled: null);

        Assert.Equal("off", settings.General.Updates.Mode);
        Assert.Null(settings.General.Updates.SkippedVersion);
        Assert.Equal(UpdateMode.Off, SettingsMapping.ToUpdateMode(settings.General));
        Assert.False(settings.Audio.MicEnabled);
    }

    [Fact]
    public void Make_safe_keeps_what_it_did_before()
    {
        var settings = new AppSettings();

        SettingsFileState.MakeSafe(settings, runKeyEnabled: true);

        Assert.Equal(AudioSettings.DisabledMic, settings.Audio.MicDeviceId);
        Assert.True(settings.General.StartWithWindows);
    }

    [Fact]
    public void A_damaged_file_starts_with_updates_off_and_writes_it_so()
    {
        File.WriteAllText(FilePath, """{ "general": { "updates": { "mode": "auto" } }, "clipSeconds": """);
        var store = new SettingsStore(FilePath);

        var (settings, start) = new SettingsFileState().Load(store, 1, () => { }, () => null);

        Assert.True(start.MakeSafe);
        Assert.True(start.Write);
        Assert.Equal("off", settings.General.Updates.Mode);
        Assert.False(settings.Audio.MicEnabled);

        // What the start writes back is the file the next start reads: it stays off until the user turns it on again.
        store.Save(settings);
        using var json = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.Equal("off", json.RootElement.GetProperty("general").GetProperty("updates").GetProperty("mode").GetString());
    }

    [Fact]
    public void A_locked_file_starts_with_updates_off()
    {
        File.WriteAllText(FilePath, """{ "general": { "updates": { "mode": "auto" } } }""");
        using var locked = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var (settings, start) = new SettingsFileState().Load(new SettingsStore(FilePath), 1, () => { }, () => null);

        Assert.True(start.MakeSafe);
        Assert.Equal("off", settings.General.Updates.Mode);
        Assert.False(settings.Audio.MicEnabled);
    }

    [Fact]
    public void A_file_read_well_keeps_its_own_mode()
    {
        File.WriteAllText(FilePath, """{ "general": { "updates": { "mode": "ask", "skippedVersion": "0.2.0" } } }""");

        var (settings, start) = new SettingsFileState().Load(new SettingsStore(FilePath), 1, () => { }, () => null);

        Assert.False(start.MakeSafe);
        Assert.Equal("ask", settings.General.Updates.Mode);
        Assert.Equal("0.2.0", settings.General.Updates.SkippedVersion);
    }

    [Fact]
    public void A_first_start_without_a_file_installs_automatically()
    {
        var (settings, start) = new SettingsFileState().Load(new SettingsStore(FilePath), 1, () => { }, () => false);

        Assert.True(start.FirstRun);
        Assert.Equal("auto", settings.General.Updates.Mode);
    }
}
