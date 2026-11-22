using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Mac.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Mac.Tests.Window;

/// <summary>
/// The name of the first lane of the editor with the real text of the Mac (FreeType, Selawik with its hinting), whose measure
/// changes with the scale of the window, at the scales a screen can have: "Whole Mac" ends inside the gutter before the wave
/// and "Whole PC" still starts where it has always started.
/// </summary>
[Trait("Category", "Mac")]
[SupportedOSPlatform("macos")]
public sealed unsafe class MachineNameWidthOnMacTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_names_of_the_lane_keep_their_place_in_the_gutter_at_every_scale(double scale)
    {
        var bits = (uint*)NativeMemory.AllocZeroed(100 * 40, sizeof(uint));
        try
        {
            using var canvas = new PixelCanvas(bits, 100, 40, scale, MacText.Create());

            var pc = canvas.Measure(EditorWave.LaneName(0, "PC"), Face.Body, 12);
            var mac = canvas.Measure(EditorWave.LaneName(0, "Mac"), Face.Body, 12);
            Assert.True(mac > pc, "the Mac name is the wider one");

            Assert.True(EditorWave.LaneNameOffset(mac) + mac <= EditorWave.Gutter, $"Whole Mac ends at {EditorWave.LaneNameOffset(mac) + mac} of the {EditorWave.Gutter} px of the gutter at {scale}");
            Assert.True(EditorWave.LaneNameOffset(mac) >= 4, "and keeps clear of the edge of the panel");
            Assert.True(EditorWave.LaneNameOffset(pc) == 12, $"Whole PC measures {pc} at {scale}: it starts at {EditorWave.LaneNameOffset(pc)}, not 12");
        }
        finally
        {
            NativeMemory.Free(bits);
        }
    }
}
