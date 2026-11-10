using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class WaveMarkerRepaintTests
{
    private const int Rate = InternalFormat.SampleRate;
    private const int Slice = WaveLane.FramesPerSlice;

    private static WaveFacts Facts(long end, long change) =>
        new(null, null, end - 300L * Rate, end, 300 * Rate, [new SourceChange(change, "Game.exe")]);

    [Fact]
    public void A_line_that_leaves_the_edge_for_the_left_of_the_last_bar_is_drawn_again()
    {
        var wave = new NowWave();
        wave.Look(1.0, Facts(10_000L * Rate, 9_990L * Rate), 30, null);
        var columns = wave.Columns;

        // Pause, choose an app: the line stands on the right edge. Then the memory moves on, and the bar after the change is the last one:
        // same bar number, the line is now on its left. Look for that step.
        for (var k = 0; k < 40; k++)
        {
            var change = 9_990L * Rate + k * Slice;
            for (var end = change; end < change + 8L * Rate; end += Slice)
            {
                var before = WaveView.MarkerOf(Facts(end, change), columns);
                var after = WaveView.MarkerOf(Facts(end + Slice, change), columns);
                if (!before.Shows || !after.Shows || before.Column != after.Column || before.Mixed == after.Mixed)
                {
                    continue;
                }

                wave = new NowWave();
                wave.Look(1.0, Facts(end, change), 30, null);
                wave.Look(1.0, Facts(end, change), 30, null);
                var found = wave.Look(1.0, Facts(end + Slice, change), 30, null);

                Assert.Equal(after, wave.Marker);
                Assert.True(found.HasFlag(WaveChange.Titles));
                return;
            }
        }

        Assert.Fail("no step of the window moves the line without changing its bar number");
    }
}
