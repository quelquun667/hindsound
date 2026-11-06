using System.Runtime.InteropServices;

namespace Hindsound.Ui.Window;

/// <summary>The window's bitmap: everything is drawn here, then copied to the screen in one go, so nothing flickers.</summary>
internal sealed unsafe class GdiCanvas : ICanvas, IDisposable
{
    private readonly nint _dc, _dib, _oldBitmap;
    private readonly Pixels _pixels;
    private readonly TextPainter _text;
    private readonly float _s;
    private readonly int _line;
    private float _originY;
    private NotificationBitmap? _notification;
    private (uint Accent, bool Mirrored) _notificationKey;

    public GdiCanvas(int width, int height, double scale)
    {
        Width = width;
        Height = height;
        _s = (float)scale;
        _line = Math.Max(1, (int)_s);

        Native.BITMAPINFOHEADER info = default;
        info.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
        info.Width = width;
        info.Height = -height;
        info.Planes = 1;
        info.BitCount = 32;
        _dc = Native.CreateCompatibleDC(0);
        void* bits;
        _dib = Native.CreateDIBSection(_dc, &info, 0, &bits, 0, 0);
        if (_dib == 0)
        {
            Native.DeleteDC(_dc);
            throw new InvalidOperationException("CreateDIBSection failed");
        }

        _oldBitmap = Native.SelectObject(_dc, _dib);
        _pixels = new Pixels((uint*)bits, width, height);
        _text = new TextPainter(_dc, _pixels);
    }

    public int Width { get; }

    public int Height { get; }

    public nint Dc => _dc;

    public uint* Bits => _pixels.Origin;

    public void SetOrigin(float dy) => _originY = dy;

    private int X(float x) => (int)MathF.Round(x * _s);

    private int Y(float y) => (int)MathF.Round((y + _originY) * _s);

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

    private FontSpec Font(Face face, float size) => face switch
    {
        Face.Title => new FontSpec("Bahnschrift SemiBold SemiConden", size * _s, false),
        Face.Medium => new FontSpec("Segoe UI Semibold", size * _s, false),
        Face.Mono => new FontSpec("Cascadia Mono", size * _s, true),
        _ => new FontSpec("Segoe UI", size * _s, false),
    };

    public void Text(string text, Face face, float size, float x, float top, uint rgb) =>
        _text.Text(Font(face, size), text, x * _s, (top + _originY) * _s, rgb);

    public float Measure(string text, Face face, float size) => _text.Measure(Font(face, size), text) / _s;

    public float LineHeight(Face face, float size) => _text.LineHeight(Font(face, size)) / _s;

    public void WarningIcon(float x, float y, float size, uint rgb) =>
        NotificationIcons.Draw(_pixels, NotificationVisual.Triangle, rgb, x * _s, (y + _originY) * _s, _s * size / 24);

    public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored)
    {
        if (_notification is null || _notificationKey != (accent, mirrored))
        {
            _notification?.Dispose();
            _notification = new NotificationBitmap(content, _s, accent, mirrored);
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

            var line = _pixels.Row(targetY);
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

    public void Dispose()
    {
        _notification?.Dispose();
        _text.Dispose();
        Native.SelectObject(_dc, _oldBitmap);
        Native.DeleteObject(_dib);
        Native.DeleteDC(_dc);
    }
}

/// <summary>A PNG with no compression, written by hand: a picture for checking the drawing is not worth a library in the exe.</summary>
internal static unsafe class PngFile
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, uint* bgra, int width, int height)
    {
        var rowBytes = width * 3 + 1;
        var raw = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var row = bgra + y * width;
            var at = y * rowBytes + 1;
            for (var x = 0; x < width; x++, at += 3)
            {
                var value = row[x];
                raw[at] = (byte)(value >> 16);
                raw[at + 1] = (byte)(value >> 8);
                raw[at + 2] = (byte)value;
            }
        }

        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 2;
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", Stored(raw));
        Chunk(file, "IEND", []);
    }

    // Zlib with stored blocks, 65535 bytes at most in each.
    private static byte[] Stored(byte[] raw)
    {
        const int block = 65535;
        var blocks = Math.Max(1, (raw.Length + block - 1) / block);
        var output = new byte[2 + raw.Length + blocks * 5 + 4];
        output[0] = 0x78;
        output[1] = 0x01;
        var at = 2;
        for (var i = 0; i < blocks; i++)
        {
            var start = i * block;
            var length = Math.Min(block, raw.Length - start);
            output[at++] = (byte)(i == blocks - 1 ? 1 : 0);
            output[at++] = (byte)length;
            output[at++] = (byte)(length >> 8);
            output[at++] = (byte)~length;
            output[at++] = (byte)(~length >> 8);
            Array.Copy(raw, start, output, at, length);
            at += length;
        }

        uint a = 1, b = 0;
        foreach (var value in raw)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        WriteBigEndian(output, at, (b << 16) | a);
        return output;
    }

    private static void Chunk(Stream file, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        file.Write(length);
        var name = System.Text.Encoding.ASCII.GetBytes(type);
        file.Write(name);
        file.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var value in name)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        var end = new byte[4];
        WriteBigEndian(end, 0, crc ^ 0xFFFFFFFFu);
        file.Write(end);
    }

    private static void WriteBigEndian(byte[] target, int at, uint value)
    {
        target[at] = (byte)(value >> 24);
        target[at + 1] = (byte)(value >> 16);
        target[at + 2] = (byte)(value >> 8);
        target[at + 3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
