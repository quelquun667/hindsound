using Hindsound.Audio;
using Hindsound.Tests.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

[Collection(nameof(TimingSensitive))]
public class WaveBarsAllocationTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Drawing_a_whole_lane_allocates_nothing(double scale)
    {
        using var canvas = new GdiCanvas((int)(600 * scale), (int)(300 * scale), scale, null);
        var grid = WaveGrid.Of(new Box(24, 138, 527, 116), scale);
        var levels = new byte[grid.Columns];
        var columns = new BarColumn[grid.Columns];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = i % 11 == 0 ? WaveLane.Empty : (byte)(i * 3 % 255);
            columns[i] = i % 3 == 0 ? new BarColumn(0x2EF2C4, 0x1D5A4E) : new BarColumn(0xAAB1BE);
        }

        canvas.Bars(grid, levels, columns, Theme.Line);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            canvas.Bars(grid, levels, columns, Theme.Line);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
