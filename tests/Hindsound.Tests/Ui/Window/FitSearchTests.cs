using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>c.Fit halves the lengths it tries, and gives the text that trying them one by one gave.</summary>
public class FitSearchTests
{
    /// <summary>A canvas that counts how often the text is measured, the whole cost of Fit.</summary>
    private sealed class MeasureCounter(ICanvas inner) : ICanvas
    {
        public int Measures { get; private set; }

        public void SetOrigin(float dy) => inner.SetOrigin(dy);

        public void FillRect(Box box, uint rgb) => inner.FillRect(box, rgb);

        public void FillRound(Box box, float radius, uint rgb) => inner.FillRound(box, radius, rgb);

        public void Frame(Box box, float radius, uint fill, uint border) => inner.Frame(box, radius, fill, border);

        public void Ring(Box box, float radius, float thickness, uint rgb) => inner.Ring(box, radius, thickness, rgb);

        public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => inner.Stroke(ax, ay, bx, by, width, rgb);

        public void Glow(Box box, float sigma, float opacity, uint rgb) => inner.Glow(box, sigma, opacity, rgb);

        public void Text(string text, Face face, float size, float x, float top, uint rgb) => inner.Text(text, face, size, x, top, rgb);

        public float Measure(string text, Face face, float size)
        {
            Measures++;
            return inner.Measure(text, face, size);
        }

        public float LineHeight(Face face, float size) => inner.LineHeight(face, size);

        public void WarningIcon(float x, float y, float size, uint rgb) => inner.WarningIcon(x, y, size, rgb);

        public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) => inner.Notification(content, x, y, accent, mirrored);
    }

    /// <summary>The way it was written before: from the longest cut down, one length at a time.</summary>
    private static string OneByOne(ICanvas c, string text, Face face, float size, float width)
    {
        if (c.Measure(text, face, size) <= width)
        {
            return text;
        }

        for (var length = text.Length - 1; length > 0; length--)
        {
            var cut = text[..length].TrimEnd() + "...";
            if (c.Measure(cut, face, size) <= width)
            {
                return cut;
            }
        }

        return string.Empty;
    }

    // The lengths the cost was measured on, names in W, the widest case, at the width of a row.
    [Theory]
    [InlineData(41)]
    [InlineData(70)]
    [InlineData(120)]
    [InlineData(255)]
    public void On_the_real_canvas_it_gives_the_same_text_at_the_measured_lengths(int length)
    {
        using var canvas = new GdiCanvas(760, 488, 1.0);
        var name = new string('W', length);

        Assert.Equal(OneByOne(canvas, name, Face.Body, 14, 418), canvas.Fit(name, Face.Body, 14, 418));
    }

    [Fact]
    public void On_the_real_canvas_it_gives_the_same_text_for_a_real_clip_name_at_any_width()
    {
        using var canvas = new GdiCanvas(760, 488, 1.0);
        const string name = "Hindsound_Discord_2026-11-08_21-14-03_with some words after it (2).mp3";

        for (var width = 0; width < 520; width += 7)
        {
            Assert.Equal(OneByOne(canvas, name, Face.Body, 14, width), canvas.Fit(name, Face.Body, 14, width));
        }
    }

    [Fact]
    public void The_longest_cut_can_be_one_character_short_of_the_whole_text()
    {
        // A W is wider than the three dots, so the text cut by one character can fit where the whole of it does not.
        using var canvas = new GdiCanvas(760, 488, 1.0);
        const string name = "iiiiWWW", cut = "iiiiWW...";
        var width = canvas.Measure(cut, Face.Body, 14);
        Assert.True(width < canvas.Measure(name, Face.Body, 14));

        Assert.Equal(cut, canvas.Fit(name, Face.Body, 14, width));
    }

    [Theory]
    [InlineData("demo.mp3")]
    [InlineData("two words and  some   spaces in a row.mp3")]
    [InlineData("   starts with spaces")]
    [InlineData("          ")]
    [InlineData("x")]
    [InlineData("")]
    public void It_gives_the_same_text_at_every_width(string text)
    {
        var canvas = new FakeCanvas();

        for (var width = -1; width < text.Length * 8 + 8; width++)
        {
            Assert.Equal(OneByOne(canvas, text, Face.Body, 14, width), canvas.Fit(text, Face.Body, 14, width));
        }
    }

    [Theory]
    [InlineData(70, 9)]
    [InlineData(120, 9)]
    [InlineData(255, 10)]
    public void A_long_name_is_measured_a_handful_of_times(int length, int most)
    {
        var canvas = new MeasureCounter(new FakeCanvas());

        canvas.Fit(new string('W', length), Face.Body, 14, 100);

        Assert.InRange(canvas.Measures, 1, most);
    }

    [Fact]
    public void A_text_that_fits_is_measured_once_and_comes_back_whole()
    {
        var canvas = new MeasureCounter(new FakeCanvas());

        Assert.Equal("demo.mp3", canvas.Fit("demo.mp3", Face.Body, 14, 500));
        Assert.Equal(1, canvas.Measures);
    }

    [Fact]
    public void Nothing_fits_in_no_room()
    {
        Assert.Equal(string.Empty, new FakeCanvas().Fit("demo.mp3", Face.Body, 14, 1));
    }
}
