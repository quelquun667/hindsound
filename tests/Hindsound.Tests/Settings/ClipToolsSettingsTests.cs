using Hindsound.Settings;

namespace Hindsound.Tests.Settings;

/// <summary>The switch of the voice and the size of the window, as settings.json keeps them.</summary>
public sealed class ClipToolsSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "settings.json");

    public ClipToolsSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_voice_is_recorded_and_the_window_is_at_120_by_default()
    {
        var settings = new AppSettings().Normalize();

        Assert.True(settings.Audio.RecordMic);
        Assert.True(settings.Audio.MicCaptured);
        Assert.Equal(120, settings.General.WindowZoom);
    }

    [Fact]
    public void A_file_written_before_these_keys_gets_the_defaults()
    {
        File.WriteAllText(FilePath, """{"audio":{"micDeviceId":"disabled"},"general":{"notifications":false}}""");

        var settings = new SettingsStore(FilePath).Load();

        Assert.True(settings.Audio.RecordMic);
        Assert.Equal(120, settings.General.WindowZoom);
        Assert.False(settings.General.Notifications);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(140)]
    public void Both_values_come_back_from_the_file(int zoom)
    {
        var store = new SettingsStore(FilePath);
        var settings = new AppSettings();
        settings.Audio.RecordMic = false;
        settings.General.WindowZoom = zoom;

        store.Save(settings);
        var text = File.ReadAllText(FilePath);
        var back = store.Load();

        Assert.Contains("\"recordMic\": false", text, StringComparison.Ordinal);
        Assert.Contains($"\"windowZoom\": {zoom}", text, StringComparison.Ordinal);
        Assert.False(back.Audio.RecordMic);
        Assert.Equal(zoom, back.General.WindowZoom);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-120)]
    [InlineData(110)]
    [InlineData(150)]
    [InlineData(1200)]
    public void A_size_that_is_not_offered_falls_back_to_120(int zoom)
    {
        File.WriteAllText(FilePath, $$$"""{"general":{"windowZoom":{{{zoom}}}}}""");

        Assert.Equal(120, new SettingsStore(FilePath).Load().General.WindowZoom);
    }

    [Fact]
    public void Turning_the_voice_off_keeps_the_chosen_microphone()
    {
        var settings = new AppSettings();
        settings.Audio.MicDeviceId = "{0.0.1.00000000}.{mic}";
        settings.Audio.RecordMic = false;
        settings.Normalize();

        Assert.Equal("{0.0.1.00000000}.{mic}", settings.Audio.MicDeviceId);
        Assert.True(settings.Audio.MicEnabled);
        Assert.False(settings.Audio.MicCaptured);
    }

    [Fact]
    public void A_disabled_microphone_is_never_captured_whatever_the_switch()
    {
        var settings = new AppSettings();
        settings.Audio.MicDeviceId = AudioSettings.DisabledMic;

        Assert.True(settings.Audio.RecordMic);
        Assert.False(settings.Audio.MicCaptured);
    }
}
