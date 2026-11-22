using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// "Whole Mac" is wider than "Whole PC": it must still stand in the gutter the editor keeps for the names of its lanes. This
/// measures with GDI (Segoe UI), where a measure does not depend on the scale; the Mac and Linux draw with FreeType in Selawik,
/// where it does, and those two are measured at four scales by the tests of their own projects.
/// </summary>
public class MachineNameWidthTests
{
    [Fact]
    public void The_name_of_the_lane_of_a_Mac_stays_in_the_gutter_before_the_wave()
    {
        using var canvas = new GdiCanvas(100, 40, 1.0);

        var pc = canvas.Measure(EditorWave.LaneName(0, "PC"), Face.Body, 12);
        var mac = canvas.Measure(EditorWave.LaneName(0, "Mac"), Face.Body, 12);
        Assert.True(mac > pc, "the Mac name is the wider one");

        // Where "Whole PC" starts is where it has always started; the wave begins a gutter in.
        Assert.Equal(12, EditorWave.LaneNameOffset(pc));
        Assert.True(EditorWave.LaneNameOffset(mac) + mac <= EditorWave.Gutter, $"Whole Mac ends at {EditorWave.LaneNameOffset(mac) + mac} of the {EditorWave.Gutter} px of the gutter");
        Assert.True(EditorWave.LaneNameOffset(mac) >= 4, "and keeps clear of the edge of the panel");
    }
}
