using Hindsound.Audio;

namespace Hindsound.Tests.Audio;

/// <summary>Guards of the lane's handling of the window that the longer scenarios do not reach.</summary>
public class WaveLaneWindowGuardTests
{
    private const int Slice = WaveRig.Slice;
    private const int Rate = WaveRig.Rate;

    [Fact]
    public void Slices_that_left_the_window_show_nothing_though_no_audio_came()
    {
        var track = new SourceTrack(1, 5 * Rate, 0);
        WaveRig.Write(track, 5 * Rate, 1);
        var lane = new WaveLane();
        var other = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(track, 5 * Rate));
        Assert.NotEqual(WaveLane.Empty, lane.LevelOf(0));

        var later = WaveRig.FactsOf(track, 5 * Rate, end: track.EndFrame + 3 * Slice);
        WaveLane.CatchUp(later, lane, other);

        for (var slice = 0; slice < 3; slice++)
        {
            Assert.Equal(WaveLane.Empty, lane.LevelOf(slice));
        }
    }

    [Fact]
    public void A_slice_the_writer_overtook_while_it_was_half_read_is_not_kept()
    {
        const int Number = 20;
        var half = Number * Slice + Slice / 2;
        var probe = new SourceTrack(1, Rate, 0);
        WaveRig.Write(probe, 3 * Rate, 9);
        var held = (int)(probe.EndFrame - probe.OldestFrame);

        var track = new SourceTrack(1, Rate, 0);
        WaveRig.Write(track, half, 1);
        var lane = new WaveLane();
        var other = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(track, Rate));
        Assert.NotEqual(WaveLane.Empty, lane.LevelOf(Number));

        // The oldest frame passes the cursor inside the same slice. The window ends soon after, so the slice stays on screen.
        WaveRig.Write(track, held + Slice / 4, 2);
        Assert.InRange(track.OldestFrame, half + 1, (Number + 1) * Slice - 1);
        var facts = new WaveFacts(track, null, track.OldestFrame, track.OldestFrame + 600, Rate, []);
        WaveLane.CatchUp(facts, lane, other);

        Assert.Equal(WaveLane.Empty, lane.LevelOf(Number));
    }

    [Fact]
    public void A_track_ahead_of_the_window_is_not_read_past_its_end()
    {
        var track = new SourceTrack(1, 5 * Rate, 0);
        WaveRig.Write(track, 3 * Rate, 1);
        var facts = WaveRig.FactsOf(track, 5 * Rate, end: 2 * Rate + Slice / 2);
        var lane = new WaveLane();

        WaveRig.ReadAll(lane, facts);

        Assert.NotEqual(WaveLane.Empty, lane.LevelOf(20));
        Assert.Equal(WaveLane.Empty, lane.LevelOf(21));
    }
}
