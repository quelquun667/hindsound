namespace Hindsound.Ui;

/// <summary>Pure-managed drawing into a premultiplied BGRA buffer: analytic coverage for shapes, a triple box blur for the glows.</summary>
internal readonly unsafe struct Pixels(uint* origin, int width, int height)
{
    public uint* Origin { get; } = origin;
    public int Width { get; } = width;
    public int Height { get; } = height;

    public uint* Row(int y) => Origin + y * Width;

    /// <summary>Lays one opaque colour over the pixel with the given coverage, keeping the buffer premultiplied.</summary>
    public static void Blend(uint* pixel, uint rgb, float coverage)
    {
        if (coverage <= 0)
        {
            return;
        }

        if (coverage >= 1)
        {
            *pixel = 0xFF000000 | rgb;
            return;
        }

        var cover = (uint)(coverage * 256);
        var keep = 256 - cover;
        var dst = *pixel;
        var a = ((0xFFu * cover) + ((dst >> 24) * keep)) >> 8;
        var r = ((((rgb >> 16) & 0xFF) * cover) + (((dst >> 16) & 0xFF) * keep)) >> 8;
        var g = ((((rgb >> 8) & 0xFF) * cover) + (((dst >> 8) & 0xFF) * keep)) >> 8;
        var b = (((rgb & 0xFF) * cover) + ((dst & 0xFF) * keep)) >> 8;
        *pixel = (a << 24) | (r << 16) | (g << 8) | b;
    }

    public void FillRect(int left, int top, int right, int bottom, uint rgb)
    {
        for (var y = Math.Max(0, top); y < Math.Min(Height, bottom); y++)
        {
            var row = Row(y);
            for (var x = Math.Max(0, left); x < Math.Min(Width, right); x++)
            {
                row[x] = 0xFF000000 | rgb;
            }
        }
    }

    /// <summary>The tab's outline: the left, top and bottom edges plus the 45-degree cut corner, inset by the given distance.</summary>
    public void FillTab(float inset, float cut, uint rgb)
    {
        var diagonal = cut + inset * MathF.Sqrt(2);
        for (var y = 0; y < Height; y++)
        {
            var row = Row(y);
            for (var x = 0; x < Width; x++)
            {
                var cover = MathF.Min(Clamp(x + 1 - inset), Clamp(y + 1 - inset));
                cover = MathF.Min(cover, Clamp(Height - inset - y));
                cover = MathF.Min(cover, Clamp((x + 0.5f + y + 0.5f - diagonal) / MathF.Sqrt(2) + 0.5f));
                Blend(row + x, rgb, cover);
            }
        }
    }

    /// <summary>Flips the buffer left to right: the tab of a notification on the left edge of the screen.</summary>
    public void MirrorHorizontally()
    {
        for (var y = 0; y < Height; y++)
        {
            new Span<uint>(Row(y), Width).Reverse();
        }
    }

    public static float RoundRectCoverage(float px, float py, float left, float top, float right, float bottom, float radius)
    {
        var distance = Sdf.RoundRect(px, py, left, top, right, bottom, radius);
        return Clamp(0.5f - distance);
    }

    public void FillRoundRect(float left, float top, float right, float bottom, float radius, uint rgb)
    {
        var opaque = 0xFF000000 | rgb;
        var startX = Math.Max(0, (int)left);
        var stopX = Math.Min(Width, (int)MathF.Ceiling(right));
        if (stopX <= startX)
        {
            return;
        }

        for (var y = Math.Max(0, (int)top); y < Math.Min(Height, (int)MathF.Ceiling(bottom)); y++)
        {
            var row = Row(y);

            // A pixel a full pixel away from the edges and off the corners is simply covered: only the rim needs its distance worked
            // out. Same pixels, and a lane of the live wave stops costing a square root per pixel at each repaint.
            var (solidFrom, solidTo) = SolidSpan(y + 0.5f, left, top, right, bottom, radius);
            solidFrom = Math.Clamp(solidFrom, startX, stopX);
            solidTo = Math.Clamp(solidTo, solidFrom, stopX);
            for (var x = startX; x < solidFrom; x++)
            {
                Blend(row + x, rgb, RoundRectCoverage(x + 0.5f, y + 0.5f, left, top, right, bottom, radius));
            }

            new Span<uint>(row + solidFrom, solidTo - solidFrom).Fill(opaque);
            for (var x = solidTo; x < stopX; x++)
            {
                Blend(row + x, rgb, RoundRectCoverage(x + 0.5f, y + 0.5f, left, top, right, bottom, radius));
            }
        }
    }

    private static (int From, int To) SolidSpan(float py, float left, float top, float right, float bottom, float radius)
    {
        const float clear = 1;
        var above = py - top;
        var below = bottom - py;
        if (above < clear || below < clear)
        {
            return (0, 0);
        }

        var inset = above >= radius + clear && below >= radius + clear ? clear : radius + clear;
        var from = (int)MathF.Ceiling(left + inset - 0.5f);
        var to = (int)MathF.Floor(right - inset - 0.5f) + 1;
        return from < to ? (from, to) : (0, 0);
    }

    /// <summary>Adds a blurred copy of the mask, in the given colour, over the buffer. Transparent pixels stay transparent.</summary>
    public void Glow(byte* mask, int maskLeft, int maskTop, int maskWidth, int maskHeight, float sigma, float opacity, uint rgb)
    {
        BoxBlur.Run(mask, maskWidth, maskHeight, sigma);
        for (var y = 0; y < maskHeight; y++)
        {
            var targetY = y + maskTop;
            if (targetY < 0 || targetY >= Height)
            {
                continue;
            }

            var row = Row(targetY);
            for (var x = 0; x < maskWidth; x++)
            {
                var targetX = x + maskLeft;
                if (targetX < 0 || targetX >= Width)
                {
                    continue;
                }

                var value = mask[y * maskWidth + x];
                var alpha = row[targetX] >> 24;
                if (value != 0 && alpha != 0)
                {
                    var cover = value / 255f * opacity * alpha / 255f;
                    var keepAlpha = row[targetX] & 0xFF000000;
                    Blend(row + targetX, rgb, cover);
                    row[targetX] = (row[targetX] & 0x00FFFFFF) | keepAlpha;
                }
            }
        }
    }

    public static float Clamp(float value) => value < 0 ? 0 : value > 1 ? 1 : value;
}

/// <summary>Signed distances, negative inside. One pixel of antialiasing is <c>0.5 - distance</c> in pixels.</summary>
internal static class Sdf
{
    public static float RoundRect(float px, float py, float left, float top, float right, float bottom, float radius)
    {
        var cx = (left + right) / 2;
        var cy = (top + bottom) / 2;
        var dx = MathF.Abs(px - cx) - ((right - left) / 2 - radius);
        var dy = MathF.Abs(py - cy) - ((bottom - top) / 2 - radius);
        var ox = MathF.Max(dx, 0);
        var oy = MathF.Max(dy, 0);
        return MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(dx, dy), 0) - radius;
    }

    /// <summary>A line of the given width with round ends.</summary>
    public static float Segment(float px, float py, float ax, float ay, float bx, float by, float width)
    {
        var vx = bx - ax;
        var vy = by - ay;
        var t = Pixels.Clamp(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
        var dx = px - ax - t * vx;
        var dy = py - ay - t * vy;
        return MathF.Sqrt(dx * dx + dy * dy) - width / 2;
    }

    public static float Circle(float px, float py, float cx, float cy, float radius)
    {
        var dx = px - cx;
        var dy = py - cy;
        return MathF.Sqrt(dx * dx + dy * dy) - radius;
    }
}

internal static unsafe class BoxBlur
{
    /// <summary>Three box passes each way approach a gaussian of the given sigma.</summary>
    public static void Run(byte* mask, int width, int height, float sigma)
    {
        var radius = Math.Max(1, (int)MathF.Round((MathF.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
        var scratch = new byte[Math.Max(width, height)];
        fixed (byte* line = scratch)
        {
            for (var pass = 0; pass < 3; pass++)
            {
                for (var y = 0; y < height; y++)
                {
                    Line(mask + y * width, 1, width, radius, line);
                }

                for (var x = 0; x < width; x++)
                {
                    Line(mask + x, width, height, radius, line);
                }
            }
        }
    }

    private static void Line(byte* data, int step, int count, int radius, byte* scratch)
    {
        var window = (radius * 2) + 1;
        var sum = 0;
        for (var i = -radius; i <= radius; i++)
        {
            sum += i >= 0 && i < count ? data[i * step] : 0;
        }

        for (var i = 0; i < count; i++)
        {
            scratch[i] = (byte)(sum / window);
            var add = i + radius + 1;
            var remove = i - radius;
            sum += (add < count ? data[add * step] : 0) - (remove >= 0 ? data[remove * step] : 0);
        }

        for (var i = 0; i < count; i++)
        {
            data[i * step] = scratch[i];
        }
    }
}
