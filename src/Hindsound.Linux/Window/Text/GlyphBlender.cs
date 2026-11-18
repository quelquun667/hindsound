using Hindsound.Ui;

namespace Hindsound.Linux.Window.Text;

/// <summary>
/// Lays the grayscale coverage of one glyph over a bitmap, clipped to it. Over an opaque pixel the colour is mixed in linear
/// light with a gamma of 1.8, the gamma DirectWrite's grayscale text is corrected for: light letters on the dark window keep
/// the weight they have on Windows, where a plain mix of the stored values would make them thin and grey. A pixel that is
/// not opaque takes the plain premultiplied mix of <see cref="Pixels.Blend"/>.
/// </summary>
internal static unsafe class GlyphBlender
{
    public const float Gamma = 1.8f;
    private const int LinearSteps = 4096;
    private static readonly ushort[] ToLinear = BuildToLinear();
    private static readonly byte[] FromLinear = BuildFromLinear();

    /// <param name="coverage">Rows of <paramref name="width"/> bytes, top first, 255 for a covered pixel.</param>
    /// <param name="left">The column of the glyph's first byte in the target.</param>
    /// <param name="top">The row of the glyph's first row in the target.</param>
    public static void Blend(Pixels target, ReadOnlySpan<byte> coverage, int width, int height, int left, int top, uint rgb)
    {
        var x0 = Math.Max(0, left);
        var x1 = Math.Min(target.Width, left + width);
        var y0 = Math.Max(0, top);
        var y1 = Math.Min(target.Height, top + height);
        if (x0 >= x1 || y0 >= y1)
        {
            return;
        }

        rgb &= 0xFFFFFF;
        int r = ToLinear[(rgb >> 16) & 0xFF], g = ToLinear[(rgb >> 8) & 0xFF], b = ToLinear[rgb & 0xFF];
        for (var y = y0; y < y1; y++)
        {
            var row = target.Row(y);
            var source = coverage.Slice((y - top) * width, width);
            for (var x = x0; x < x1; x++)
            {
                int c = source[x - left];
                if (c == 0)
                {
                    continue;
                }

                var pixel = row + x;
                if (c == 255)
                {
                    *pixel = 0xFF000000 | rgb;
                }
                else if (*pixel >> 24 == 0xFF)
                {
                    var dst = *pixel;
                    *pixel = 0xFF000000
                        | ((uint)Mix(r, (dst >> 16) & 0xFF, c) << 16)
                        | ((uint)Mix(g, (dst >> 8) & 0xFF, c) << 8)
                        | Mix(b, dst & 0xFF, c);
                }
                else
                {
                    Pixels.Blend(pixel, rgb, c / 255f);
                }
            }
        }
    }

    private static byte Mix(int sourceLinear, uint destination, int coverage) =>
        FromLinear[((sourceLinear * coverage) + (ToLinear[destination] * (255 - coverage)) + 127) / 255];

    private static ushort[] BuildToLinear()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = (ushort)Math.Round(Math.Pow(i / 255.0, Gamma) * (LinearSteps - 1));
        }

        return table;
    }

    private static byte[] BuildFromLinear()
    {
        var table = new byte[LinearSteps];
        for (var i = 0; i < LinearSteps; i++)
        {
            table[i] = (byte)Math.Round(Math.Pow(i / (double)(LinearSteps - 1), 1 / Gamma) * 255);
        }

        return table;
    }
}
