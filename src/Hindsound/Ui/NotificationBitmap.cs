using System.Runtime.InteropServices;
using Hindsound.Ui.Window;

namespace Hindsound.Ui;

/// <summary>One notification as a prepared bitmap, plus the bars as a strip copied in, row by row, while they rise.</summary>
internal sealed unsafe class NotificationBitmap : IDisposable
{
    private const float TabCut = 14, StripeWidth = 4, IconSize = 24, BarsWidth = 96, Margin = 14, Gap = 12;

    private readonly nint _dib, _oldBitmap;
    private readonly uint* _strip;
    private readonly Pixels _pixels;
    private readonly NotificationContent _content;
    private readonly uint _accent;
    private readonly bool _mirrored;
    private readonly TextEngine? _engine;
    private readonly int _stripLeft, _stripTop, _stripWidth, _stripHeight;
    private float _barsScale = -1;

    public nint Dc { get; }
    public int Width { get; }
    public int Height { get; }

    public Native.RECT BarsRect => new()
    {
        Left = _stripLeft,
        Top = _stripTop,
        Right = _stripLeft + _stripWidth,
        Bottom = _stripTop + _stripHeight,
    };

    /// <param name="content">What to draw.</param>
    /// <param name="scale">Physical pixels per design pixel.</param>
    /// <param name="accent">The accent colour, for the glow of the bars.</param>
    /// <param name="mirrored">On the left edge of the screen: the tab is flipped, the text still reads left to right.</param>
    public NotificationBitmap(NotificationContent content, double scale, uint accent, bool mirrored)
        : this(content, scale, accent, mirrored, TextEngine.Shared)
    {
    }

    /// <param name="engine">The text engine; null draws the text with GDI.</param>
    internal NotificationBitmap(NotificationContent content, double scale, uint accent, bool mirrored, TextEngine? engine)
    {
        _engine = engine;
        _content = content;
        _accent = accent;
        _mirrored = mirrored;
        var s = (float)scale;
        Width = (int)Math.Round(360 * scale);
        Height = (int)Math.Round(64 * scale);

        Native.BITMAPINFOHEADER info = default;
        info.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
        info.Width = Width;
        info.Height = -Height;
        info.Planes = 1;
        info.BitCount = 32;
        Dc = Native.CreateCompatibleDC(0);
        void* bits;
        _dib = Native.CreateDIBSection(Dc, &info, 0, &bits, 0, 0);
        if (_dib == 0)
        {
            Native.DeleteDC(Dc);
            throw new InvalidOperationException("CreateDIBSection failed");
        }

        _oldBitmap = Native.SelectObject(Dc, _dib);
        _pixels = new Pixels((uint*)bits, Width, Height);

        var visualLeft = VisualLeft(content, mirrored);
        _stripLeft = (int)MathF.Floor((visualLeft - 5) * s);
        _stripTop = (int)MathF.Floor(7 * s);
        _stripWidth = (int)MathF.Ceiling((visualLeft + 101) * s) - _stripLeft;
        _stripHeight = Height - 2 * _stripTop;
        _strip = content.HasBars ? (uint*)NativeMemory.Alloc((nuint)(_stripWidth * _stripHeight * sizeof(uint))) : null;

        Draw(s, visualLeft);
    }

    /// <summary>Design pixels from the left of the bitmap to the bars or the icon.</summary>
    public static float VisualLeft(NotificationContent content, bool mirrored) =>
        mirrored ? 360 - Margin - (content.HasBars ? BarsWidth : IconSize) : Margin;

    /// <summary>Design pixels from the left of the bitmap to the text.</summary>
    public static float TextLeft(NotificationContent content, bool mirrored) =>
        mirrored ? StripeWidth + Margin : Margin + (content.HasBars ? BarsWidth : IconSize) + Gap;

    /// <summary>Width of the text in design pixels: what is left between the visual and the stripe.</summary>
    public static float TextWidth(NotificationContent content) =>
        360 - Margin - (content.HasBars ? BarsWidth : IconSize) - Gap - Margin - StripeWidth;

    private void Draw(float s, float visualLeft)
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

        // Grey, not ClearType: the tab is a layered window, its letters must keep a true alpha over whatever is behind.
        using var painter = new TextPainter(Dc, _pixels, _engine, clearType: false);
        var titleFont = new FontSpec(Face.Title, 19 * s);
        var bodyFont = new FontSpec(Face.Body, 13 * s);
        var keyFont = new FontSpec(Face.Mono, (_content.SmallKeys ? 10 : 12) * s);

        var textLeft = TextLeft(_content, _mirrored) * s;
        var textWidth = TextWidth(_content) * s;
        painter.Text(titleFont, painter.Fit(titleFont, _content.Title, textWidth), textLeft, 8.7f * s, NotificationPalette.Title);

        var rowTop = MathF.Round(34 * s);
        var rowHeight = MathF.Round(20 * s);
        var body = BodyShown(_content, painter, bodyFont, keyFont, textWidth, s);
        painter.Text(bodyFont, body, textLeft, rowTop + (rowHeight - painter.LineHeight(bodyFont)) / 2, NotificationPalette.Muted);

        var x = MathF.Round(textLeft + painter.Measure(bodyFont, body) + 6 * s);
        for (var i = 0; i < _content.Keys.Count; i++)
        {
            x = DrawKey(painter, keyFont, _content.Keys[i], x, rowTop, rowHeight, s) + MathF.Round(4 * s);
        }
    }

    /// <summary>What the second row really says, once the keys have taken their room: the tests measure this very path.</summary>
    internal static string BodyShown(NotificationContent content, TextPainter painter, FontSpec bodyFont, FontSpec keyFont, float textWidth, float s) =>
        content.Keys.Count > 0
            ? BodyBeforeKeys(content, painter, bodyFont, textWidth - KeysWidth(content, painter, keyFont, s))
            : painter.Fit(bodyFont, content.Body, textWidth);

    // A long shortcut wins over the sentence: the keys are the useful part of the row.
    private static string BodyBeforeKeys(NotificationContent content, TextPainter painter, FontSpec font, float width)
    {
        if (painter.Measure(font, content.Body) <= width)
        {
            return content.Body;
        }

        var lastSentence = content.Body[(content.Body.LastIndexOf(' ') + 1)..];
        return painter.Measure(font, lastSentence) <= width ? lastSentence : string.Empty;
    }

    private static float KeysWidth(NotificationContent content, TextPainter painter, FontSpec font, float s)
    {
        var width = 6 * s;
        foreach (var key in content.Keys)
        {
            width += MathF.Round(painter.Measure(font, key) + 2 * (KeyPaddingOf(content) * s + s)) + MathF.Round(4 * s);
        }

        return width;
    }

    private static float KeyPaddingOf(NotificationContent content) => content.SmallKeys ? 4 : 6;

    private float DrawKey(TextPainter painter, FontSpec font, string text, float left, float rowTop, float rowHeight, float s)
    {
        var height = _content.SmallKeys ? MathF.Round(16 * s) : rowHeight;
        var top = rowTop + MathF.Floor((rowHeight - height) / 2);
        var textWidth = painter.Measure(font, text);
        var right = left + MathF.Round(textWidth + 2 * (KeyPaddingOf(_content) * s + s));
        var radius = 3 * s;
        _pixels.FillRoundRect(left, top, right, top + height, radius, NotificationPalette.CapBorder);
        _pixels.FillRoundRect(left + s, top + s, right - s, top + height - s, MathF.Max(0, radius - s), NotificationPalette.CapFill);
        painter.Text(font, text, left + (right - left - textWidth) / 2, top + (height - painter.LineHeight(font)) / 2, NotificationPalette.Title);
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
        var bars = stackalloc Native.RECT[columns.Count];
        var anyActive = false;
        for (var i = 0; i < columns.Count; i++)
        {
            var left = (int)MathF.Floor((visualLeft + 1 + i * 4) * s + 0.5f);
            var right = left + (int)MathF.Round(2 * s);
            var height = (int)MathF.Round(columns[i].Height * s);
            var top = (Height - height) / 2;
            bars[i] = new Native.RECT { Left = left, Top = top, Right = right, Bottom = top + height };
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
            ref var bar = ref bars[i];
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

    /// <summary>The colour of a pixel, for tests.</summary>
    public uint PixelAt(int x, int y) => _pixels.Row(y)[x];

    /// <summary>Writes the bitmap over the dark desktop colour, to check the drawing without showing anything.</summary>
    public void DumpBmp(string path)
    {
        SetBarsScale(1);
        const int headerSize = 54;
        var rowBytes = Width * 4;
        var file = new byte[headerSize + rowBytes * Height];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.TryWriteBytes(file.AsSpan(2), file.Length);
        BitConverter.TryWriteBytes(file.AsSpan(10), headerSize);
        BitConverter.TryWriteBytes(file.AsSpan(14), 40);
        BitConverter.TryWriteBytes(file.AsSpan(18), Width);
        BitConverter.TryWriteBytes(file.AsSpan(22), -Height);
        BitConverter.TryWriteBytes(file.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(file.AsSpan(28), (short)32);
        for (var y = 0; y < Height; y++)
        {
            var row = _pixels.Row(y);
            for (var x = 0; x < Width; x++)
            {
                var value = row[x];
                var keep = 255 - (value >> 24);
                var r = ((value >> 16) & 0xFF) + 0x0D * keep / 255;
                var g = ((value >> 8) & 0xFF) + 0x0F * keep / 255;
                var b = (value & 0xFF) + 0x13 * keep / 255;
                BitConverter.TryWriteBytes(file.AsSpan(headerSize + y * rowBytes + x * 4), 0xFF000000 | (r << 16) | (g << 8) | b);
            }
        }

        File.WriteAllBytes(path, file);
    }

    public void Dispose()
    {
        Native.SelectObject(Dc, _oldBitmap);
        Native.DeleteObject(_dib);
        Native.DeleteDC(Dc);
        NativeMemory.Free(_strip);
    }
}
