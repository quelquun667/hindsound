using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A real canvas that also writes down what the page asked it to draw, which the pixels alone cannot say.</summary>
internal sealed class WatchedCanvas(ICanvas inner) : ICanvas
{
    public List<string> Texts { get; } = [];

    public List<Box> Fills { get; } = [];

    public void Reset()
    {
        Texts.Clear();
        Fills.Clear();
    }

    public double Scale => inner.Scale;
    public void SetOrigin(float dy) => inner.SetOrigin(dy);

    public void FillRect(Box box, uint rgb)
    {
        Fills.Add(box);
        inner.FillRect(box, rgb);
    }

    public void FillRound(Box box, float radius, uint rgb) => inner.FillRound(box, radius, rgb);
    public void Frame(Box box, float radius, uint fill, uint border) => inner.Frame(box, radius, fill, border);
    public void Ring(Box box, float radius, float thickness, uint rgb) => inner.Ring(box, radius, thickness, rgb);
    public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => inner.Stroke(ax, ay, bx, by, width, rgb);
    public void Bars(in WaveGrid grid, ReadOnlySpan<byte> levels, ReadOnlySpan<BarColumn> columns, uint baseline) => inner.Bars(grid, levels, columns, baseline);
    public void Glow(Box box, float sigma, float opacity, uint rgb) => inner.Glow(box, sigma, opacity, rgb);

    public void Text(string text, Face face, float size, float x, float top, uint rgb)
    {
        Texts.Add(text);
        inner.Text(text, face, size, x, top, rgb);
    }

    public float Measure(string text, Face face, float size) => inner.Measure(text, face, size);
    public float LineHeight(Face face, float size) => inner.LineHeight(face, size);
    public void WarningIcon(float x, float y, float size, uint rgb) => inner.WarningIcon(x, y, size, rgb);
    public void Notification(Hindsound.Ui.NotificationContent content, float x, float y, uint accent, bool mirrored) => inner.Notification(content, x, y, accent, mirrored);
}

/// <summary>The words of the lanes are drawn by a full paint and by a change of words, not by every tick that moves a bar.</summary>
public class NowTickDrawingTests
{
    [Fact]
    public void A_tick_that_moves_only_the_bars_writes_no_title_and_clears_no_wave_area()
    {
        using var screens = new ScreenRig();
        using var bitmap = new GdiCanvas(
            (int)SettingsScreen.WindowWidth, (int)SettingsScreen.WindowHeight, 1.0, null);
        var canvas = new WatchedCanvas(bitmap);
        var host = new WaveHost();
        var panel = new NowPanel { Facts = NowRig.Held(NowRig.Loud(10), null, 20) };
        var screen = new SettingsScreen(screens.Settings, screens.Backend, host, canvas);
        screen.UpdateStatus(ScreenRig.Capturing());
        screen.SetNow(panel);
        screen.Paint(canvas);
        screen.WaveTick();
        host.Reset();
        canvas.Reset();

        panel.Facts = NowRig.Held(panel.Facts.System, null, 26);
        screen.WaveTick();

        Assert.Contains(NowWave.BarsArea, host.Areas);
        Assert.DoesNotContain(NowWave.WaveArea, host.Areas);
        Assert.DoesNotContain(NowWave.WaveArea, canvas.Fills);
        // "Filling..." is part of the bars (inside their area), the titles are not.
        Assert.All(canvas.Texts, text => Assert.True(text.StartsWith("Save:", StringComparison.Ordinal) || text == "Filling...", text));
    }
}
