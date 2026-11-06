using Hindsound.App;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>The whole way from the command line and the file to what the engine is told.</summary>
public sealed class NoMicTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-nomic-").FullName;

    private string Path => System.IO.Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_option_is_read_and_off_by_default()
    {
        Assert.True(AppOptions.Parse(["--no-mic"]).NoMic);
        Assert.False(AppOptions.Parse([]).NoMic);
    }

    [Fact]
    public void A_mic_in_the_settings_stays_off_at_the_start_and_at_every_read_and_the_file_stays_intact()
    {
        File.WriteAllText(Path, """{"audio":{"micDeviceId":"{real-mic}"}}""");
        var before = File.ReadAllBytes(Path);
        var options = AppOptions.Parse(["--settings", Path, "--no-mic"]);
        var store = new SettingsStore(options.SettingsPath);

        var settings = store.Load();
        var atStart = SettingsMapping.ToEngineOptions(settings, options);

        Assert.False(atStart.MicEnabled);
        Assert.Null(atStart.MicDeviceId);
        Assert.Equal("{real-mic}", settings.Audio.MicDeviceId);
        Assert.Equal(before, File.ReadAllBytes(Path));

        File.WriteAllText(Path, """{"audio":{"micDeviceId":"{other-mic}"}}""");
        before = File.ReadAllBytes(Path);
        var read = store.Read();
        var atRead = SettingsMapping.ToEngineOptions(read.Settings!, options);

        Assert.False(atRead.MicEnabled);
        Assert.Null(atRead.MicDeviceId);
        Assert.Equal("{other-mic}", read.Settings!.Audio.MicDeviceId);
        Assert.Equal(before, File.ReadAllBytes(Path));
    }

    [Fact]
    public void Without_the_option_the_settings_decide()
    {
        var settings = new AppSettings { Audio = new AudioSettings { MicDeviceId = "{real-mic}" } };

        var engine = SettingsMapping.ToEngineOptions(settings, new AppOptions());

        Assert.True(engine.MicEnabled);
        Assert.Equal("{real-mic}", engine.MicDeviceId);
    }
}
