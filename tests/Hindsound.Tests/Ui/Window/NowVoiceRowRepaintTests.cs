using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The switch of the voice is a widget of the page: a repaint of the titles, which a tick does, must not wipe it.</summary>
public sealed class NowVoiceRowRepaintTests
{
    private sealed class FillLog : ICanvas
    {
        public List<Box> Fills { get; } = [];

        public void SetOrigin(float dy) { }
        public void FillRect(Box box, uint rgb) => Fills.Add(box);
        public void FillRound(Box box, float radius, uint rgb) => Fills.Add(box);
        public void Frame(Box box, float radius, uint fill, uint border) => Fills.Add(box);
        public void Ring(Box box, float radius, float thickness, uint rgb) { }
        public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) { }
        public void Glow(Box box, float sigma, float opacity, uint rgb) { }
        public void Dim(float opacity) { }
        public void Text(string text, Face face, float size, float x, float top, uint rgb) { }
        public float Measure(string text, Face face, float size) => text.Length * size * 0.5f;
        public float LineHeight(Face face, float size) => size * 1.3f;
        public void WarningIcon(float x, float y, float size, uint rgb) { }
        public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) { }
    }

    private static bool Overlaps(Box a, Box b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    [Fact]
    public void Repainting_the_titles_leaves_the_switch_and_its_words_alone()
    {
        var wave = new NowWave();
        var status = ScreenRig.Capturing();
        wave.Look(1.0, default, 30, status);
        var canvas = new FillLog();

        wave.PaintLanes(canvas, 0xD4F53C, status, titles: true);

        Assert.DoesNotContain(canvas.Fills, fill => Overlaps(fill, NowWave.VoiceSwitch));
        Assert.Contains(canvas.Fills, fill => fill.Y < NowWave.VoiceSwitch.Y && fill.W > 400);
    }

    [Fact]
    public void Repainting_the_titles_still_clears_the_rest_of_the_title_line_of_the_Mic_lane()
    {
        var wave = new NowWave();
        var status = ScreenRig.Capturing();
        wave.Look(1.0, default, 30, status);
        var canvas = new FillLog();

        wave.PaintLanes(canvas, 0xD4F53C, status, titles: true);

        var title = new Box(SettingsScreen.Left + 5, NowWave.VoiceSwitch.MidY, 20, 2);
        Assert.Contains(canvas.Fills, fill => fill.X <= title.X && fill.Right >= title.Right && fill.Y <= title.Y && fill.Bottom >= title.Bottom);
    }
}
