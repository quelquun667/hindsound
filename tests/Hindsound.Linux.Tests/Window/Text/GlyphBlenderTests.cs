using Hindsound.Linux.Window.Text;
using Hindsound.Ui;

namespace Hindsound.Linux.Tests.Window.Text;

/// <summary>How the coverage of a glyph lands on the bitmap: the colour, the gamma, and nothing outside the bitmap.</summary>
public sealed unsafe class GlyphBlenderTests
{
    private const int Width = 20;
    private const int Height = 10;
    private const int Guard = 64;
    private const uint Canary = 0x12345678;

    // A bitmap of opaque black with a guard of canaries before and after it, where a write off the bitmap would land.
    private static uint[] Memory(uint fill = 0xFF000000)
    {
        var memory = new uint[Guard + (Width * Height) + Guard];
        Array.Fill(memory, Canary);
        Array.Fill(memory, fill, Guard, Width * Height);
        return memory;
    }

    private static void Blend(uint[] memory, byte[] coverage, int width, int height, int left, int top, uint rgb)
    {
        fixed (uint* bits = memory)
        {
            GlyphBlender.Blend(new Pixels(bits + Guard, Width, Height), coverage, width, height, left, top, rgb);
        }
    }

    private static uint At(uint[] memory, int x, int y) => memory[Guard + (y * Width) + x];

    private static byte[] Solid(int width, int height) => Enumerable.Repeat((byte)255, width * height).ToArray();

    [Fact]
    public void A_covered_pixel_takes_the_colour_and_an_uncovered_one_stays()
    {
        var memory = Memory();

        Blend(memory, [255, 0], 2, 1, 3, 4, 0x20C0F0);

        Assert.Equal(0xFF20C0F0u, At(memory, 3, 4));
        Assert.Equal(0xFF000000u, At(memory, 4, 4));
    }

    [Fact]
    public void Half_coverage_over_an_opaque_pixel_mixes_in_linear_light()
    {
        var memory = Memory();

        Blend(memory, [128], 1, 1, 0, 0, 0xFFFFFF);

        // White over black at half coverage: (128/255)^(1/1.8) of full, about 174, where a plain mix gives 128.
        var channel = At(memory, 0, 0) & 0xFF;
        Assert.InRange(channel, 170u, 178u);
        Assert.Equal(0xFFu, At(memory, 0, 0) >> 24);
    }

    [Fact]
    public void A_pixel_that_is_not_opaque_stays_premultiplied()
    {
        var memory = Memory(0x00000000);

        Blend(memory, [128], 1, 1, 0, 0, 0xFFFFFF);

        var pixel = At(memory, 0, 0);
        var alpha = pixel >> 24;
        Assert.InRange(alpha, 120u, 136u);
        Assert.True((pixel & 0xFF) <= alpha && ((pixel >> 8) & 0xFF) <= alpha && ((pixel >> 16) & 0xFF) <= alpha, pixel.ToString("X8"));
    }

    [Theory]
    [InlineData(-2, -2, 4)]
    [InlineData(Width - 2, -2, 4)]
    [InlineData(-2, Height - 2, 4)]
    [InlineData(Width - 2, Height - 2, 4)]
    [InlineData(-4, 3, 0)]
    [InlineData(Width, 3, 0)]
    [InlineData(3, -4, 0)]
    [InlineData(3, Height, 0)]
    [InlineData(-100, -100, 0)]
    public void A_glyph_across_an_edge_draws_only_its_part_on_the_bitmap(int left, int top, int expected)
    {
        var memory = Memory();

        Blend(memory, Solid(4, 4), 4, 4, left, top, 0xFFFFFF);

        Assert.Equal(expected, memory.Skip(Guard).Take(Width * Height).Count(p => p == 0xFFFFFFFF));
        Assert.All(memory.Take(Guard).Concat(memory.Skip(Guard + (Width * Height))), p => Assert.Equal(Canary, p));
    }

    [Fact]
    public void Each_row_of_the_glyph_lands_on_its_own_row()
    {
        var memory = Memory();
        // A glyph two wide and three high whose middle row only is covered.
        byte[] coverage = [0, 0, 255, 255, 0, 0];

        Blend(memory, coverage, 2, 3, 5, 2, 0xFFFFFF);

        Assert.Equal(0xFF000000u, At(memory, 5, 2));
        Assert.Equal(0xFFFFFFFFu, At(memory, 5, 3));
        Assert.Equal(0xFFFFFFFFu, At(memory, 6, 3));
        Assert.Equal(0xFF000000u, At(memory, 5, 4));
    }
}
