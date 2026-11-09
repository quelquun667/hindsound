using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// What the page says in words must stay true when the only thing that moved is the state behind it: the status object is the same,
/// the bars are the same, and a guard that looked at less would leave the old words on screen.
/// </summary>
public class NowLookTruthTests
{
    private const double Scale = 1.0;
    private const uint Accent = 0x7CFF3B;

    [Fact]
    public void The_title_goes_back_to_the_app_when_the_line_leaves_the_wave_with_the_same_status()
    {
        // With the memory full the engine sends no new status: the line leaving the wave is the only thing that tells the title.
        var wave = new NowWave();
        var canvas = new FakeCanvas();
        var status = ScreenRig.Capturing() with { SourceApp = "Discord.exe" };
        var change = new SourceChange(95 * NowRig.Rate, "Discord.exe");

        wave.Look(Scale, NowRig.FullUntil(null, null, 100, change), 30, status);
        Assert.True(wave.Marker.Shows);
        wave.PaintLanes(canvas, Accent, status, titles: true);
        Assert.Contains("Whole PC", canvas.Drawn);
        Assert.DoesNotContain("Discord", canvas.Drawn);
        canvas.Drawn.Clear();

        var later = wave.Look(Scale, NowRig.FullUntil(null, null, 160, change), 30, status);
        wave.PaintLanes(canvas, Accent, status, titles: true);

        Assert.False(wave.Marker.Shows);
        Assert.True((later & WaveChange.Titles) != 0);
        Assert.Contains("Discord", canvas.Drawn);
        Assert.DoesNotContain("Whole PC", canvas.Drawn);
    }

    [Fact]
    public void A_title_that_already_says_the_app_is_not_written_again_for_a_line_that_was_never_there()
    {
        var wave = new NowWave();
        var status = ScreenRig.Capturing() with { SourceApp = "Discord.exe" };
        var facts = NowRig.FullUntil(null, null, 160);
        wave.Look(Scale, facts, 30, status);

        Assert.Equal(WaveChange.None, wave.Look(Scale, facts, 30, status));
    }

    [Fact]
    public void A_device_lost_before_anything_was_kept_says_so_even_with_a_track_in_place()
    {
        // The track exists (a device was open) but not one frame came in: nothing is held, so the lane is not a silent flat line.
        var status = ScreenRig.Capturing(false, CaptureActivity.NoDevice) with { SystemCapturing = false, Buffered = TimeSpan.Zero };
        var facts = new WaveFacts(NowRig.Loud(10), null, 0, 0, NowRig.Memory, []);
        var wave = new NowWave();
        var canvas = new FakeCanvas();
        wave.Look(Scale, facts, 30, status);
        wave.Follow(facts);

        wave.PaintLanes(canvas, Accent, status, titles: true);

        Assert.Contains("No audio device", canvas.Drawn);
    }

    [Fact]
    public void A_device_lost_after_keeping_something_does_not_cover_what_was_kept_with_words()
    {
        var status = ScreenRig.Capturing(false, CaptureActivity.NoDevice) with { SystemCapturing = false };
        var facts = NowRig.Held(NowRig.Loud(30), null, 30);
        var wave = new NowWave();
        var canvas = new FakeCanvas();
        wave.Look(Scale, facts, 30, status);
        wave.Follow(facts);

        wave.PaintLanes(canvas, Accent, status, titles: true);

        Assert.DoesNotContain("No audio device", canvas.Drawn);
    }
}
