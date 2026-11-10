using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

public unsafe class PixelsRoundFillTests
{
    private const int Width = 64, Height = 48;

    private static uint[] Drawn(Action<Pixels> draw)
    {
        var buffer = new uint[Width * Height];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = 0xFF102030u + (uint)i % 7;
        }

        fixed (uint* origin = buffer)
        {
            draw(new Pixels(origin, Width, Height));
        }

        return buffer;
    }

    // The definition: every pixel from its distance to the shape, with nothing skipped.
    private static void PixelByPixel(Pixels pixels, uint* origin, float left, float top, float right, float bottom, float radius, uint rgb)
    {
        for (var y = Math.Max(0, (int)top); y < Math.Min(Height, (int)MathF.Ceiling(bottom)); y++)
        {
            for (var x = Math.Max(0, (int)left); x < Math.Min(Width, (int)MathF.Ceiling(right)); x++)
            {
                Pixels.Blend(origin + y * Width + x, rgb, Pixels.RoundRectCoverage(x + 0.5f, y + 0.5f, left, top, right, bottom, radius));
            }
        }
    }

    [Theory]
    [InlineData(4, 4, 60, 44, 4)]
    [InlineData(5, 6, 59, 40, 3)]
    [InlineData(2.5, 3.25, 41.75, 30.5, 2.5)]
    [InlineData(0, 0, 64, 48, 8)]
    [InlineData(-10, -10, 80, 70, 6)]
    [InlineData(10, 10, 14, 13, 8)]
    [InlineData(10, 10, 11, 40, 0.01)]
    [InlineData(10, 10, 40, 11, 0.01)]
    [InlineData(10.5, 10.5, 12.5, 12.5, 0.01)]
    [InlineData(20, 20, 40, 40, 10)]
    [InlineData(20, 20, 40, 40, 12)]
    [InlineData(3, 3, 3, 30, 2)]
    [InlineData(30, 30, 20, 40, 2)]
    public void The_fast_middle_gives_the_very_pixels_of_the_definition(float left, float top, float right, float bottom, float radius)
    {
        const uint rgb = 0xD8FF2E;

        var fast = Drawn(p => p.FillRoundRect(left, top, right, bottom, radius, rgb));
        var slow = new uint[Width * Height];
        for (var i = 0; i < slow.Length; i++)
        {
            slow[i] = 0xFF102030u + (uint)i % 7;
        }

        fixed (uint* origin = slow)
        {
            PixelByPixel(new Pixels(origin, Width, Height), origin, left, top, right, bottom, radius, rgb);
        }

        Assert.Equal(slow, fast);
    }

    [Fact]
    public void A_sweep_of_edges_and_radii_gives_the_same_pixels()
    {
        const uint rgb = 0x3080FF;
        for (var inset = 0f; inset < 6; inset += 0.25f)
        {
            for (var radius = 0.01f; radius < 12; radius += 0.73f)
            {
                var (left, top, right, bottom) = (inset, inset * 2, Width - inset * 3, Height - inset);
                var fast = Drawn(p => p.FillRoundRect(left, top, right, bottom, radius, rgb));
                var slow = new uint[Width * Height];
                for (var i = 0; i < slow.Length; i++)
                {
                    slow[i] = 0xFF102030u + (uint)i % 7;
                }

                fixed (uint* origin = slow)
                {
                    PixelByPixel(new Pixels(origin, Width, Height), origin, left, top, right, bottom, radius, rgb);
                }

                Assert.True(slow.AsSpan().SequenceEqual(fast), $"inset {inset}, radius {radius}");
            }
        }
    }
}