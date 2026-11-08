using Hindsound.Audio;

namespace Hindsound.Tests.Audio;

/// <summary>
/// Each test here stands on one guard of the lane that nothing else would notice without it. The names say which.
/// </summary>
public class WaveLaneGuardTests
{
    private const int Slice = WaveRig.Slice;
    private const int Rate = WaveRig.Rate;

    private static float[] Tone(int frames, float value)
    {
        var samples = new float[frames];
        Array.Fill(samples, value);
        return samples;
    }

    [Fact]
    public void A_new_slice_on_a_used_slot_inherits_nothing_from_the_old_one()
    {
        // Small steps on a ring that went round: a slice is started from nothing, or it keeps the old peaks.
        var track = new SourceTrack(1, Rate, 0);
        var lane = new WaveLane();
        for (var step = 0; step < 40; step++)
        {
            track.AppendContinuation(Tone(Slice / 2, 0.9f));
            WaveRig.ReadAll(lane, WaveRig.FactsOf(track, Rate));
        }

        for (var step = 0; step < 40; step++)
        {
            track.AppendContinuation(Tone(Slice / 2, 0.001f));
            WaveRig.ReadAll(lane, WaveRig.FactsOf(track, Rate));
        }

        WaveRig.AssertLikeRaw(lane, track, WaveRig.FactsOf(track, Rate));
    }

    [Fact]
    public void An_end_stepping_back_two_slices_does_not_touch_the_newest_slice()
    {
        // The microphone comes back after a silence and the end steps back while the PC sound was read further on.
        var track = new SourceTrack(1, 2 * Rate, 0);
        WaveRig.Write(track, 10 * Rate + 1, 7);
        var lane = new WaveLane();
        var other = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(track, 2 * Rate));
        var newest = WaveRig.Raw(track, 100);
        Assert.Equal(newest, lane.LevelOf(100));

        WaveLane.CatchUp(WaveRig.FactsOf(track, 2 * Rate, end: track.EndFrame - 2 * Slice), lane, other);

        Assert.Equal(newest, lane.LevelOf(100));
    }

    [Fact]
    public void A_gone_mark_does_not_follow_the_lane_to_a_new_start()
    {
        var old = new SourceTrack(1, Rate, 0);
        WaveRig.Write(old, 30 * Rate + 7, 1);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, WaveRig.FactsOf(old, 2 * Rate));

        // The frames count from zero again: what the old track found gone says nothing about this one.
        var fresh = new SourceTrack(1, Rate, 0);
        WaveRig.Write(fresh, 5 * Rate, 2);
        var facts = WaveRig.FactsOf(fresh, 2 * Rate);
        WaveRig.ReadAll(lane, facts);

        WaveRig.AssertLikeRaw(lane, fresh, facts);
    }
}
