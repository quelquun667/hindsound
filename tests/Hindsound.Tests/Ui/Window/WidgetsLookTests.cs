using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>A canvas that writes down the frames, the round fills and the texts it is asked for, with their colors.</summary>
internal sealed class ShapesCanvas : ICanvas
{
    public List<(Box Box, uint Fill, uint Border)> Frames { get; } = [];

    public List<(Box Box, uint Rgb)> Rounds { get; } = [];

    public List<(string Text, float X, uint Rgb)> Texts { get; } = [];

    public void SetOrigin(float dy) { }
    public void FillRect(Box box, uint rgb) { }
    public void FillRound(Box box, float radius, uint rgb) => Rounds.Add((box, rgb));
    public void Frame(Box box, float radius, uint fill, uint border) => Frames.Add((box, fill, border));
    public void Ring(Box box, float radius, float thickness, uint rgb) { }
    public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) { }
    public void Glow(Box box, float sigma, float opacity, uint rgb) { }
    public void Text(string text, Face face, float size, float x, float top, uint rgb) => Texts.Add((text, x, rgb));
    public float Measure(string text, Face face, float size) => text.Length * size * 0.5f;
    public float LineHeight(Face face, float size) => size * 1.3f;
    public void WarningIcon(float x, float y, float size, uint rgb) { }
    public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) { }
}

/// <summary>What a switch and a text field look like when they are out of use or carry a suffix.</summary>
public sealed class WidgetsLookTests
{
    private const uint Accent = 0x00FF88;

    private static readonly UiState Ui = new(Accent, null, null, null, -1, -1, 0, false, null);

    private static readonly Box Place = new(100, 50, 40, 22);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_switch_out_of_use_is_grey_whichever_side_it_stands_on(bool on)
    {
        var canvas = new ShapesCanvas();

        new Toggle(() => on, _ => { }) { Bounds = Place, Enabled = false }.Paint(canvas, Ui);

        Assert.DoesNotContain(canvas.Rounds, round => round.Rgb == Accent);
        Assert.DoesNotContain(canvas.Rounds, round => round.Rgb == Theme.Muted);
        var knob = Assert.Single(canvas.Rounds, round => round.Rgb == Theme.Control);
        Assert.Equal(on ? Place.Right - 19 : Place.X + 3, knob.Box.X);
        if (on)
        {
            Assert.Contains(canvas.Rounds, round => round.Box == Place && round.Rgb == Theme.Line);
        }
        else
        {
            Assert.Equal((Place, Theme.Panel, Theme.Line), Assert.Single(canvas.Frames));
        }
    }

    [Fact]
    public void A_switch_in_use_keeps_its_colors()
    {
        var on = new ShapesCanvas();
        var off = new ShapesCanvas();

        new Toggle(() => true, _ => { }) { Bounds = Place }.Paint(on, Ui);
        new Toggle(() => false, _ => { }) { Bounds = Place }.Paint(off, Ui);

        Assert.Contains(on.Rounds, round => round.Box == Place && round.Rgb == Accent);
        Assert.Equal((Place, Theme.Raised, Theme.Control), Assert.Single(off.Frames));
        Assert.Contains(off.Rounds, round => round.Rgb == Theme.Muted);
    }

    [Fact]
    public void A_suffix_stands_in_grey_at_the_right_inside_the_frame_and_takes_its_room_from_the_native_box()
    {
        var field = new TextField(new FieldState("best"), _ => FieldCheck.Fine, null, () => "best")
        {
            Bounds = new Box(100, 50, 300, 32),
            Suffix = ".mp3",
            SuffixWidth = 38,
        };
        var plain = new TextField(new FieldState("best"), _ => FieldCheck.Fine, null, () => "best") { Bounds = field.Bounds };
        var canvas = new ShapesCanvas();

        field.Paint(canvas, Ui);

        var suffix = Assert.Single(canvas.Texts, text => text.Text == ".mp3");
        Assert.Equal(Theme.Faded, suffix.Rgb);
        Assert.Equal(field.Bounds.Right - 8 - canvas.Measure(".mp3", field.Face, field.Size), suffix.X);
        Assert.Equal(plain.TextBox.W - 38, field.TextBox.W);
        Assert.Equal(plain.TextBox.X, field.TextBox.X);
    }

    [Fact]
    public void A_field_without_a_suffix_draws_none_and_keeps_all_its_width()
    {
        var field = new TextField(new FieldState("best"), _ => FieldCheck.Fine, null, () => "best") { Bounds = new Box(100, 50, 300, 32), SuffixWidth = 38 };
        var canvas = new ShapesCanvas();

        field.Paint(canvas, Ui);

        Assert.Equal(["best"], canvas.Texts.Select(text => text.Text));
        Assert.Equal(284, field.TextBox.W);
    }
}
