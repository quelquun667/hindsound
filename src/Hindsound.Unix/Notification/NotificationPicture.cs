using System.Runtime.InteropServices;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Unix.Notification;

/// <summary>
/// One notification drawn in managed code, as the Windows app draws it (NotificationBitmap there), on a bitmap of premultiplied
/// BGRA pixels whose cut corner is transparent: the tab, its stripe, the bars or the icon, the title, the body and the keys.
/// The shapes are drawn by <see cref="Pixels"/>, the text by an <see cref="ICanvasText"/> (FreeType on Linux and macOS), so
/// any system with a window that takes an alpha channel can show it. The bars are kept as a strip, copied back row by row at
/// another height while they rise (<see cref="SetBarsScale"/>).
/// </summary>
internal sealed unsafe class NotificationPicture : IDisposable
{
    /// <summary>The size of the notification in design units (points on macOS).</summary>
    public const int DesignWidth = 360, DesignHeight = 64;

    private const float TabCut = 14, StripeWidth = 4, IconSize = 24, BarsWidth = 96, Margin = 14, Gap = 12;

    private readonly Pixels _pixels;
    private readonly bool _ownsPixels;
    private readonly uint* _strip;
    private readonly NotificationContent _content;
    private readonly uint _accent;
    private readonly bool _mirrored;
    private readonly int _stripLeft, _stripTop, _stripWidth, _stripHeight;
    private float _barsScale = -1;
    private bool _disposed;

    /// <summary>Draws on a bitmap of its own, freed with it. <paramref name="text"/> stays the caller's.</summary>
    /// <param name="scale">Physical pixels per design unit.</param>
    /// <param name="accent">The accent colour, for the bars and their glow.</param>
    /// <param name="mirrored">On the left edge of the screen: the tab is flipped, the text still reads left to right.</param>
    public NotificationPicture(NotificationContent content, double scale, uint accent, bool mirrored, ICanvasText text)
        : this(NewPixels(scale), true, content, scale, accent, mirrored, text)
    {
    }

    /// <summary>
    /// Draws on <paramref name="target"/>, which stays the caller's: <see cref="WidthAt"/> by <see cref="HeightAt"/> pixels, all
    /// transparent (zero). The Windows text can only draw on the bitmap of its DC: the tests that set this picture against the
    /// Windows one hand it that bitmap.
    /// </summary>
    public NotificationPicture(Pixels target, NotificationContent content, double scale, uint accent, bool mirrored, ICanvasText text)
        : this(target, false, content, scale, accent, mirrored, text)
    {
    }

    private NotificationPicture(Pixels pixels, bool owns, NotificationContent content, double scale, uint accent, bool mirrored, ICanvasText text)
    {
        _pixels = pixels;
        _ownsPixels = owns;
        if (pixels.Width != WidthAt(scale) || pixels.Height != HeightAt(scale))
        {
            Dispose();
            throw new ArgumentException($"A notification at scale {scale} is {WidthAt(scale)} by {HeightAt(scale)} pixels.", nameof(pixels));
        }

        _content = content;
        _accent = accent;
        _mirrored = mirrored;
        var s = (float)scale;
        var visualLeft = VisualLeft(content, mirrored);
        _stripLeft = (int)MathF.Floor((visualLeft - 5) * s);
        _stripTop = (int)MathF.Floor(7 * s);
        _stripWidth = (int)MathF.Ceiling((visualLeft + 101) * s) - _stripLeft;
        _stripHeight = Height - 2 * _stripTop;
        _strip = content.HasBars ? (uint*)NativeMemory.Alloc((nuint)(_stripWidth * _stripHeight * sizeof(uint))) : null;

        Draw(s, visualLeft, text);
    }

    public int Width => _pixels.Width;

    public int Height => _pixels.Height;

    /// <summary>The bitmap, premultiplied BGRA, the first row at the top.</summary>
    public Pixels Pixels => _pixels;

    /// <summary>What it shows.</summary>
    public NotificationContent Content => _content;

    /// <summary>Where the bars are redrawn while they rise, in pixels of the bitmap.</summary>
    public (int X, int Y, int W, int H) BarsRect => (_stripLeft, _stripTop, _stripWidth, _stripHeight);

    public static int WidthAt(double scale) => (int)Math.Round(DesignWidth * scale);

    public static int HeightAt(double scale) => (int)Math.Round(DesignHeight * scale);

    /// <summary>Design units from the left of the bitmap to the bars or the icon.</summary>
    public static float VisualLeft(NotificationContent content, bool mirrored) =>
        mirrored ? DesignWidth - Margin - (content.HasBars ? BarsWidth : IconSize) : Margin;

    /// <summary>Design units from the left of the bitmap to the text.</summary>
    public static float TextLeft(NotificationContent content, bool mirrored) =>
        mirrored ? StripeWidth + Margin : Margin + (content.HasBars ? BarsWidth : IconSize) + Gap;

    /// <summary>Width of the text in design units: what is left between the visual and the stripe.</summary>
    public static float TextWidth(NotificationContent content) =>
        DesignWidth - Margin - (content.HasBars ? BarsWidth : IconSize) - Gap - Margin - StripeWidth;

    /// <summary>The text, cut with an ellipsis if it would run past the width: the rule of the Windows text.</summary>
    public static string Fit(ICanvasText text, FontSpec font, string value, float width)
    {
        if (text.Measure(font, value) <= width)
        {
            return value;
        }

        for (var length = value.Length - 1; length > 0; length--)
        {
            var cut = value[..length].TrimEnd() + "...";
            if (text.Measure(font, cut) <= width)
            {
                return cut;
            }
        }

        return string.Empty;
    }

    /// <summary>What the second row really says, once the keys have taken their room.</summary>
    public static string BodyShown(NotificationContent content, ICanvasText text, FontSpec bodyFont, FontSpec keyFont, float textWidth, float s) =>
        content.Keys.Count > 0
            ? BodyBeforeKeys(content, text, bodyFont, textWidth - KeysWidth(content, text, keyFont, s))
            : Fit(text, bodyFont, content.Body, textWidth);

    // A long shortcut wins over the sentence: the keys are the useful part of the row.
    private static string BodyBeforeKeys(NotificationContent content, ICanvasText text, FontSpec font, float width)
    {
        if (text.Measure(font, content.Body) <= width)
        {
            return content.Body;
        }

        var lastSentence = content.Body[(content.Body.LastIndexOf(' ') + 1)..];
        return text.Measure(font, lastSentence) <= width ? lastSentence : string.Empty;
    }

    private static float KeysWidth(NotificationContent content, ICanvasText text, FontSpec font, float s)
    {
        var width = 6 * s;
        foreach (var key in content.Keys)
        {
            width += MathF.Round(text.Measure(font, key) + 2 * (KeyPaddingOf(content) * s + s)) + MathF.Round(4 * s);
        }

        return width;
    }

    private static float KeyPaddingOf(NotificationContent content) => content.SmallKeys ? 4 : 6;

    private static Pixels NewPixels(double scale)
    {
        int width = WidthAt(scale), height = HeightAt(scale);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale of a notification is positive.");
        }

        return new Pixels((uint*)NativeMemory.AllocZeroed((nuint)(width * height), sizeof(uint)), width, height);
    }

    private void Draw(float s, float visualLeft, ICanvasText text)
    {
        _pixels.FillTab(0, TabCut * s, NotificationPalette.Border);
        _pixels.FillTab(s, TabCut * s, NotificationPalette.Fill);
        DrawStripe(s);
        if (_mirrored)
        {
            _pixels.MirrorHorizontally();
        }

        if (_content.HasBars)
        {
            DrawBars(s, visualLeft);
        }
        else
        {
            NotificationIcons.Draw(_pixels, _content.Visual, _content.VisualColor, visualLeft * s, 20 * s, s);
        }

        var titleFont = new FontSpec(Face.Title, 19 * s);
        var bodyFont = new FontSpec(Face.Body, 13 * s);
        var keyFont = new FontSpec(Face.Mono, (_content.SmallKeys ? 10 : 12) * s);

        var textLeft = TextLeft(_content, _mirrored) * s;
        var textWidth = TextWidth(_content) * s;
        text.Draw(_pixels, titleFont, Fit(text, titleFont, _content.Title, textWidth), textLeft, 8.7f * s, NotificationPalette.Title);

        var rowTop = MathF.Round(34 * s);
        var rowHeight = MathF.Round(20 * s);
        var body = BodyShown(_content, text, bodyFont, keyFont, textWidth, s);
        text.Draw(_pixels, bodyFont, body, textLeft, rowTop + (rowHeight - text.LineHeight(bodyFont)) / 2, NotificationPalette.Muted);

        var x = MathF.Round(textLeft + text.Measure(bodyFont, body) + 6 * s);
        for (var i = 0; i < _content.Keys.Count; i++)
        {
            x = DrawKey(text, keyFont, _content.Keys[i], x, rowTop, rowHeight, s) + MathF.Round(4 * s);
        }
    }

    private float DrawKey(ICanvasText text, FontSpec font, string key, float left, float rowTop, float rowHeight, float s)
    {
        var height = _content.SmallKeys ? MathF.Round(16 * s) : rowHeight;
        var top = rowTop + MathF.Floor((rowHeight - height) / 2);
        var textWidth = text.Measure(font, key);
        var right = left + MathF.Round(textWidth + 2 * (KeyPaddingOf(_content) * s + s));
        var radius = 3 * s;
        _pixels.FillRoundRect(left, top, right, top + height, radius, NotificationPalette.CapBorder);
        _pixels.FillRoundRect(left + s, top + s, right - s, top + height - s, MathF.Max(0, radius - s), NotificationPalette.CapFill);
        text.Draw(_pixels, font, key, left + (right - left - textWidth) / 2, top + (height - text.LineHeight(font)) / 2, NotificationPalette.Title);
        return right;
    }

    private void DrawStripe(float s)
    {
        var stripeLeft = Width - (int)MathF.Round(StripeWidth * s);
        if (_content.StripeGlows)
        {
            var sigma = 20 * s / 3;
            var pad = (int)MathF.Ceiling(sigma * 3);
            var maskWidth = Width - stripeLeft + 2 * pad;
            var maskHeight = Height + 2 * pad;
            var mask = (byte*)NativeMemory.AllocZeroed((nuint)(maskWidth * maskHeight));
            for (var y = pad; y < pad + Height; y++)
            {
                new Span<byte>(mask + y * maskWidth + pad, Width - stripeLeft).Fill(255);
            }

            _pixels.Glow(mask, stripeLeft - pad, -pad, maskWidth, maskHeight, sigma, 0.7f, _content.Stripe);
            NativeMemory.Free(mask);
        }

        _pixels.FillRect(stripeLeft, 0, Width, Height, _content.Stripe);
    }

    private void DrawBars(float s, float visualLeft)
    {
        var columns = _content.Bars;
        var mask = (byte*)NativeMemory.AllocZeroed((nuint)(_stripWidth * _stripHeight));
        var bars = new (int Left, int Top, int Right, int Bottom)[columns.Count];
        var anyActive = false;
        for (var i = 0; i < columns.Count; i++)
        {
            var left = (int)MathF.Floor((visualLeft + 1 + i * 4) * s + 0.5f);
            var right = left + (int)MathF.Round(2 * s);
            var height = (int)MathF.Round(columns[i].Height * s);
            var top = (Height - height) / 2;
            bars[i] = (left, top, right, top + height);
            if (!columns[i].Active)
            {
                continue;
            }

            anyActive = true;
            for (var y = top; y < top + height; y++)
            {
                var row = mask + (y - _stripTop) * _stripWidth - _stripLeft;
                for (var x = left; x < right; x++)
                {
                    row[x] = (byte)(255 * Pixels.RoundRectCoverage(x + 0.5f, y + 0.5f, left, top, right, top + height, s));
                }
            }
        }

        if (anyActive)
        {
            _pixels.Glow(mask, _stripLeft, _stripTop, _stripWidth, _stripHeight, 6 * s / 3, 0.55f, _accent);
        }

        NativeMemory.Free(mask);
        for (var i = 0; i < columns.Count; i++)
        {
            var bar = bars[i];
            _pixels.FillRoundRect(bar.Left, bar.Top, bar.Right, bar.Bottom, s, columns[i].Active ? _accent : NotificationPalette.Faint);
        }

        for (var y = 0; y < _stripHeight; y++)
        {
            new ReadOnlySpan<uint>(_pixels.Row(y + _stripTop) + _stripLeft, _stripWidth).CopyTo(new Span<uint>(_strip + y * _stripWidth, _stripWidth));
        }
    }

    /// <summary>Redraws the bars at the given vertical scale, centred. Returns false when nothing changed.</summary>
    public bool SetBarsScale(float scale)
    {
        if (_strip == null || scale == _barsScale)
        {
            return false;
        }

        _barsScale = scale;
        var center = Height / 2f;
        for (var y = _stripTop; y < _stripTop + _stripHeight; y++)
        {
            var target = new Span<uint>(_pixels.Row(y) + _stripLeft, _stripWidth);
            var source = (int)MathF.Floor(center + (y + 0.5f - center) / scale) - _stripTop;
            if (source < 0 || source >= _stripHeight)
            {
                target.Fill(0xFF000000 | NotificationPalette.Fill);
            }
            else
            {
                new ReadOnlySpan<uint>(_strip + source * _stripWidth, _stripWidth).CopyTo(target);
            }
        }

        return true;
    }

    /// <summary>The colour of a pixel, premultiplied 0xAARRGGBB.</summary>
    public uint PixelAt(int x, int y) => _pixels.Row(y)[x];

    /// <summary>How many pixels are exactly this opaque colour (0xRRGGBB).</summary>
    public int Count(uint rgb)
    {
        var wanted = 0xFF000000 | rgb;
        var count = 0;
        for (var y = 0; y < Height; y++)
        {
            foreach (var pixel in new ReadOnlySpan<uint>(_pixels.Row(y), Width))
            {
                count += pixel == wanted ? 1 : 0;
            }
        }

        return count;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMemory.Free(_strip);
        if (_ownsPixels)
        {
            NativeMemory.Free(_pixels.Origin);
        }
    }
}
