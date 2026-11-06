using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui;

/// <summary>
/// A sheet of the tray icon, drawn by the real code, for a person to look at. HINDSOUND_TRAY_SHEET names the file to keep;
/// without it, the sheet is drawn in the temporary folder and removed.
/// </summary>
public class TrayIconSheetTests
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48];
    private static readonly uint[] Accents = [0xD4F53C, 0x35D6FF, 0xB79CFF, 0xF2F4F7];
    private const int Label = 118, Gap = 12, RowHeight = 108, Header = 26, Enlarged = 96;
    private const uint DarkTaskbar = 0x1C1C1C, LightTaskbar = 0xF3F3F3;

    [Fact]
    public void The_sheet_is_drawn_for_every_size_state_accent_and_background()
    {
        var wanted = Environment.GetEnvironmentVariable("HINDSOUND_TRAY_SHEET");
        var path = wanted ?? Path.Combine(Path.GetTempPath(), $"hindsound-sheet-{Guid.NewGuid():N}.png");
        try
        {
            Write(path);
            Assert.True(new FileInfo(path).Length > 1000);
        }
        finally
        {
            if (wanted is null)
            {
                File.Delete(path);
            }
        }
    }

    private static int PanelWidth => Label + Sizes.Sum(size => size + 6 + (Enlarged / size * size) + Gap);

    private static unsafe void Write(string path)
    {
        var rows = (Accents.Length * 2 * RowHeight) + Header;
        using var canvas = new GdiCanvas(PanelWidth * 2, rows, 1);
        DrawPanel(canvas, 0, DarkTaskbar, 0xF2F4F7);
        DrawPanel(canvas, PanelWidth, LightTaskbar, 0x15181E);
        PngFile.Write(path, canvas.Bits, canvas.Width, canvas.Height);
    }

    private static void DrawPanel(GdiCanvas canvas, int left, uint background, uint text)
    {
        canvas.FillRect(new Box(left, 0, PanelWidth, canvas.Height), background);
        var x = left + Label;
        foreach (var size in Sizes)
        {
            canvas.Text($"{size} px", Face.Body, 12, x, 5, text);
            x += size + 6 + (Enlarged / size * size) + Gap;
        }

        var y = Header;
        foreach (var state in new[] { TrayState.Active, TrayState.Paused })
        {
            foreach (var accent in Accents)
            {
                canvas.Text($"{(state == TrayState.Active ? "active" : "pause")} #{accent:X6}", Face.Body, 12, left + 8, y + 40, text);
                x = left + Label;
                foreach (var size in Sizes)
                {
                    var pixels = TrayIconShape.Compose(state, size, accent);
                    var scale = Enlarged / size;
                    Blit(canvas, pixels, size, x, y + Enlarged - size, 1);
                    Blit(canvas, pixels, size, x + size + 6, y + Enlarged - (size * scale), scale);
                    x += size + 6 + (size * scale) + Gap;
                }

                y += RowHeight;
            }
        }
    }

    // The icon's own alpha over the background, each of its pixels a square of scale x scale.
    private static unsafe void Blit(GdiCanvas canvas, uint[] pixels, int size, int left, int top, int scale)
    {
        for (var y = 0; y < size * scale; y++)
        {
            for (var x = 0; x < size * scale; x++)
            {
                var source = pixels[(y / scale * size) + (x / scale)];
                var target = canvas.Bits + ((top + y) * canvas.Width) + left + x;
                var alpha = source >> 24;
                uint Channel(int shift) =>
                    ((((source >> shift) & 0xFF) * alpha) + (((*target >> shift) & 0xFF) * (255 - alpha))) / 255 << shift;
                *target = 0xFF000000 | Channel(16) | Channel(8) | Channel(0);
            }
        }
    }
}
