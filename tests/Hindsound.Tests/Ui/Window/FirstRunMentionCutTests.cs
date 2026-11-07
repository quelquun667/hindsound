using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The line about GitHub sits in the 34 px row of the first-launch buttons: whatever the font, it takes two lines at most,
/// the second one cut. A sweep of widths goes through every count of lines, so the cut is tried at its edge too.
/// </summary>
public sealed class FirstRunMentionCutTests : IDisposable
{
    private readonly ScreenRig _rig = new(firstRun: true);

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    [Fact]
    public void Any_font_width_draws_two_lines_at_most_the_second_one_cut()
    {
        Screen.SetUpdates(new FakePanel { View = UpdatesSectionTests.ReadyView() });
        var canvas = new ScaledCanvas();
        var cuts = 0;

        // From the recording font to one four times as wide: one, two, three, then many lines before the cut.
        for (var factor = 1f; factor <= 4f; factor += 0.02f)
        {
            canvas.Factor = factor;
            canvas.Inner.Texts.Clear();
            canvas.Inner.Places.Clear();
            Screen.SetCanvas(canvas);
            Screen.Paint(canvas);

            // The box holds as many lines as are drawn: two at most, whatever the width.
            var mention = Screen.WidgetById(SettingsScreen.FirstRunUpdatesId)!.Bounds;
            Assert.True(mention.H <= 2 * canvas.LineHeight(Face.Body, 13) + 0.01f, $"factor {factor}: {mention.H}");
            if (canvas.Inner.Texts.Any(t => t.EndsWith("...", StringComparison.Ordinal) && canvas.Inner.Places[t].X == 40 && canvas.Inner.Places[t].Y >= mention.Y - 0.01f))
            {
                cuts++;
            }
        }

        Assert.True(cuts > 0);
    }

    /// <summary>The recording font, made wider by a factor.</summary>
    private sealed class ScaledCanvas : ICanvas
    {
        public float Factor { get; set; } = 1;

        public RecordingCanvas Inner { get; } = new();

        public void SetOrigin(float dy) => Inner.SetOrigin(dy);
        public void FillRect(Box box, uint rgb) => Inner.FillRect(box, rgb);
        public void FillRound(Box box, float radius, uint rgb) => Inner.FillRound(box, radius, rgb);
        public void Frame(Box box, float radius, uint fill, uint border) => Inner.Frame(box, radius, fill, border);
        public void Ring(Box box, float radius, float thickness, uint rgb) => Inner.Ring(box, radius, thickness, rgb);
        public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => Inner.Stroke(ax, ay, bx, by, width, rgb);
        public void Glow(Box box, float sigma, float opacity, uint rgb) => Inner.Glow(box, sigma, opacity, rgb);
        public void Text(string text, Face face, float size, float x, float top, uint rgb) => Inner.Text(text, face, size, x, top, rgb);
        public float Measure(string text, Face face, float size) => Factor * Inner.Measure(text, face, size);
        public float LineHeight(Face face, float size) => Inner.LineHeight(face, size);
        public void WarningIcon(float x, float y, float size, uint rgb) => Inner.WarningIcon(x, y, size, rgb);
        public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) => Inner.Notification(content, x, y, accent, mirrored);
    }
}
