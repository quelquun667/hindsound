using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// Room enough for every sentence about what is kept: Fit would cut one without a word, and a sentence too long for its keys
/// is replaced by its last word. The same painter, engine and fonts as the drawing, by the path the drawing takes.
/// </summary>
public unsafe class KeptNoticesWidthTests
{
    private const string Default = "Ctrl+Shift+F10";

    public static TheoryData<double> Scales => new() { 1.0, 1.25, 1.5, 1.75, 2.0 };

    private static IEnumerable<NotificationContent> Samples()
    {
        // The longest figures the settings allow: 30:00 is the top of the slider.
        foreach (var cap in new[] { 300, 1800 })
        {
            foreach (var kept in new int?[] { null, 0, 130, cap - 50, cap })
            {
                yield return NotificationStates.CaptureOn(cap, Default, keptSeconds: kept);
                yield return NotificationStates.CaptureOn(cap, null, keptSeconds: kept);
                yield return NotificationStates.CaptureOn(cap, Default, "Save hotkey is taken", keptSeconds: kept);
                yield return NotificationStates.CaptureOn(cap, Default, "Save hotkey doesn't work", keptSeconds: kept);
                yield return NotificationStates.CaptureOn(cap, Default, updatedTo: "10.20.30", keptSeconds: kept);
                yield return NotificationStates.CaptureOn(cap, Default, "Save hotkey doesn't work", updatedTo: "10.20.30", keptSeconds: kept);
            }
        }

        yield return NotificationStates.Paused();
        yield return NotificationStates.Paused(130);
        yield return NotificationStates.Paused(1800);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void No_text_is_cut(double scale)
    {
        var s = (float)scale;
        using var target = new Target((int)Math.Round(360 * scale), (int)Math.Round(64 * scale));
        using var painter = new TextPainter(target.Dc, target.Pixels, TextEngine.Shared, clearType: false);
        var titleFont = new FontSpec(Face.Title, 19 * s);
        var bodyFont = new FontSpec(Face.Body, 13 * s);

        foreach (var content in Samples())
        {
            var keyFont = new FontSpec(Face.Mono, (content.SmallKeys ? 10 : 12) * s);
            var room = NotificationBitmap.TextWidth(content) * s;

            Assert.Equal(content.Title, painter.Fit(titleFont, content.Title, room));
            Assert.Equal(content.Body, NotificationBitmap.BodyShown(content, painter, bodyFont, keyFont, room, s));
        }
    }

    // A 32-bit DIB selected in a memory DC, as NotificationBitmap makes its own.
    private sealed class Target : IDisposable
    {
        private readonly nint _dib, _old;

        public Target(int width, int height)
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
