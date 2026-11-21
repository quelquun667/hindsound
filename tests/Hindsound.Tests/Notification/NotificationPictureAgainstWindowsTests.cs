using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Unix.Notification;

namespace Hindsound.Tests.PortableNotification;

/// <summary>
/// The portable notification of macOS (NotificationPicture, compiled in here from Hindsound.Unix) set against the Windows one
/// (NotificationBitmap), pixel by pixel, for every sample of <c>--dump-notifications</c>, at the scales 1 and 1.5, on the right
/// edge and mirrored on the left, the bars full and halfway up. Windows has no FreeType, so the portable picture gets the
/// Windows text here (DirectWrite through a TextPainter on a DIB of its own, as NotificationBitmap draws it): what is left to
/// differ is the portable drawing itself, and nothing may.
/// </summary>
public sealed unsafe class NotificationPictureAgainstWindowsTests
{
    [Theory]
    [InlineData(1.0, false)]
    [InlineData(1.5, false)]
    [InlineData(1.0, true)]
    [InlineData(1.5, true)]
    public void Every_sample_is_the_Windows_bitmap_to_the_pixel(double scale, bool mirrored)
    {
        var accent = NotificationPalette.Lime;
        var report = new List<string>();
        var compared = 0;
        foreach (var (name, content) in NotificationDump.Samples(accent))
        {
            using var windows = new NotificationBitmap(content, scale, accent, mirrored, TextEngine.Shared);
            using var dib = new Dib(windows.Width, windows.Height);
            using var painter = new TextPainter(dib.Dc, dib.Pixels, TextEngine.Shared, clearType: false);
            using var picture = new NotificationPicture(dib.Pixels, content, scale, accent, mirrored, painter);

            Assert.Equal((windows.Width, windows.Height), (picture.Width, picture.Height));
            var bars = windows.BarsRect;
            Assert.Equal((bars.Left, bars.Top, bars.Right - bars.Left, bars.Bottom - bars.Top), picture.BarsRect);
            foreach (var barsScale in new[] { 1f, 0.45f })
            {
                Assert.Equal(windows.SetBarsScale(barsScale), picture.SetBarsScale(barsScale));
                var (differ, worst) = Compare(windows, picture);
                compared++;
                if (differ > 0)
                {
                    report.Add($"{name} bars {barsScale}: {differ} pixels differ, worst channel gap {worst}");
                }
            }
        }

        Assert.Equal(2 * NotificationDump.Samples(accent).Count, compared);
        Assert.True(compared >= 80, $"only {compared} pictures compared");
        Assert.True(report.Count == 0, string.Join(Environment.NewLine, report));
    }

    [Fact]
    public void A_pixel_changed_in_the_portable_picture_is_seen()
    {
        var accent = NotificationPalette.Lime;
        var content = NotificationDump.Samples(accent)[0].Content;
        using var windows = new NotificationBitmap(content, 1, accent, false, TextEngine.Shared);
        using var dib = new Dib(windows.Width, windows.Height);
        using var painter = new TextPainter(dib.Dc, dib.Pixels, TextEngine.Shared, clearType: false);
        using var picture = new NotificationPicture(dib.Pixels, content, 1, accent, false, painter);
        windows.SetBarsScale(1);
        picture.SetBarsScale(1);

        dib.Pixels.Row(30)[200] ^= 0x00000040;

        Assert.Equal((1, 0x40), Compare(windows, picture));
    }

    private static (int Differ, int Worst) Compare(NotificationBitmap windows, NotificationPicture picture)
    {
        int differ = 0, worst = 0;
        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                uint a = windows.PixelAt(x, y), b = picture.PixelAt(x, y);
                if (a == b)
                {
                    continue;
                }

                differ++;
                for (var shift = 0; shift < 32; shift += 8)
                {
                    worst = Math.Max(worst, Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF)));
                }
            }
        }

        return (differ, worst);
    }

    /// <summary>A 32-bit top-down DIB selected in a DC of its own, all zero, as NotificationBitmap makes its own.</summary>
    private sealed class Dib : IDisposable
    {
        private readonly nint _dib, _old;

        public Dib(int width, int height)
        {
            Native.BITMAPINFOHEADER info = default;
            info.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
            info.Width = width;
            info.Height = -height;
            info.Planes = 1;
            info.BitCount = 32;
            Dc = Native.CreateCompatibleDC(0);
            void* bits;
            _dib = Native.CreateDIBSection(Dc, &info, 0, &bits, 0, 0);
            Assert.NotEqual(0, _dib);
            _old = Native.SelectObject(Dc, _dib);
            Pixels = new Pixels((uint*)bits, width, height);
        }

        public nint Dc { get; }

        public Pixels Pixels { get; }

        public void Dispose()
        {
            Native.SelectObject(Dc, _old);
            Native.DeleteObject(_dib);
            Native.DeleteDC(Dc);
        }
    }
}
