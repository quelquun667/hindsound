using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Audio;

/// <summary>What on the screen moves with time alone, with no new audio and no change of status.</summary>
[Collection(nameof(TimingSensitive))]
public class WaveViewTimeTests
{
    private const int Rate = InternalFormat.SampleRate;
    private const string App = "Discord.exe";

    private static WaveFacts At(int seconds, SourceChange[] changes) =>
        new(null, null, (long)(seconds - 300) * Rate, (long)seconds * Rate, 300 * Rate, changes);

    [Fact]
    public void The_save_line_moves_with_time_alone()
    {
        SourceChange[] changes = [new SourceChange(995L * Rate, App)];

        Assert.Equal("Save: last 0:30 (0:25 from before Discord)", WaveView.SaveLine(30, At(1000, changes)));
        Assert.Equal("Save: last 0:30 (0:15 from before Discord)", WaveView.SaveLine(30, At(1010, changes)));
        Assert.Equal("Save: last 0:30", WaveView.SaveLine(30, At(1030, changes)));
    }

    [Fact]
    public void The_seconds_of_the_save_line_change_with_time_and_cost_nothing_to_read()
    {
        SourceChange[] changes = [new SourceChange(995L * Rate, App)];
        var at1000 = At(1000, changes);
        var at1010 = At(1010, changes);

        Assert.Equal((30, 25), WaveView.SaveSeconds(30, at1000));
        Assert.Equal((30, 15), WaveView.SaveSeconds(30, at1010));
        Assert.Equal((30, 0), WaveView.SaveSeconds(30, At(1030, changes)));
        Assert.Equal(WaveView.SaveSeconds(30, at1000), WaveView.SaveSeconds(30, at1000));

        Action reads = () =>
        {
            for (var i = 0; i < 1000; i++)
            {
                _ = WaveView.SaveSeconds(30, at1000);
                _ = WaveView.SaveSeconds(30, at1010);
            }
        };

        Assert.Equal(0, WaveRig.LeastAllocated(reads));
    }

    [Fact]
    public void Flat_bars_stay_equal_while_the_line_of_a_change_moves()
    {
        var track = new SourceTrack(2, 300 * Rate, 0);
        track.PadTo(300 * Rate);
        var lane = new WaveLane();
        var other = new WaveLane();
        SourceChange[] changes = [new SourceChange(290L * Rate, App)];
        var first = WaveRig.FactsOf(track, 300 * Rate, changes: changes);
        WaveRig.ReadAll(lane, first);
        var before = new byte[180];
        WaveView.Compose(lane, first, before);
        var line = WaveView.MarkerOf(first, 180);

        track.PadTo(305 * Rate);
        var second = WaveRig.FactsOf(track, 300 * Rate, changes: changes);
        WaveLane.CatchUp(second, lane, other);
        var after = new byte[180];
        WaveView.Compose(lane, second, after);

        // The bars alone would say nothing moved.
        Assert.True(before.AsSpan().SequenceEqual(after));
        Assert.NotEqual(line.Column, WaveView.MarkerOf(second, 180).Column);
    }

    [Fact]
    public void Flat_bars_stay_equal_while_the_saved_part_moves()
    {
        var track = new SourceTrack(2, 300 * Rate, 0);
        track.PadTo(300 * Rate);
        var lane = new WaveLane();
        var other = new WaveLane();
        var first = WaveRig.FactsOf(track, 300 * Rate);
        WaveRig.ReadAll(lane, first);
        var before = new byte[180];
        WaveView.Compose(lane, first, before);
        var zones = new HashSet<int> { WaveView.ZoneOf(60, first, 180) };

        // The start of the saved part is a frame, the columns a grid: over a few seconds it moves from one to the next.
        var after = new byte[180];
        for (var seconds = 301; seconds <= 320; seconds++)
        {
            track.PadTo((long)seconds * Rate);
            var facts = WaveRig.FactsOf(track, 300 * Rate);
            WaveLane.CatchUp(facts, lane, other);
            WaveView.Compose(lane, facts, after);
            Assert.True(before.AsSpan().SequenceEqual(after));
            zones.Add(WaveView.ZoneOf(60, facts, 180));
        }

        Assert.True(zones.Count > 1);
    }
}
