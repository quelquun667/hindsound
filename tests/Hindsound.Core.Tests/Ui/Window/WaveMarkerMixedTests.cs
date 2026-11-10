using Hindsound.Audio;
using Hindsound.Tests.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The line of a change of source stands to the right of every bar that holds sound from before the change.</summary>
public class WaveMarkerMixedTests
{
    private const int Rate = InternalFormat.SampleRate;
    private const int Slice = WaveLane.FramesPerSlice;

    private static WaveFacts Facts(int bufferSeconds, long end, long changeFrame, double heldSeconds)
    {
        var buffer = bufferSeconds * Rate;
        return new WaveFacts(null, null, end - (long)(heldSeconds * Rate), end, buffer, [new SourceChange(changeFrame, "Game.exe")]);
    }

    // The first frame of each bar, worked out from the grid: a bar holds the slices from column * slices / columns on.
    private static long FirstFrameOf(int windowIndex, int columns, long slices, long end)
    {
        var lastSlice = (end - 1) / Slice;
        var right = ((lastSlice + 1) * columns + slices - 1) / slices - 1;
        var column = windowIndex + right - (columns - 1);
        return column < 0 ? -1 : column * slices / columns * Slice;
    }

    [Theory]
    [InlineData(300, 87)]
    [InlineData(1800, 87)]
    [InlineData(60, 60)]
    [InlineData(1, 180)]
    public void No_bar_with_sound_from_before_the_change_is_right_of_the_line(int bufferSeconds, int columns)
    {
        var slices = Math.Max(1, ((long)bufferSeconds * Rate + Slice - 1) / Slice);
        foreach (var endOffset in new[] { 0, 1234, Slice / 2 + 7 })
        {
            var end = 100_000L * Rate / 10 + endOffset;
            for (var back = 0L; back <= (long)bufferSeconds * Rate; back += 3331)
            {
                var facts = Facts(bufferSeconds, end, end - back, bufferSeconds);
                var marker = WaveView.MarkerOf(facts, columns);
                if (!marker.Shows)
                {
                    continue;
                }

                for (var i = 0; i < columns; i++)
                {
                    var first = FirstFrameOf(i, columns, slices, end);
                    if (first < 0)
                    {
                        continue;
                    }

                    var gray = WaveView.ToneOf(i, 0, marker) is WaveTone.GrayLight or WaveTone.GrayDark;
                    Assert.True(gray == first < end - back, $"buffer {bufferSeconds}, {columns} columns, change {back} frames before the end, end offset {endOffset}, bar {i}: gray {gray}");
                }
            }
        }
    }

    [Fact]
    public void A_change_with_nothing_after_it_greys_every_bar_and_stands_on_the_right_edge()
    {
        var end = 1000L * Rate;
        var marker = WaveView.MarkerOf(Facts(300, end, end, 300), 87);

        Assert.Equal(86, marker.Column);
        Assert.True(marker.Mixed);
        Assert.Equal(WaveTone.GrayLight, WaveView.ToneOf(86, 0, marker));
    }

    [Fact]
    public void A_change_on_the_edge_of_a_bar_leaves_that_bar_to_the_new_source()
    {
        var marker = WaveView.MarkerOf(Facts(60, 1000L * Rate, 960L * Rate, 60), 60);

        Assert.Equal(new WaveMarker(20, "Game.exe"), marker);
        Assert.Equal(WaveTone.Accent, WaveView.ToneOf(20, 0, marker));
    }
}
