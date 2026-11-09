using Hindsound.Audio;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A microphone that was never captured is not a lost one: the lane says what the band says, and the title stays quiet.</summary>
public class MicNeverCapturedTests
{
    [Fact]
    public void With_no_track_the_title_stays_Mic_and_the_lane_says_so()
    {
        var status = ScreenRig.Capturing(micCapturing: false);

        Assert.Equal("Mic", WaveView.MicLaneTitle(status, hasTrack: false));
        Assert.Equal("Mic not captured", WaveView.MicLaneText(status, hasTrack: false));
    }

    [Fact]
    public void With_a_track_a_lost_mic_is_still_lost_and_keeps_its_bars()
    {
        var status = ScreenRig.Capturing(micCapturing: false);

        Assert.Equal(WaveView.MicLost, WaveView.MicLaneTitle(status, hasTrack: true));
        Assert.Null(WaveView.MicLaneText(status, hasTrack: true));
    }

    [Theory]
    [InlineData(CaptureActivity.Paused)]
    [InlineData(CaptureActivity.Stopped)]
    [InlineData(CaptureActivity.NoDevice)]
    public void The_lane_says_nothing_new_when_no_capture_is_open_at_all(CaptureActivity activity)
    {
        var status = ScreenRig.Capturing(micCapturing: false, activity: activity);

        Assert.Null(WaveView.MicLaneText(status, hasTrack: false));
    }

    [Fact]
    public void A_mic_that_is_off_says_off_whatever_the_track()
    {
        var status = ScreenRig.Capturing(micCapturing: false, micEnabled: false);

        Assert.Equal("Mic off", WaveView.MicLaneText(status, hasTrack: false));
        Assert.Null(WaveView.MicLaneText(ScreenRig.Capturing(), hasTrack: false));
    }

    [Fact]
    public void On_the_page_the_title_is_not_amber_and_the_words_stand_in_the_lane()
    {
        using var rig = new NowRig(NowRig.Held(NowRig.Loud(60), null, 60), ScreenRig.Capturing(micCapturing: false), 1.0);
        rig.Settle();

        Assert.Equal(0, rig.Near(Theme.Amber, new Box(200, 262, 60, 16)));
        Assert.NotEqual(0, rig.Near(Theme.Muted, new Box(NowRig.MicLane.MidX - 50, rig.MicGrid.Mid - 8, 100, 16)));
    }
}
