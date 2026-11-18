using Hindsound.Ui;

namespace Hindsound.Linux.Window.Text;

/// <summary>
/// Lays the grayscale coverage of one glyph over a bitmap, clipped to it, the way DirectWrite's grayscale text lands on the
/// Windows window, so the letters keep the weight and the crispness they have there. Over an opaque pixel:
/// <list type="number">
/// <item>the coverage is rounded to eighths, the steps DirectWrite's grayscale coverage comes in: a pixel covered to 15/16
/// or more is solid, which gives the stems their solid core;</item>
/// <item>the grayscale enhanced contrast of DirectWrite's default rendering parameters (1.0) thickens dark letters, and
/// fades out for light ones, as DirectWrite does;</item>
/// <item>DirectWrite's alpha correction for its default gamma, 1.8, gives the letters the weight a mix in linear light
/// would give;</item>
/// <item>the colour is mixed with the pixel by that corrected coverage, in the stored values.</item>
/// </list>
/// Measured against DirectWrite on Windows 11 (18/11/2026): the same corrected coverage, to a level, for every step. No
/// coverage leaves the pixel as it is and full coverage gives the colour itself. A pixel that is not opaque takes the plain
/// premultiplied mix of <see cref="Pixels.Blend"/>.
/// </summary>
internal static unsafe class GlyphBlender
{
    /// <summary>The steps of the coverage: eighths.</summary>
    public const int Levels = 8;

    /// <summary>DirectWrite's default gamma (IDWriteRenderingParams::GetGamma on Windows 11).</summary>
    public const double Gamma = 1.8;

    /// <summary>DirectWrite's default grayscale enhanced contrast (IDWriteRenderingParams1::GetGrayscaleEnhancedContrast).</summary>
    public const double GrayscaleEnhancedContrast = 1.0;

    // The alpha correction DirectWrite applies for a gamma of 1.8: a + a(1 - a)((x f + y) a + (z f + w)), f the
    // intensity of the colour of the text.
    private const double RatioX = 0.1469, RatioY = -0.8911, RatioZ = 1.4644, RatioW = -0.3234;

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
        int r = (int)(rgb >> 16) & 0xFF, g = (int)(rgb >> 8) & 0xFF, b = (int)rgb & 0xFF;

        // The corrected coverage of each step, in 1/65536.
        Span<int> weights = stackalloc int[Levels + 1];
        for (var level = 0; level <= Levels; level++)
        {
            weights[level] = (int)Math.Round(Corrected((double)level / Levels, rgb) * 65536);
        }

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
                if (*pixel >> 24 != 0xFF)
                {
                    Pixels.Blend(pixel, rgb, c / 255f);
                    continue;
                }

                var level = Level(c);
                if (level == 0)
                {
                    continue;
                }

                if (level == Levels)
                {
                    *pixel = 0xFF000000 | rgb;
                    continue;
                }

                var dst = *pixel;
                var w = weights[level];
                *pixel = 0xFF000000
                    | ((uint)Mix(r, (int)(dst >> 16) & 0xFF, w) << 16)
                    | ((uint)Mix(g, (int)(dst >> 8) & 0xFF, w) << 8)
                    | (uint)Mix(b, (int)dst & 0xFF, w);
            }
        }
    }

    /// <summary>The coverage rounded to eighths: 0 below 16, 8 from 240.</summary>
    internal static int Level(int coverage) => ((coverage * Levels) + 127) / 255;

    /// <summary>
    /// The coverage (0 to 1) DirectWrite mixes a text of this colour with: the grayscale enhanced contrast, reduced to
    /// nothing as the colour's intensity goes from 0.5 to 0.75, then the alpha correction of the gamma. 0 and 1 stay.
    /// </summary>
    internal static double Corrected(double coverage, uint rgb)
    {
        var f = Intensity(rgb);
        var k = GrayscaleEnhancedContrast * Math.Clamp(4 * (0.75 - f), 0, 1);
        var a = coverage * (k + 1) / ((coverage * k) + 1);
        a += a * (1 - a) * ((((RatioX * f) + RatioY) * a) + ((RatioZ * f) + RatioW));
        return Math.Clamp(a, 0, 1);
    }

    /// <summary>The intensity DirectWrite weighs a colour with: 0.30 red, 0.59 green, 0.11 blue, from 0 to 1.</summary>
    internal static double Intensity(uint rgb) =>
        ((0.30 * ((rgb >> 16) & 0xFF)) + (0.59 * ((rgb >> 8) & 0xFF)) + (0.11 * (rgb & 0xFF))) / 255;

    private static int Mix(int source, int destination, int weight) =>
        destination + ((((source - destination) * weight) + (source >= destination ? 32768 : -32768)) / 65536);
}
