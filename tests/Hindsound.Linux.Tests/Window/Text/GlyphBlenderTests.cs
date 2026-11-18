using Hindsound.Linux.Window.Text;
using Hindsound.Ui;

namespace Hindsound.Linux.Tests.Window.Text;

/// <summary>How the coverage of a glyph lands on the bitmap: the colour, DirectWrite's steps, contrast and gamma, and nothing
/// outside the bitmap.</summary>
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

    private static readonly uint[] Colours = [0xFFFFFF, 0x000000, 0xF2F4F7, 0xD4F53C, 0x808080, 0x2060A0];
    private static readonly uint[] Backgrounds = [0xFF000000, 0xFFFFFFFF, 0xFF101418, 0xFF3A7F55];

    [Fact]
    public void Coverage_comes_in_eighths_and_from_15_16_a_pixel_is_solid()
    {
        Assert.Equal(0, GlyphBlender.Level(15));
        Assert.Equal(1, GlyphBlender.Level(16));
        Assert.Equal(4, GlyphBlender.Level(128));
        Assert.Equal(7, GlyphBlender.Level(239));
        Assert.Equal(8, GlyphBlender.Level(240));
        Assert.Equal(8, GlyphBlender.Level(255));
        var memory = Memory(0xFF101418);

        Blend(memory, [245, 10, 120, 124], 4, 1, 0, 0, 0xF2F4F7);

        Assert.Equal(0xFFF2F4F7u, At(memory, 0, 0));
        Assert.Equal(0xFF101418u, At(memory, 1, 0));
        Assert.Equal(At(memory, 2, 0), At(memory, 3, 0));
    }

    // What DirectWrite drew on Windows 11 (18/11/2026), grayscale, its default rendering parameters (gamma 1.8, grayscale
    // enhanced contrast 1), for each step of its raw coverage (drawn with gamma 1 and no contrast, same layout): white
    // text on black, and black text on white given as coverage (255 less the value). Light text takes no contrast.
    [Theory]
    [InlineData(0xFFFFFFu, 0xFF000000u, new[] { 61, 110, 147, 177, 199, 218, 236 })]
    [InlineData(0x000000u, 0xFFFFFFFFu, new[] { 34, 60, 88, 118, 150, 184, 220 })]
    public void Each_eighth_lands_as_DirectWrites_grayscale_text_does(uint rgb, uint background, int[] expected)
    {
        for (var level = 1; level < GlyphBlender.Levels; level++)
        {
            var memory = Memory(background);

            Blend(memory, [(byte)Math.Round(level * 255 / 8.0)], 1, 1, 0, 0, rgb);

            var green = (int)(At(memory, 0, 0) >> 8) & 0xFF;
            var drawn = rgb == 0 ? 255 - green : green;
            Assert.True(Math.Abs(drawn - expected[level - 1]) <= 2, $"{level}/8: {drawn}, DirectWrite {expected[level - 1]}");
        }
    }

    [Fact]
    public void No_coverage_keeps_the_pixel_and_full_coverage_gives_the_colour_whatever_both_are()
    {
        foreach (var rgb in Colours)
        {
            foreach (var background in Backgrounds)
            {
                var memory = Memory(background);

                Blend(memory, [255, 0, 1, 254], 4, 1, 0, 0, rgb);

                Assert.Equal(0xFF000000 | rgb, At(memory, 0, 0));
                Assert.Equal(background, At(memory, 1, 0));
                Assert.Equal(background, At(memory, 2, 0));
                Assert.Equal(0xFF000000 | rgb, At(memory, 3, 0));
                Assert.Equal(0.0, GlyphBlender.Corrected(0, rgb));
                Assert.Equal(1.0, GlyphBlender.Corrected(1, rgb));
            }
        }
    }

    [Fact]
    public void Each_step_of_coverage_moves_every_channel_toward_the_colour()
    {
        foreach (var rgb in Colours)
        {
            foreach (var background in Backgrounds)
            {
                var previous = background;
                for (var level = 1; level <= GlyphBlender.Levels; level++)
                {
                    var memory = Memory(background);
                    Blend(memory, [(byte)Math.Round(level * 255 / 8.0)], 1, 1, 0, 0, rgb);
                    var pixel = At(memory, 0, 0);
                    for (var shift = 0; shift <= 16; shift += 8)
                    {
                        int target = (int)(rgb >> shift) & 0xFF, before = (int)(previous >> shift) & 0xFF, now = (int)(pixel >> shift) & 0xFF;
                        Assert.True(Math.Abs(target - now) <= Math.Abs(target - before), $"{rgb:X6} over {background:X8}, {level}/8: {before} then {now}");
                    }

                    previous = pixel;
                }
            }
        }
    }

    [Fact]
    public void The_contrast_thickens_dark_text_and_fades_out_for_light_text()
    {
        // At half coverage the gamma alone gives black text 0.31 and white text 0.69; the contrast of 1 brings black to 0.46,
        // and would bring white to 0.81 if it did not fade out for light colours.
        Assert.InRange(GlyphBlender.Corrected(0.5, 0x000000), 0.44, 0.48);
        Assert.InRange(GlyphBlender.Corrected(0.5, 0xFFFFFF), 0.67, 0.71);
        Assert.InRange(GlyphBlender.Corrected(0.5, 0xF2F4F7), 0.66, 0.71);
        Assert.Equal(1.0, GlyphBlender.Intensity(0xFFFFFF), 6);
        Assert.Equal(0.0, GlyphBlender.Intensity(0x000000), 6);
        Assert.Equal(0.59, GlyphBlender.Intensity(0x00FF00), 6);
    }
}
