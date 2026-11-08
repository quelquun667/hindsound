using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Audio;

public class WaveLaneMuteTests
{
    private const int Rate = WaveRig.Rate;

    [Fact]
    public void A_lane_caught_up_on_a_mute_PC_still_moves_the_bars_with_time()
    {
        // Nothing is read, yet the window slides, so whoever stops following the lane once it is caught up freezes the wave.
        var track = new SourceTrack(1, 5 * Rate, 0);
        WaveRig.Write(track, 3 * Rate, 1);
        var lane = new WaveLane();
        var other = new WaveLane();
        var first = WaveRig.FactsOf(track, 5 * Rate);
        WaveRig.ReadAll(lane, first);
        var before = new byte[180];
        WaveView.Compose(lane, first, before);
        Assert.True(lane.IsCaughtUp);

        var later = WaveRig.FactsOf(track, 5 * Rate, end: track.EndFrame + Rate);
        WaveLane.CatchUp(later, lane, other);
        var after = new byte[180];
        WaveView.Compose(lane, later, after);

        Assert.True(lane.IsCaughtUp);
        Assert.False(before.AsSpan().SequenceEqual(after));
    }
}
