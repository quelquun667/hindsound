using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class HotkeyCaptureTests
{
    private readonly HotkeyCapture _capture = new();

    public HotkeyCaptureTests() => _capture.Begin(HotkeyAction.Save15s);

    [Fact]
    public void A_key_with_a_modifier_is_a_gesture()
    {
        var step = _capture.Key('H', HotkeyGesture.ModControl | HotkeyGesture.ModAlt);

        Assert.Equal(CaptureStepKind.Captured, step.Kind);
        Assert.Equal("Ctrl+Alt+H", step.Gesture);
    }

    [Fact]
    public void A_function_key_alone_is_a_gesture()
    {
        Assert.Equal("F9", _capture.Key(0x78, 0).Gesture);
    }

    [Fact]
    public void A_letter_alone_stays_listening_and_says_why()
    {
        var step = _capture.Key('H', 0);

        Assert.Equal(CaptureStepKind.Listening, step.Kind);
        Assert.Equal(HotkeyResult.InvalidMessage, _capture.Message);
        Assert.True(_capture.Listening);
    }

    [Fact]
    public void Modifiers_alone_only_show_up()
    {
        var step = _capture.Key(Vk.Ctrl, HotkeyGesture.ModControl);

        Assert.Equal(CaptureStepKind.Listening, step.Kind);
        Assert.Null(_capture.Message);
        Assert.Equal("Ctrl", _capture.HeldText());

        _capture.Key(Vk.Shift, HotkeyGesture.ModControl | HotkeyGesture.ModShift);
        Assert.Equal("Ctrl+Shift", _capture.HeldText());

        _capture.KeyUp(HotkeyGesture.ModShift);
        Assert.Equal("Shift", _capture.HeldText());
    }

    [Fact]
    public void Escape_cancels_and_backspace_clears()
    {
        Assert.Equal(CaptureStepKind.Cancelled, _capture.Key(Vk.Escape, 0).Kind);
        Assert.Equal(CaptureStepKind.Cleared, _capture.Key(Vk.Back, 0).Kind);
    }

    [Fact]
    public void Tab_alone_leaves_for_the_next_control()
    {
        Assert.Equal(CaptureStepKind.CancelledAndMoveOn, _capture.Key(Vk.Tab, 0).Kind);
    }

    [Fact]
    public void Ctrl_tab_is_a_gesture_and_not_a_way_out()
    {
        Assert.Equal("Ctrl+Tab", _capture.Key(Vk.Tab, HotkeyGesture.ModControl).Gesture);
    }

    [Fact]
    public void The_main_hotkey_cannot_be_cleared()
    {
        _capture.Begin(HotkeyAction.SaveDefault);

        var step = _capture.Key(Vk.Back, 0);

        Assert.Equal(CaptureStepKind.Listening, step.Kind);
        Assert.Equal(HotkeyCapture.CannotClearMain, _capture.Message);
    }
}

public class StatusTextTests
{
    [Fact]
    public void Before_any_status_it_is_starting()
    {
        var band = StatusText.From(null);

        Assert.Equal("Starting", band.Title);
        Assert.False(band.PauseEnabled);
    }

    [Fact]
    public void Capturing_shows_the_mic_the_time_and_the_memory()
    {
        var band = StatusText.From(ScreenRig.Capturing());

        Assert.Equal(StatusMark.Dot, band.Mark);
        Assert.Equal("Capturing", band.Title);
        Assert.Equal("mic on", band.Mic);
        Assert.Equal("1:02", band.Duration);
        Assert.Equal("~87 MB", band.Ram);
        Assert.Equal("Pause", band.PauseLabel);
    }

    [Theory]
    [InlineData(true, false, "mic not captured")]
    [InlineData(false, false, "mic off")]
    [InlineData(true, true, "mic on")]
    public void The_mic_says_what_it_really_does(bool enabled, bool capturing, string text)
    {
        Assert.Equal(text, StatusText.From(ScreenRig.Capturing(capturing, micEnabled: enabled)).Mic);
    }

    [Fact]
    public void Paused_offers_resume_and_has_its_own_shape()
    {
        var band = StatusText.From(ScreenRig.Capturing(false, CaptureActivity.Paused));

        Assert.Equal(StatusMark.Pause, band.Mark);
        Assert.Equal("Paused", band.Title);
        Assert.Equal("Resume", band.PauseLabel);
    }

    [Fact]
    public void Capturing_one_app_names_it()
    {
        var band = StatusText.From(ScreenRig.Capturing() with { SourceApp = "Discord.exe" });

        Assert.Equal(StatusMark.Dot, band.Mark);
        Assert.Equal("Capturing Discord", band.Title);
    }

    [Fact]
    public void An_app_not_running_is_said_and_is_no_error()
    {
        var band = StatusText.From(ScreenRig.Capturing() with { SystemCapturing = false, SourceApp = "Discord.exe", SourceAppMissing = true });

        Assert.Equal(StatusMark.Caution, band.Mark);
        Assert.Equal("Not running: Discord", band.Title);
    }

    [Fact]
    public void An_app_that_cannot_be_captured_is_never_called_captured()
    {
        var withMic = StatusText.From(ScreenRig.Capturing() with { SystemCapturing = false, SourceApp = "Discord.exe" });
        Assert.Equal(StatusMark.Warning, withMic.Mark);
        Assert.Equal("Can't capture Discord", withMic.Title);

        var alone = StatusText.From(ScreenRig.Capturing(false, CaptureActivity.NoDevice) with { SystemCapturing = false, SourceApp = "Discord.exe" });
        Assert.Equal(StatusMark.Warning, alone.Mark);
        Assert.Equal("Can't capture Discord", alone.Title);
    }

    [Fact]
    public void No_device_is_a_warning_shape()
    {
        var band = StatusText.From(ScreenRig.Capturing(false, CaptureActivity.NoDevice));

        Assert.Equal(StatusMark.Warning, band.Mark);
        Assert.Equal("No audio device", band.Title);
    }

    [Theory]
    [InlineData(15, "Save up to 15s")]
    [InlineData(30, "Save up to 30s")]
    [InlineData(60, "Save up to 1 min")]
    [InlineData(300, "Save up to 5 min")]
    [InlineData(90, "Save up to 1:30")]
    public void The_big_button_says_how_long(int seconds, string label)
    {
        Assert.Equal(label, StatusText.SaveLabel(seconds));
    }

    [Theory]
    [InlineData(720_000, "720 KB")]
    [InlineData(400, "1 KB")]
    [InlineData(1_400_000, "1.4 MB")]
    public void A_size_reads_in_the_right_unit(long bytes, string text)
    {
        Assert.Equal(text, StatusText.FileSize(bytes));
    }

    [Theory]
    [InlineData(0, "0 dB")]
    [InlineData(3, "+3 dB")]
    [InlineData(-12, "-12 dB")]
    public void A_gain_shows_its_sign(double db, string text)
    {
        Assert.Equal(text, StatusText.GainLabel(db));
    }

    [Fact]
    public void The_last_clip_reads_as_in_the_mockup()
    {
        var clip = new LastClipInfo(TimeSpan.FromSeconds(29.98), new DateTime(2026, 11, 6, 14, 32, 0), 720_000);

        Assert.Equal("0:30", clip.DurationText);
        Assert.Equal("14:32", clip.TimeText);
        Assert.Equal("720 KB", clip.SizeText);
    }
}

public class MicMeterTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(0.001f, 0f)]
    [InlineData(float.NaN, 0f)]
    public void The_scale_runs_from_minus_60_db_to_full(float peak, float level)
    {
        Assert.Equal(level, MicMeter.ScaleOf(peak), 3);
    }

    [Fact]
    public void A_clipped_sample_stays_at_the_top()
    {
        Assert.Equal(1f, MicMeter.ScaleOf(4f));
    }

    [Fact]
    public void It_rises_at_once_and_falls_slowly()
    {
        var meter = new MicMeter();

        meter.Feed(1);
        Assert.Equal(1f, meter.Level);

        meter.Feed(0);
        Assert.InRange(meter.Level, 0.9f, 0.95f);

        for (var i = 0; i < 40; i++)
        {
            meter.Feed(0);
        }

        Assert.Equal(0f, meter.Level);
    }

    [Fact]
    public void The_peak_is_the_loudest_sample_either_way()
    {
        Assert.Equal(0.5f, MicMeter.Peak([0.1f, -0.5f, 0.2f]));
        Assert.Equal(0f, MicMeter.Peak([]));
    }
}

public class FocusRingTests
{
    [Fact]
    public void It_goes_round_in_both_directions()
    {
        Assert.Equal(1, FocusRing.Next(3, 0, true, _ => true));
        Assert.Equal(0, FocusRing.Next(3, 2, true, _ => true));
        Assert.Equal(2, FocusRing.Next(3, 0, false, _ => true));
    }

    [Fact]
    public void It_skips_what_cannot_take_the_focus()
    {
        Assert.Equal(3, FocusRing.Next(4, 0, true, i => i != 1 && i != 2));
    }

    [Fact]
    public void Without_a_current_entry_it_starts_at_an_end()
    {
        Assert.Equal(0, FocusRing.Next(3, -1, true, _ => true));
        Assert.Equal(2, FocusRing.Next(3, -1, false, _ => true));
    }

    [Fact]
    public void With_nothing_to_focus_it_says_so()
    {
        Assert.Equal(-1, FocusRing.Next(3, 0, true, _ => false));
        Assert.Equal(-1, FocusRing.Next(0, -1, true, _ => true));
    }
}
