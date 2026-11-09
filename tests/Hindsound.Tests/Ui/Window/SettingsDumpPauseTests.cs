using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A picture of the window must show a state the engine can be in: a pause closes the captures, it does not keep them open.</summary>
public class SettingsDumpPauseTests
{
    [Fact]
    public void The_paused_picture_has_no_capture_open_like_the_engine_after_a_pause()
    {
        var open = ScreenRig.Capturing();

        var paused = SettingsDump.PausedStatus(open);

        Assert.Equal(CaptureActivity.Paused, paused.Activity);
        Assert.False(paused.SystemCapturing);
        Assert.False(paused.MicCapturing);
        Assert.Equal(open with { Activity = CaptureActivity.Paused, SystemCapturing = false, MicCapturing = false }, paused);
    }

    [Fact]
    public void The_band_of_the_paused_picture_says_what_the_real_one_says()
    {
        var paused = SettingsDump.PausedStatus(ScreenRig.Capturing());

        var band = StatusText.From(paused, 300);

        Assert.Equal("Paused", band.Title);
        Assert.Contains("mic not captured", band.Mic);
    }
}
