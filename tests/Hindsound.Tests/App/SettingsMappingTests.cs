using Hindsound.App;
using Hindsound.Export;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

public class SettingsMappingTests
{
    [Theory]
    [InlineData("default", null, "default", null, true)]
    [InlineData("DEFAULT", null, "Default", null, true)]
    [InlineData(" default ", null, " default ", null, true)]
    [InlineData("", null, "", null, true)]
    [InlineData("{out-id}", "{out-id}", "{mic-id}", "{mic-id}", true)]
    [InlineData("default", null, "disabled", null, false)]
    [InlineData("default", null, "DISABLED", null, false)]
    [InlineData("default", null, " Disabled ", null, false)]
    [InlineData("{out-id}", "{out-id}", "disabled", null, false)]
    public void Devices_are_translated(string output, string? expectedOutput, string mic, string? expectedMic, bool expectedEnabled)
    {
        var settings = new AppSettings { Audio = new AudioSettings { OutputDeviceId = output, MicDeviceId = mic } };

        var options = SettingsMapping.ToEngineOptions(settings);

        Assert.Equal(expectedOutput, options.SystemDeviceId);
        Assert.Equal(expectedMic, options.MicDeviceId);
        Assert.Equal(expectedEnabled, options.MicEnabled);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Discord.exe", "Discord.exe")]
    [InlineData(" Discord.exe ", "Discord.exe")]
    public void The_recorded_app_reaches_the_engine(string? stored, string? expected)
    {
        var settings = new AppSettings { Audio = new AudioSettings { SourceApp = stored } };

        Assert.Equal(expected, SettingsMapping.ToEngineOptions(settings).SourceApp);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(300, 300)]
    [InlineData(0, 1)]
    public void Buffer_duration_comes_from_the_settings(int seconds, int expected)
    {
        var settings = new AppSettings { BufferSeconds = seconds };
        Assert.Equal(TimeSpan.FromSeconds(expected), SettingsMapping.ToEngineOptions(settings).BufferDuration);
    }

    [Theory]
    [InlineData("mp3", ExportFormat.Mp3)]
    [InlineData("MP3", ExportFormat.Mp3)]
    [InlineData("wav", ExportFormat.Wav)]
    [InlineData("Wav", ExportFormat.Wav)]
    [InlineData(" wav ", ExportFormat.Wav)]
    [InlineData("flac", ExportFormat.Mp3)]
    [InlineData("0", ExportFormat.Mp3)]
    [InlineData("7", ExportFormat.Mp3)]
    public void Format_ignores_case(string stored, ExportFormat expected)
    {
        var settings = new AppSettings { Export = new ExportSettings { Format = stored } };
        Assert.Equal(expected, SettingsMapping.ToExportOptions(settings).Format);
    }

    [Theory]
    [InlineData("off", NormalizationMode.Off)]
    [InlineData("OFF", NormalizationMode.Off)]
    [InlineData("peak", NormalizationMode.Peak)]
    [InlineData("Peak", NormalizationMode.Peak)]
    [InlineData("loudness", NormalizationMode.Loudness)]
    [InlineData("LOUDNESS", NormalizationMode.Loudness)]
    [InlineData("nonsense", NormalizationMode.Loudness)]
    public void Normalization_ignores_case(string stored, NormalizationMode expected)
    {
        var settings = new AppSettings { Export = new ExportSettings { Normalization = stored } };
        Assert.Equal(expected, SettingsMapping.ToExportOptions(settings).Normalization);
    }

    [Theory]
    [InlineData("mix", TrackMode.Mix)]
    [InlineData("MIX", TrackMode.Mix)]
    [InlineData("multitrack", TrackMode.MultiTrack)]
    [InlineData("MultiTrack", TrackMode.MultiTrack)]
    [InlineData("stems", TrackMode.Mix)]
    public void Track_mode_ignores_case(string stored, TrackMode expected)
    {
        var settings = new AppSettings { Audio = new AudioSettings { Mode = stored } };
        Assert.Equal(expected, SettingsMapping.ToExportOptions(settings).TrackMode);
    }

    [Theory]
    [InlineData(128, 128)]
    [InlineData(192, 192)]
    [InlineData(320, 320)]
    [InlineData(64, 192)]
    public void An_unusable_bitrate_never_reaches_the_saver(int stored, int expected)
    {
        var settings = new AppSettings { Export = new ExportSettings { Mp3Bitrate = stored } };
        Assert.Equal(expected, SettingsMapping.ToExportOptions(settings).Mp3BitrateKbps);
    }

    [Theory]
    [InlineData(@"%USERPROFILE%\Music\Hindsound")]
    [InlineData(@"%userprofile%\music\hindsound")]
    [InlineData(@"%USERPROFILE%\Music\Hindsound\")]
    [InlineData("%USERPROFILE%/Music/Hindsound")]
    [InlineData(" %USERPROFILE%\\Music\\Hindsound ")]
    [InlineData("")]
    [InlineData("   ")]
    public void The_default_folder_is_the_real_music_folder(string stored)
    {
        // DoNotVerify, as the app asks: an account with no Music folder still has its path, never an empty string.
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic, Environment.SpecialFolderOption.DoNotVerify), "Hindsound");
        var settings = new AppSettings { Export = new ExportSettings { OutputFolder = stored } };

        Assert.Equal(real, SettingsMapping.ToExportOptions(settings).OutputFolder);
    }

    [Fact]
    public void Another_folder_is_kept_and_its_variables_expanded()
    {
        Environment.SetEnvironmentVariable("HINDSOUND_TEST_DIR", @"D:\Audio");
        var settings = new AppSettings { Export = new ExportSettings { OutputFolder = @"%HINDSOUND_TEST_DIR%\Clips" } };

        Assert.Equal(@"D:\Audio\Clips", SettingsMapping.ToExportOptions(settings).OutputFolder);
    }

    [Fact]
    public void Every_other_value_is_carried_over()
    {
        var settings = new AppSettings
        {
            Audio = new AudioSettings { SystemGainDb = -3, MicGainDb = 6 },
            Export = new ExportSettings { TargetLufs = -20, PeakCeilingDb = -2, FileNameTemplate = "{date}_clip" },
        };

        var options = SettingsMapping.ToExportOptions(settings);

        Assert.Equal(-3, options.SystemGainDb);
        Assert.Equal(6, options.MicGainDb);
        Assert.Equal(-20, options.TargetLufs);
        Assert.Equal(-2, options.PeakCeilingDb);
        Assert.Equal("{date}_clip", options.FileNameTemplate);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_gain_that_is_not_a_number_never_reaches_the_saver(double gain)
    {
        var settings = new AppSettings { Audio = new AudioSettings { SystemGainDb = gain, MicGainDb = gain } };

        var options = SettingsMapping.ToExportOptions(settings);

        Assert.Equal(0, options.SystemGainDb);
        Assert.Equal(0, options.MicGainDb);
    }
}
