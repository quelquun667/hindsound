using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Input;
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

    [Fact]
    public void A_failure_for_the_self_test_is_read()
    {
        Assert.Equal(AppOptions.FailRun, AppOptions.Parse(["--selftest-fail", "run"]).SelfTestFail);
        Assert.Null(AppOptions.Parse([]).SelfTestFail);
    }

    [Fact]
    public void A_pretend_game_for_the_self_test_is_read()
    {
        Assert.Equal(12.5, AppOptions.Parse(["--selftest-game", "12.5"]).SelfTestGameSeconds);
        Assert.Null(AppOptions.Parse(["--selftest-game", "soon"]).SelfTestGameSeconds);
        Assert.Null(AppOptions.Parse([]).SelfTestGameSeconds);
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

    [Theory]
    [InlineData(CaptureActivity.Capturing, false)]
    [InlineData(CaptureActivity.Paused, true)]
    [InlineData(CaptureActivity.NoDevice, true)]
    [InlineData(CaptureActivity.Stopped, true)]
    public void Capture_on_stops_being_true_when_the_capture_stops(CaptureActivity activity, bool ceased)
    {
        Assert.Equal(ceased, StatusTransitions.HasCeased(NotificationKind.CaptureOn, Status(activity)));
    }

    [Theory]
    [InlineData(CaptureActivity.NoDevice, false)]
    [InlineData(CaptureActivity.Capturing, true)]
    [InlineData(CaptureActivity.Paused, true)]
    public void No_device_stops_being_true_when_the_activity_changes(CaptureActivity activity, bool ceased)
    {
        Assert.Equal(ceased, StatusTransitions.HasCeased(NotificationKind.NoDevice, Status(activity)));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void Mic_lost_stops_being_true_when_the_mic_is_back_or_not_wanted(bool enabled, bool capturing, bool ceased)
    {
        var status = Status(CaptureActivity.Capturing) with { MicEnabled = enabled, MicCapturing = capturing };
        Assert.Equal(ceased, StatusTransitions.HasCeased(NotificationKind.MicLost, status));
    }

    private static readonly HotkeyResult[] AllGood = [new(HotkeyAction.SaveDefault, "Ctrl+F10", HotkeyStatus.Registered, null)];
    private static readonly HotkeyResult[] SaveTaken = [new(HotkeyAction.SaveDefault, "Ctrl+F10", HotkeyStatus.InUse, HotkeyResult.InUseMessage)];
    private static readonly HotkeyResult[] OtherTaken =
    [
        new(HotkeyAction.SaveDefault, "Ctrl+F10", HotkeyStatus.Registered, null),
        new(HotkeyAction.Save1m, "Ctrl+F11", HotkeyStatus.InUse, HotkeyResult.InUseMessage),
    ];

    private static AudioEngineStatus Listening(CaptureActivity activity = CaptureActivity.Capturing, bool micEnabled = true, bool micCapturing = true) =>
        Status(activity) with { MicEnabled = micEnabled, MicCapturing = micCapturing };

    [Theory]
    [InlineData(CaptureActivity.Capturing, TrayState.Active)]
    [InlineData(CaptureActivity.Paused, TrayState.Paused)]
    [InlineData(CaptureActivity.Stopped, TrayState.Active)]
    public void With_no_problem_the_icon_follows_the_activity(CaptureActivity activity, TrayState expected)
    {
        Assert.Equal((expected, "Hindsound"), StatusTransitions.TrayOf(Listening(activity), AllGood, false));
    }

    [Fact]
    public void No_status_yet_is_no_problem()
    {
        Assert.Equal((TrayState.Active, "Hindsound"), StatusTransitions.TrayOf(null, AllGood, false));
    }

    [Fact]
    public void No_device_gives_the_triangle_and_names_itself()
    {
        Assert.Equal((TrayState.Error, "Hindsound - No audio device"), StatusTransitions.TrayOf(Listening(CaptureActivity.NoDevice), AllGood, false));
    }

    [Fact]
    public void An_engine_that_could_not_start_counts_as_no_device()
    {
        Assert.Equal((TrayState.Error, "Hindsound - No audio device"), StatusTransitions.TrayOf(null, AllGood, false, engineFailed: true));
    }

    [Fact]
    public void A_refused_save_hotkey_gives_the_triangle_and_says_which()
    {
        Assert.Equal((TrayState.Error, "Hindsound - Ctrl+F10: " + HotkeyResult.InUseMessage), StatusTransitions.TrayOf(Listening(), SaveTaken, false));
    }

    [Fact]
    public void A_refused_save_hotkey_also_shows_over_a_pause()
    {
        Assert.Equal(TrayState.Error, StatusTransitions.TrayOf(Listening(CaptureActivity.Paused), SaveTaken, false).State);
    }

    [Fact]
    public void A_clip_with_a_problem_waiting_gives_the_triangle()
    {
        Assert.Equal((TrayState.Error, "Hindsound - A clip has a problem"), StatusTransitions.TrayOf(Listening(), AllGood, true));
    }

    [Fact]
    public void A_mic_not_captured_only_goes_in_the_tip()
    {
        Assert.Equal((TrayState.Active, "Hindsound - Microphone not captured"), StatusTransitions.TrayOf(Listening(micCapturing: false), AllGood, false));
    }

    [Theory]
    [InlineData(CaptureActivity.Paused, true)] // nothing is captured during a pause
    [InlineData(CaptureActivity.Capturing, false)] // the mic is off on purpose
    public void A_mic_that_is_not_expected_is_no_problem(CaptureActivity activity, bool micEnabled)
    {
        Assert.Equal("Hindsound", StatusTransitions.TrayOf(Listening(activity, micEnabled, micCapturing: false), AllGood, false).Tip);
    }

    [Fact]
    public void Another_refused_hotkey_only_goes_in_the_tip()
    {
        Assert.Equal((TrayState.Active, "Hindsound - Ctrl+F11: " + HotkeyResult.InUseMessage), StatusTransitions.TrayOf(Listening(), OtherTaken, false));
    }

    [Fact]
    public void A_hotkey_being_typed_or_not_set_is_no_problem()
    {
        HotkeyResult[] quiet = [new(HotkeyAction.SaveDefault, null, HotkeyStatus.NotSet, null), new(HotkeyAction.Save1m, "Ctrl+F11", HotkeyStatus.Suspended, null)];
        Assert.Equal((TrayState.Active, "Hindsound"), StatusTransitions.TrayOf(Listening(), quiet, false));
    }

    [Fact]
    public void The_tip_names_the_first_problem_in_order()
    {
        var noMic = Listening(micCapturing: false);

        Assert.Equal("Hindsound - No audio device", StatusTransitions.TrayOf(Listening(CaptureActivity.NoDevice, micCapturing: false), SaveTaken, true).Tip);
        Assert.Equal("Hindsound - Ctrl+F10: " + HotkeyResult.InUseMessage, StatusTransitions.TrayOf(noMic, SaveTaken, true).Tip);
        Assert.Equal("Hindsound - A clip has a problem", StatusTransitions.TrayOf(noMic, AllGood, true).Tip);
        Assert.Equal("Hindsound - Microphone not captured", StatusTransitions.TrayOf(noMic, OtherTaken, false).Tip);
    }

    [Fact]
    public void Everything_goes_back_to_normal_when_the_condition_ends()
    {
        Assert.Equal(TrayState.Error, StatusTransitions.TrayOf(Listening(CaptureActivity.NoDevice), SaveTaken, true).State);
        Assert.Equal((TrayState.Active, "Hindsound"), StatusTransitions.TrayOf(Listening(), AllGood, false));
    }

    [Theory]
    [InlineData(HotkeyStatus.Registered, null)]
    [InlineData(HotkeyStatus.NotSet, null)]
    [InlineData(HotkeyStatus.Suspended, null)]
    [InlineData(HotkeyStatus.InUse, "Save hotkey is taken")]
    [InlineData(HotkeyStatus.Duplicate, "Save hotkey is taken")]
    [InlineData(HotkeyStatus.Invalid, "Save hotkey doesn't work")]
    [InlineData(HotkeyStatus.Unavailable, "Save hotkey doesn't work")]
    public void The_reminder_says_what_is_wrong_with_the_save_hotkey(HotkeyStatus status, string? problem)
    {
        Assert.Equal(problem, StatusTransitions.SaveHotkeyProblem([new HotkeyResult(HotkeyAction.SaveDefault, "Ctrl+F10", status, null)]));
    }

    [Fact]
    public void Another_refused_hotkey_changes_nothing_in_the_reminder()
    {
        Assert.Null(StatusTransitions.SaveHotkeyProblem(OtherTaken));
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

    private static AudioEngineStatus Recording(string app, bool running = true) =>
        Status(CaptureActivity.Capturing, running ? app : null) with { SourceApp = app, SourceAppMissing = !running };

    [Fact]
    public void An_app_that_closes_or_comes_back_is_no_device_change()
    {
        Assert.Equal(StatusNotice.None, StatusTransitions.Decide(Recording("Discord.exe"), Recording("Discord.exe", running: false)));
        Assert.Equal(StatusNotice.None, StatusTransitions.Decide(Recording("Discord.exe", running: false), Recording("Discord.exe")));
    }

    [Fact]
    public void Choosing_what_to_record_is_no_device_change()
    {
        var wholePc = Status(CaptureActivity.Capturing, "Speakers");

        Assert.Equal(StatusNotice.None, StatusTransitions.Decide(wholePc, Recording("Discord.exe")));
        Assert.Equal(StatusNotice.None, StatusTransitions.Decide(Recording("Discord.exe"), Recording("Game.exe")));
        Assert.Equal(StatusNotice.None, StatusTransitions.Decide(Recording("Discord.exe"), wholePc));
    }

    [Fact]
    public void An_app_capture_that_recovers_says_the_capture_is_on()
    {
        var failed = Status(CaptureActivity.NoDevice, null) with { SourceApp = "Discord.exe" };

        Assert.Equal(StatusNotice.CaptureOn, StatusTransitions.Decide(failed, Recording("Discord.exe")));
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
