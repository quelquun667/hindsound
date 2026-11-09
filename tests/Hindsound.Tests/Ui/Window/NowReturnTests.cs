using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>Coming back to the Now page reads what arrived while it was away, before the first picture and not a tick later.</summary>
public class NowReturnTests
{
    [Fact]
    public void The_first_paint_after_a_return_shows_what_was_heard_in_the_meantime()
    {
        var system = NowRig.Loud(60);
        var mic = NowRig.Loud(60);
        using var rig = new NowRig(NowRig.Held(system, mic, 20));
        rig.Settle();
        var columns = rig.Wave.Columns;
        Assert.Equal(WaveLane.Empty, rig.Wave.SystemBars[0]);

        rig.Screen.ShowSection(Section.Hotkeys);
        rig.Panel.Facts = NowRig.Held(system, mic, 50);
        rig.Screen.ShowSection(Section.Now);
        rig.Screen.Paint(rig.Canvas);

        var bars = rig.Wave.SystemBars.ToArray();
        var empty = Enumerable.Range(0, columns).Where(i => bars[i] == WaveLane.Empty).ToArray();

        // 60 s of memory with 50 s held: the first sixth is still to come, the rest is there on this very paint.
        Assert.All(empty, i => Assert.True(i < columns / 6 + 1, $"bar {i} of {columns} is empty on the first paint"));
        Assert.NotEqual(WaveLane.Empty, bars[columns - 1]);
        Assert.NotEqual(WaveLane.Empty, rig.Wave.MicBars[columns - 1]);
    }
}
