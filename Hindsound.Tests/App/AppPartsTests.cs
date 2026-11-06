using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

public class AppOptionsTests
{
    [Fact]
    public void No_arguments_means_the_ordinary_app()
    {
        Assert.Equal(new AppOptions(), AppOptions.Parse([]));
    }

    [Fact]
    public void Every_option_is_read()
    {
        var options = AppOptions.Parse(["--settings", @"C:\t\s.json", "--exit-after", "2.5", "--selftest-save", "--offscreen", "--dump-notifications", @"C:\t\d"]);

        Assert.Equal(new AppOptions(@"C:\t\s.json", 2.5, true, true, @"C:\t\d"), options);
    }

    [Theory]
    [InlineData("--exit-after")]
    [InlineData("--settings")]
    [InlineData("--nonsense")]
    public void An_incomplete_or_unknown_option_is_ignored(string option)
    {
        Assert.Equal(new AppOptions(), AppOptions.Parse([option]));
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("-3")]
    public void A_bad_exit_delay_is_ignored(string value)
    {
        Assert.Null(AppOptions.Parse(["--exit-after", value]).ExitAfterSeconds);
    }

    [Fact]
    public void The_log_of_a_test_run_sits_next_to_its_settings()
    {
        Assert.Equal(@"C:\t\run.log", Journal.PathFor(@"C:\t\run.json"));
    }
}

public class StatusTransitionsTests
{
    private static AudioEngineStatus Status(CaptureActivity activity, string? device = "Speakers") =>
        new(activity, true, false, false, device, null, true, TimeSpan.Zero, 0);

    [Theory]
    [InlineData(null, CaptureActivity.Capturing, StatusNotice.CaptureOn)]
    [InlineData(null, CaptureActivity.NoDevice, StatusNotice.NoDevice)]
    [InlineData(CaptureActivity.Stopped, CaptureActivity.Capturing, StatusNotice.CaptureOn)]
    [InlineData(CaptureActivity.Capturing, CaptureActivity.Paused, StatusNotice.Paused)]
    [InlineData(CaptureActivity.Paused, CaptureActivity.Capturing, StatusNotice.CaptureOn)]
    [InlineData(CaptureActivity.Paused, CaptureActivity.Paused, StatusNotice.None)]
    [InlineData(CaptureActivity.Capturing, CaptureActivity.NoDevice, StatusNotice.NoDevice)]
    [InlineData(CaptureActivity.NoDevice, CaptureActivity.NoDevice, StatusNotice.None)]
    [InlineData(CaptureActivity.NoDevice, CaptureActivity.Capturing, StatusNotice.DeviceChanged)]
    [InlineData(CaptureActivity.Capturing, CaptureActivity.Capturing, StatusNotice.None)]
    [InlineData(CaptureActivity.Capturing, CaptureActivity.Stopped, StatusNotice.None)]
    public void Transitions(CaptureActivity? from, CaptureActivity to, StatusNotice expected)
    {
        var previous = from is { } activity ? Status(activity) : null;
        Assert.Equal(expected, StatusTransitions.Decide(previous, Status(to)));
    }

    [Fact]
    public void Another_output_is_announced()
    {
        Assert.Equal(StatusNotice.DeviceChanged, StatusTransitions.Decide(Status(CaptureActivity.Capturing, "Speakers"), Status(CaptureActivity.Capturing, "Headset")));
    }

    [Fact]
    public void Capturing_only_the_microphone_again_is_no_output_change()
    {
        Assert.Equal(StatusNotice.CaptureOn, StatusTransitions.Decide(Status(CaptureActivity.NoDevice, null), Status(CaptureActivity.Capturing, null)));
        Assert.Equal(StatusNotice.None, StatusTransitions.Decide(Status(CaptureActivity.Capturing, "Speakers"), Status(CaptureActivity.Capturing, null)));
    }

    [Theory]
    [InlineData(CaptureActivity.Capturing, TrayState.Active)]
    [InlineData(CaptureActivity.Stopped, TrayState.Active)]
    [InlineData(CaptureActivity.Paused, TrayState.Paused)]
    [InlineData(CaptureActivity.NoDevice, TrayState.Error)]
    public void The_tray_follows_the_engine(CaptureActivity activity, TrayState expected)
    {
        Assert.Equal(expected, StatusTransitions.TrayStateOf(activity));
    }
}

public class SettingsStoreTrimmedFormatTests
{
    // The context generated for the trimmed exe must write what the reflection-based serializer wrote.
    [Fact]
    public void The_file_keeps_its_names_and_its_readable_characters()
    {
        var path = Path.Combine(Path.GetTempPath(), "hindsound-format-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new SettingsStore(path).Save(new AppSettings { Export = new ExportSettings { FileNameTemplate = "a+b&c" } });
            var text = File.ReadAllText(path);

            foreach (var name in new[] { "\"hotkeys\"", "\"saveDefault\": \"Ctrl+Shift+F10\"", "\"clipSeconds\"", "\"micDeviceId\"", "\"mp3Bitrate\"", "\"fileNameTemplate\": \"a+b&c\"", "\"accentColor\"" })
            {
                Assert.Contains(name, text);
            }

            Assert.Contains(Environment.NewLine + "  ", text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
