using System.Runtime.InteropServices;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The portable canvas on memory of its own, with a text of fixed advance: what another system draws the window with, before its
/// own text exists. Every scene of the dump goes through it, and the shapes land where the scale and the origin say.
/// </summary>
public sealed unsafe class PixelCanvasTests
{
    /// <summary>Each letter is a block of its advance, 0.6 em wide, on a line of 1.25 em: enough to lay out and to see.</summary>
    private sealed class FixedText : ICanvasText
    {
        public List<(FontSpec Font, string Text, float X, float Top, uint Rgb)> Drawn { get; } = [];

        public uint* Target { get; private set; }

        public bool Disposed { get; private set; }

        public float Measure(FontSpec font, string text) => text.Length * Advance(font);

        public float LineHeight(FontSpec font) => MathF.Round(1.25f * Em(font));

        public void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb)
        {
            Target = target.Origin;
            Drawn.Add((font, text, x, top, rgb));
            var advance = Advance(font);
            var height = (int)LineHeight(font);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                {
                    var left = (int)MathF.Round(x + i * advance);
                    target.FillRect(left, (int)MathF.Round(top), left + Math.Max(1, (int)advance - 1), (int)MathF.Round(top) + height, rgb);
                }
            }
        }

        public void Dispose() => Disposed = true;

        private static float Em(FontSpec font) => MathF.Floor(font.Size + 0.5f);

        private static float Advance(FontSpec font) => 0.6f * Em(font);
    }

    /// <summary>Memory the test owns, as a window's would be: the canvas draws into it and never frees it.</summary>
    private sealed class Bitmap : IDisposable
    {
        public Bitmap(int width, int height)
        {
            Width = width;
            Height = height;
            Bits = (uint*)NativeMemory.AllocZeroed((nuint)width * (nuint)height, sizeof(uint));
        }

        public int Width { get; }

        public int Height { get; }

        public uint* Bits { get; }

        public uint At(int x, int y) => Bits[y * Width + x];

        public void Clear() => new Span<uint>(Bits, Width * Height).Clear();

        public int Painted()
        {
            var count = 0;
            foreach (var value in new ReadOnlySpan<uint>(Bits, Width * Height))
            {
                count += value != 0 ? 1 : 0;
            }

            return count;
        }

        public void Dispose() => NativeMemory.Free(Bits);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Every_scene_of_the_dump_draws_through_the_portable_canvas(double scale)
    {
        Bitmap? bitmap = null;
        FixedText? text = null;
        var scenes = new List<string>();
        try
        {
            SettingsDump.Render(scale, (width, height, s) =>
            {
                bitmap = new Bitmap(width, height);
                text = new FixedText();
                return new PixelCanvas(bitmap.Bits, width, height, s, text);
            }, (name, canvas) =>
            {
                Assert.True(canvas.Bits == bitmap!.Bits, name);
                // A page paints its whole ground, then its text: most of the window, and some letters, on every scene.
                Assert.True(bitmap.Painted() > bitmap.Width * bitmap.Height / 2, $"{name}: {bitmap.Painted()} pixels painted");
                Assert.True(text!.Drawn.Count > 0, $"{name}: no text drawn");
                Assert.True(text.Target == bitmap.Bits, name);
                scenes.Add(name);
                bitmap.Clear();
                text.Drawn.Clear();
            });

            var suffix = "@" + (int)Math.Round(scale * 100);
            Assert.Equal(scenes.Count, scenes.Distinct().Count());
            Assert.All(scenes, name => Assert.EndsWith(suffix, name, StringComparison.Ordinal));
            Assert.Contains("audio" + suffix, scenes);
            Assert.All(DumpScenesLibrary.Scenes, scene => Assert.Contains(scene.Name + suffix, scenes));
            Assert.All(DumpScenesEditor.Scenes, scene => Assert.Contains(scene.Name + suffix, scenes));
            Assert.True(text!.Disposed, "the dump disposes its canvas, and the canvas its text");
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    [Fact]
    public void A_box_is_scaled_and_moved_by_the_origin()
    {
        using var bitmap = new Bitmap(20, 20);
        using var canvas = new PixelCanvas(bitmap.Bits, bitmap.Width, bitmap.Height, 2, new FixedText());

        canvas.SetOrigin(3);
        canvas.FillRect(new Box(1, 1, 2, 2), 0x123456);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var inside = x >= 2 && x < 6 && y >= 8 && y < 12;
                Assert.Equal(inside ? 0x123456u : 0u, bitmap.At(x, y) & 0xFFFFFF);
            }
        }
    }

    [Fact]
    public void A_frame_has_a_border_of_one_physical_line_around_its_fill()
    {
        using var bitmap = new Bitmap(40, 40);
        using var canvas = new PixelCanvas(bitmap.Bits, bitmap.Width, bitmap.Height, 2, new FixedText());

        canvas.Frame(new Box(2, 2, 14, 14), 0, 0x00FF00, 0xFF0000);

        // The edges are smooth: the pixels checked are those whole on each side of one.
        Assert.Equal(0xFF0000u, bitmap.At(5, 20) & 0xFFFFFF);
        Assert.Equal(0x00FF00u, bitmap.At(7, 20) & 0xFFFFFF);
        Assert.Equal(0x00FF00u, bitmap.At(20, 20) & 0xFFFFFF);
        Assert.Equal(0u, bitmap.At(3, 20));
    }

    [Fact]
    public void The_text_is_asked_in_physical_pixels_and_measured_back_in_design_units()
    {
        using var bitmap = new Bitmap(100, 60);
        var text = new FixedText();
        using var canvas = new PixelCanvas(bitmap.Bits, bitmap.Width, bitmap.Height, 1.5, text);

        canvas.SetOrigin(-10);
        canvas.Text("ab c", Face.Mono, 12, 4, 20, 0xABCDEF);

        var drawn = Assert.Single(text.Drawn);
        Assert.Equal(new FontSpec(Face.Mono, 18), drawn.Font);
        Assert.Equal(("ab c", 6f, 15f, 0xABCDEFu), (drawn.Text, drawn.X, drawn.Top, drawn.Rgb));
        Assert.True(text.Target == bitmap.Bits);
        Assert.Equal(0xABCDEFu, bitmap.At(6, 15) & 0xFFFFFF);
        Assert.Equal(4 * 0.6f * 18 / 1.5f, canvas.Measure("ab c", Face.Mono, 12), 3);
        Assert.Equal(MathF.Round(1.25f * 18) / 1.5f, canvas.LineHeight(Face.Mono, 12), 3);
    }

    [Fact]
    public void Dim_darkens_the_colours_and_keeps_the_alpha()
    {
        using var bitmap = new Bitmap(3, 1);
        using var canvas = new PixelCanvas(bitmap.Bits, bitmap.Width, bitmap.Height, 1, new FixedText());
        canvas.FillRect(new Box(0, 0, 3, 1), 0xFFFFFF);
        var alpha = bitmap.At(0, 0) & 0xFF000000;

        canvas.Dim(0.5f);

        Assert.Equal(alpha | 0x7F7F7F, bitmap.At(0, 0));
        Assert.Equal(alpha | 0x7F7F7F, bitmap.At(2, 0));
    }

    [Fact]
    public void A_square_polygon_fills_its_pixels_whole()
    {
        using var bitmap = new Bitmap(10, 10);
        using var canvas = new PixelCanvas(bitmap.Bits, bitmap.Width, bitmap.Height, 1, new FixedText());

        canvas.FillPolygon([(2, 2), (6, 2), (6, 6), (2, 6)], 0x4080C0);

        Assert.Equal(0x4080C0u, bitmap.At(2, 2) & 0xFFFFFF);
        Assert.Equal(0x4080C0u, bitmap.At(5, 5) & 0xFFFFFF);
        Assert.Equal(0u, bitmap.At(6, 6) & 0xFFFFFF);
        Assert.Equal(0u, bitmap.At(1, 3) & 0xFFFFFF);
    }

    [Fact]
    public void Disposing_the_canvas_disposes_its_text_and_leaves_the_bitmap()
    {
        using var bitmap = new Bitmap(4, 4);
        var text = new FixedText();
        var canvas = new PixelCanvas(bitmap.Bits, bitmap.Width, bitmap.Height, 1, text);
        canvas.FillRect(new Box(0, 0, 4, 4), 0x0D0F13);

        canvas.Dispose();

        Assert.True(text.Disposed);
        Assert.Equal(0x0D0F13u, bitmap.At(3, 3) & 0xFFFFFF);
    }
}
