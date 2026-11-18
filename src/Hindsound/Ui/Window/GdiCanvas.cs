namespace Hindsound.Ui.Window;

/// <summary>
/// The window's bitmap: everything is drawn here, then copied to the screen in one go, so nothing flickers. A DIB, so that GDI
/// can copy it and DirectWrite draw the text on it; the shapes are drawn by <see cref="PixelCanvas"/>, and the notification by
/// the code of the real one.
/// </summary>
internal sealed unsafe class GdiCanvas : PixelCanvas
{
    private readonly nint _dc, _dib, _oldBitmap;
    private readonly TextPainter _text;
    private NotificationBitmap? _notification;
    private (uint Accent, bool Mirrored) _notificationKey;

    public GdiCanvas(int width, int height, double scale)
        : this(width, height, scale, TextEngine.Shared)
    {
    }

    /// <param name="engine">The text engine; null draws the text with GDI.</param>
    internal GdiCanvas(int width, int height, double scale, TextEngine? engine)
        : this(Dib.Create(width, height), width, height, scale, engine)
    {
    }

    private GdiCanvas(Dib dib, int width, int height, double scale, TextEngine? engine)
        : base(dib.Pixels, scale, dib.Painter(engine))
    {
        _dc = dib.Dc;
        _dib = dib.Bitmap;
        _oldBitmap = dib.OldBitmap;
        _text = (TextPainter)Typesetter;
    }

    public nint Dc => _dc;

    /// <summary>True when the text is drawn by DirectWrite, false when GDI took over.</summary>
    internal bool UsesDirectWrite => _text.UsesDirectWrite;

    public override void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored)
    {
        if (_notification is null || _notificationKey != (accent, mirrored))
        {
            _notification?.Dispose();
            _notification = new NotificationBitmap(content, Factor, accent, mirrored);
            _notificationKey = (accent, mirrored);
        }

        // The bitmap is premultiplied and its cut corner is clear: it goes over what is below, not instead of it.
        var left = X(x);
        var top = Y(y);
        for (var row = 0; row < _notification.Height; row++)
        {
            var targetY = top + row;
            if (targetY < 0 || targetY >= Height)
            {
                continue;
            }

            var line = Pixels.Row(targetY);
            for (var col = 0; col < _notification.Width && left + col < Width; col++)
            {
                var source = _notification.PixelAt(col, row);
                var keep = 255 - (source >> 24);
                var below = line[left + col];
                var r = ((source >> 16) & 0xFF) + ((below >> 16) & 0xFF) * keep / 255;
                var g = ((source >> 8) & 0xFF) + ((below >> 8) & 0xFF) * keep / 255;
                var b = (source & 0xFF) + (below & 0xFF) * keep / 255;
                line[left + col] = 0xFF000000 | (r << 16) | (g << 8) | b;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        _notification?.Dispose();
        base.Dispose(disposing);
        Native.SelectObject(_dc, _oldBitmap);
        Native.DeleteObject(_dib);
        Native.DeleteDC(_dc);
    }

    /// <summary>A DIB of 32 bits per pixel, top row first, selected into a memory DC of its own.</summary>
    private readonly record struct Dib(nint Dc, nint Bitmap, nint OldBitmap, Pixels Pixels)
    {
        public static Dib Create(int width, int height)
        {
            Native.BITMAPINFOHEADER info = default;
            info.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
            info.Width = width;
            info.Height = -height;
            info.Planes = 1;
            info.BitCount = 32;
            var dc = Native.CreateCompatibleDC(0);
            void* bits;
            var dib = Native.CreateDIBSection(dc, &info, 0, &bits, 0, 0);
            if (dib == 0)
            {
                Native.DeleteDC(dc);
                throw new InvalidOperationException("CreateDIBSection failed");
            }

            var oldBitmap = Native.SelectObject(dc, dib);
            return new Dib(dc, dib, oldBitmap, new Pixels((uint*)bits, width, height));
        }

        // The window is opaque: ClearType, as the page of the mockup (v1.png) that Chrome draws, unless the user turned ClearType
        // off in Windows, which Chrome follows too.
        public TextPainter Painter(TextEngine? engine) =>
            new(Dc, Pixels, engine, clearType: TextEngine.SystemUsesClearType(), opaque: true);
    }
}
