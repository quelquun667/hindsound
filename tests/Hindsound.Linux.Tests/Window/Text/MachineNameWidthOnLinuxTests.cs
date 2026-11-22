using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Linux.Window.Text;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.Tests.Window.Text;

/// <summary>
/// The name of the first lane of the editor with the real text of Linux (FreeType, Selawik with its hinting), whose measure
/// changes with the scale of the window, at the scales a screen can have: "Whole PC" starts where it has always started, and
/// "Whole Mac" (the same fonts draw it on a Mac) still ends inside the gutter before the wave.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed unsafe class MachineNameWidthOnLinuxTests
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
            using var canvas = new PixelCanvas(bits, 100, 40, scale, LinuxText.Create());

            var pc = canvas.Measure(EditorWave.LaneName(0, "PC"), Face.Body, 12);
            var mac = canvas.Measure(EditorWave.LaneName(0, "Mac"), Face.Body, 12);
            Assert.True(mac > pc, "the Mac name is the wider one");

            Assert.True(EditorWave.LaneNameOffset(pc) == 12, $"Whole PC measures {pc} at {scale}: it starts at {EditorWave.LaneNameOffset(pc)}, not 12");
            Assert.True(EditorWave.LaneNameOffset(mac) + mac <= EditorWave.Gutter, $"Whole Mac ends at {EditorWave.LaneNameOffset(mac) + mac} of the {EditorWave.Gutter} px of the gutter at {scale}");
        }
        finally
        {
            NativeMemory.Free(bits);
        }
    }
}
