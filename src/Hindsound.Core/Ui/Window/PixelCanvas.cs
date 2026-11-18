using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hindsound.Audio;

namespace Hindsound.Ui.Window;

/// <summary>
/// The window drawn in managed code on a bitmap of 32-bit BGRA pixels, opaque, the first row at the top: the shapes, the bars, the
/// dimming and the glow are drawn here, the text by an <see cref="ICanvasText"/>. The bitmap belongs to whoever made the canvas
/// (a DIB on Windows, the memory of a window elsewhere): the canvas only draws into it, and never frees it. The text is the
/// canvas's own, disposed with it.
/// </summary>
internal unsafe class PixelCanvas : ICanvas, IDisposable
{
    private readonly Pixels _pixels;
    private readonly float _s;
    private readonly int _line;
    private float _originY;

    /// <param name="bits">The first pixel of the top row; the rows follow each other with no gap, <paramref name="width"/> pixels each.</param>
    /// <param name="width">The width of the bitmap, in physical pixels.</param>
    /// <param name="height">The height of the bitmap, in physical pixels.</param>
    /// <param name="scale">Physical pixels per design unit.</param>
    /// <param name="text">What measures and draws the text, in physical pixels; the canvas disposes it.</param>
    public PixelCanvas(uint* bits, int width, int height, double scale, ICanvasText text)
        : this(new Pixels(bits, width, height), scale, text)
    {
    }

    /// <param name="pixels">The bitmap to draw into, which stays its maker's.</param>
    /// <param name="scale">Physical pixels per design unit.</param>
    /// <param name="text">What measures and draws the text, in physical pixels; the canvas disposes it.</param>
    public PixelCanvas(Pixels pixels, double scale, ICanvasText text)
    {
        _pixels = pixels;
        Width = pixels.Width;
        Height = pixels.Height;
        _s = (float)scale;
        _line = Math.Max(1, (int)_s);
        Typesetter = text;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The bitmap drawn into.</summary>
    public Pixels Pixels => _pixels;

    public uint* Bits => _pixels.Origin;

    /// <summary>What measures and draws the text.</summary>
    protected ICanvasText Typesetter { get; }

    /// <summary>The scale as the drawing uses it: physical pixels per design unit, as a float.</summary>
    protected float Factor => _s;

    public void SetOrigin(float dy) => _originY = dy;

    public double Scale => _s;

    protected int X(float x) => (int)MathF.Round(x * _s);

    protected int Y(float y) => (int)MathF.Round((y + _originY) * _s);

    public void FillRect(Box box, uint rgb) => _pixels.FillRect(X(box.X), Y(box.Y), X(box.Right), Y(box.Bottom), rgb);

    public void FillRound(Box box, float radius, uint rgb) =>
        _pixels.FillRoundRect(X(box.X), Y(box.Y), X(box.Right), Y(box.Bottom), MathF.Max(0.01f, radius * _s), rgb);

    public void Frame(Box box, float radius, uint fill, uint border)
    {
        float left = X(box.X), top = Y(box.Y), right = X(box.Right), bottom = Y(box.Bottom);
        var r = radius * _s;
        _pixels.FillRoundRect(left, top, right, bottom, MathF.Max(0.01f, r), border);
        _pixels.FillRoundRect(left + _line, top + _line, right - _line, bottom - _line, MathF.Max(0.01f, r - _line), fill);
    }

    public void Ring(Box box, float radius, float thickness, uint rgb)
    {
        float left = box.X * _s, top = (box.Y + _originY) * _s, right = box.Right * _s, bottom = (box.Bottom + _originY) * _s;
        var half = thickness * _s / 2;
        var r = radius * _s;
        Cover((int)(left - half - 1), (int)(top - half - 1), (int)(right + half + 2), (int)(bottom + half + 2), rgb, (px, py) =>
            Pixels.Clamp(0.5f - (MathF.Abs(Sdf.RoundRect(px, py, left, top, right, bottom, r)) - half)));
    }

    public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb)
    {
        float x1 = ax * _s, y1 = (ay + _originY) * _s, x2 = bx * _s, y2 = (by + _originY) * _s, w = width * _s;
        Cover((int)(MathF.Min(x1, x2) - w - 1), (int)(MathF.Min(y1, y2) - w - 1), (int)(MathF.Max(x1, x2) + w + 2), (int)(MathF.Max(y1, y2) + w + 2), rgb, (px, py) =>
            Pixels.Clamp(0.5f - Sdf.Segment(px, py, x1, y1, x2, y2, w)));
    }

    // A lane is about a hundred tall, thin bars. Drawn bar by bar, each one reaches into a different cache line on every row of
    // the bitmap and the next bar loads the same lines again; drawn row by row, the bitmap is walked once, top to bottom. Same
    // pixels either way: the halos only ever fill the gap beside their own bar, and the gap holds both neighbours' halos.
    private const int BarsPerPass = 512;

    public void Bars(in WaveGrid grid, ReadOnlySpan<byte> levels, ReadOnlySpan<BarColumn> columns, uint baseline)
    {
        var count = Math.Min(Math.Min(levels.Length, columns.Length), grid.Columns);
        var mid = grid.Mid + (int)MathF.Round(_originY * _s);
        Span<int> reach = stackalloc int[Math.Min(count, BarsPerPass)];
        for (var first = 0; first < count; first += BarsPerPass)
        {
            BarsOf(grid, levels, columns, baseline, mid, first, Math.Min(count, first + BarsPerPass), count, reach);
        }
    }

    private void BarsOf(in WaveGrid grid, ReadOnlySpan<byte> levels, ReadOnlySpan<BarColumn> columns, uint baseline, int mid, int first, int last, int count, Span<int> reach)
    {
        var tallest = 0;
        var anyHalo = false;
        for (var i = first; i < last; i++)
        {
            var level = levels[i];
            if (level == WaveLane.Empty)
            {
                reach[i - first] = -1;
                continue;
            }

            if (level == 0)
            {
                // A run of silence is one line: it reaches into the gap only when the next column is silent too.
                var x = grid.X(i);
                var right = x + (i + 1 < count && levels[i + 1] == 0 ? grid.Pitch : grid.Bar);
                Fill(x, mid, right, mid + grid.Line, baseline);
                reach[i - first] = -1;
                continue;
            }

            var h = Math.Max(1, (int)MathF.Round(Math.Min(level, WaveLane.FullLevel) / (float)WaveLane.FullLevel * grid.Half));
            reach[i - first] = h;
            tallest = Math.Max(tallest, h);
            anyHalo |= columns[i].HasHalo;
        }

        if (tallest == 0)
        {
            return;
        }

        var edge = anyHalo ? grid.Edge : 0;
        var stop = Math.Min(Height, mid + tallest + grid.Line + edge);
        for (var y = Math.Max(0, mid - tallest - edge); y < stop; y++)
        {
            var row = _pixels.Row(y);
            for (var i = first; i < last; i++)
            {
                var h = reach[i - first];
                if (h < 0)
                {
                    continue;
                }

                int top = mid - h, bottom = mid + h + grid.Line, x = grid.X(i);
                var column = columns[i];
                if (y >= top && y < bottom)
                {
                    if (column.HasHalo)
                    {
                        // A cross, not a box: the corners stay clear, which keeps the bars looking round-edged and separate.
                        Put(row, x - grid.Edge, x, column.Halo);
                        Put(row, x + grid.Bar, x + grid.Bar + grid.Edge, column.Halo);
                    }

                    Put(row, x, x + grid.Bar, column.Color);
                }
                else if (column.HasHalo && y >= top - grid.Edge && y < bottom + grid.Edge)
                {
                    Put(row, x, x + grid.Bar, column.Halo);
                }
            }
        }
    }

    // A bar is a few pixels wide: a plain loop, with no call to Fill.
    private void Put(uint* row, int left, int right, uint rgb)
    {
        var value = 0xFF000000 | rgb;
        for (var x = Math.Max(0, left); x < Math.Min(Width, right); x++)
        {
            row[x] = value;
        }
    }

    private void Fill(int left, int top, int right, int bottom, uint rgb)
    {
        for (var y = Math.Max(0, top); y < Math.Min(Height, bottom); y++)
        {
            Put(_pixels.Row(y), left, right, rgb);
        }
    }

    private void Cover(int left, int top, int right, int bottom, uint rgb, Func<float, float, float> coverage)
    {
        for (var y = Math.Max(0, top); y < Math.Min(Height, bottom); y++)
        {
            var row = _pixels.Row(y);
            for (var x = Math.Max(0, left); x < Math.Min(Width, right); x++)
            {
                Pixels.Blend(row + x, rgb, coverage(x + 0.5f, y + 0.5f));
            }
        }
    }

    public void Dim(float opacity)
    {
        // Nothing to darken, and a NaN counts as nothing: no walk over the whole window for it.
        if (!(opacity > 0))
        {
            return;
        }

        // What is left of each colour is keep / 256, for the three colours of two pixels at once. Red and blue sit two bytes apart,
        // with a byte of nothing between them, so one multiplication does both; the product of a byte and a number up to 256 is
        // at most 0xFF00, which never reaches into the next field. The alpha bytes are left as they are.
        const ulong RedBlue = 0x00FF00FF00FF00FF, Green = 0x0000FF000000FF00, Alpha = 0xFF000000FF000000;
        var keep = 256 - (ulong)MathF.Round(Math.Min(opacity, 1f) * 256);
        var count = (long)Width * Height;
        var pixel = _pixels.Origin;
        var pairs = pixel + (count & ~1L);
        for (; pixel < pairs; pixel += 2)
        {
            var both = Unsafe.ReadUnaligned<ulong>(pixel);
            var dimmed = (both & Alpha) | (((both & RedBlue) * keep >> 8) & RedBlue) | (((both & Green) * keep >> 8) & Green);
            Unsafe.WriteUnaligned(pixel, dimmed);
        }

        if ((count & 1) != 0)
        {
            var value = *pixel;
            *pixel = (value & 0xFF000000) | (uint)(((value & 0x00FF00FF) * keep >> 8) & 0x00FF00FF) | (uint)(((value & 0x0000FF00) * keep >> 8) & 0x0000FF00);
        }
    }

    // Eight rows of samples in each row of pixels, each with the exact width of what lies inside: the edges of a star are smooth
    // from the first corner on, and a pixel costs one pass over the edges per sample row, never one per sample.
    private const int SampleRows = 8;

    // A star has ten corners: a longer outline gets its working space from the heap, once per call.
    private const int OnTheStack = 64;

    // A shape wider than this on the bitmap gets its row of coverage from the heap, once per call.
    private const int CoverOnTheStack = 512;

    public void FillPolygon(ReadOnlySpan<(float X, float Y)> points, uint rgb)
    {
        var count = points.Length;
        if (count < 3)
        {
            return;
        }

        Span<float> work = count <= OnTheStack ? stackalloc float[count * 3] : new float[count * 3];
        Span<float> xs = work[..count], ys = work.Slice(count, count), crossings = work.Slice(2 * count, count);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (var i = 0; i < count; i++)
        {
            xs[i] = points[i].X * _s;
            ys[i] = (points[i].Y + _originY) * _s;
            minX = MathF.Min(minX, xs[i]);
            maxX = MathF.Max(maxX, xs[i]);
            minY = MathF.Min(minY, ys[i]);
            maxY = MathF.Max(maxY, ys[i]);
        }

        // Written so that a NaN corner ends in an empty range, not in a loop over the whole bitmap.
        if (!(maxX > minX) || !(maxY > minY))
        {
            return;
        }

        // Clamped as floats first: a corner far off the bitmap must not overflow the conversion.
        var left = (int)Math.Clamp(MathF.Floor(minX), 0f, Width);
        var right = (int)Math.Clamp(MathF.Ceiling(maxX), 0f, Width);
        var top = (int)Math.Clamp(MathF.Floor(minY), 0f, Height);
        var bottom = (int)Math.Clamp(MathF.Ceiling(maxY), 0f, Height);
        var width = right - left;
        if (width <= 0 || top >= bottom)
        {
            return;
        }

        Span<float> cover = width <= CoverOnTheStack ? stackalloc float[width] : new float[width];
        for (var y = top; y < bottom; y++)
        {
            cover.Clear();
            for (var sample = 0; sample < SampleRows; sample++)
            {
                var at = y + (sample + 0.5f) / SampleRows;
                var crossed = 0;
                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    if (ys[i] > at != ys[j] > at)
                    {
                        crossings[crossed++] = xs[i] + (at - ys[i]) * (xs[j] - xs[i]) / (ys[j] - ys[i]);
                    }
                }

                // The crossings are in pairs, once in and once out, but not in order: an insertion sort of a few numbers.
                for (var i = 1; i < crossed; i++)
                {
                    var value = crossings[i];
                    var k = i - 1;
                    while (k >= 0 && crossings[k] > value)
                    {
                        crossings[k + 1] = crossings[k];
                        k--;
                    }

                    crossings[k + 1] = value;
                }

                for (var i = 0; i + 1 < crossed; i += 2)
                {
                    AddSpan(cover, crossings[i] - left, crossings[i + 1] - left);
                }
            }

            var row = _pixels.Row(y) + left;
            for (var x = 0; x < width; x++)
            {
                Pixels.Blend(row + x, rgb, cover[x] / SampleRows);
            }
        }
    }

    /// <summary>Adds the part of each pixel that the span from <paramref name="from"/> to <paramref name="to"/> covers.</summary>
    private static void AddSpan(Span<float> cover, float from, float to)
    {
        from = MathF.Max(from, 0f);
        to = MathF.Min(to, cover.Length);
        if (!(to > from))
        {
            return;
        }

        var first = (int)from;
        var last = (int)to;
        if (first == last)
        {
            cover[first] += to - from;
            return;
        }

        cover[first] += first + 1 - from;
        for (var x = first + 1; x < last; x++)
        {
            cover[x] += 1;
        }

        if (last < cover.Length)
        {
            cover[last] += to - last;
        }
    }

    public void Glow(Box box, float sigma, float opacity, uint rgb)
    {
        var scaled = sigma * _s;
        var pad = (int)MathF.Ceiling(scaled * 3);
        int left = X(box.X), top = Y(box.Y), width = X(box.Right) - left, height = Y(box.Bottom) - top;
        if (top + height + pad < 0 || top - pad > Height)
        {
            return;
        }

        var maskWidth = width + 2 * pad;
        var maskHeight = height + 2 * pad;
        var mask = (byte*)NativeMemory.AllocZeroed((nuint)(maskWidth * maskHeight));
        for (var y = pad; y < pad + height; y++)
        {
            new Span<byte>(mask + y * maskWidth + pad, width).Fill(255);
        }

        _pixels.Glow(mask, left - pad, top - pad, maskWidth, maskHeight, scaled, opacity, rgb);
        NativeMemory.Free(mask);
    }

    private FontSpec Font(Face face, float size) => new(face, size * _s);

    public void Text(string text, Face face, float size, float x, float top, uint rgb) =>
        Typesetter.Draw(_pixels, Font(face, size), text, x * _s, (top + _originY) * _s, rgb);

    public float Measure(string text, Face face, float size) => Typesetter.Measure(Font(face, size), text) / _s;

    public float LineHeight(Face face, float size) => Typesetter.LineHeight(Font(face, size)) / _s;

    public void WarningIcon(float x, float y, float size, uint rgb) =>
        NotificationIcons.Draw(_pixels, NotificationVisual.Triangle, rgb, x * _s, (y + _originY) * _s, _s * size / 24);

    /// <summary>Draws nothing here: the notification is drawn by the platform's own code, which overrides this.</summary>
    public virtual void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored)
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes the text; a platform that made the bitmap frees it after calling this.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Typesetter.Dispose();
        }
    }
}
