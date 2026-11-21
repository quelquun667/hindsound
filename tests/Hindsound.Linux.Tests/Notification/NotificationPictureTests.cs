using Hindsound.Export;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Unix.Notification;

namespace Hindsound.Linux.Tests.Notification;

/// <summary>The portable picture of the notification, on any system, with a text that draws nothing.</summary>
public sealed class NotificationPictureTests
{
    private const uint Accent = NotificationPalette.Lime;

    internal static NotificationContent Saved()
    {
        float[] bars = [0.25f, 0.3f, 0.42f, 0.15f, 0.38f, 0.51f, 0.14f, 0.35f, 0.44f, 0.78f, 0.88f, 0.8f, 0.6f, 0.5f, 0.4f, 0.3f, 0.7f, 0.2f, 0.9f, 0.1f, 0.5f, 0.6f, 0.3f, 0.8f];
        var thirty = TimeSpan.FromSeconds(30);
        return NotificationStates.ForClip(new ClipResult(["clip.mp3"], thirty, 1, false, bars, false, false), thirty, true, true, Accent);
    }

    [Theory]
    [InlineData(1.0, 360, 64)]
    [InlineData(1.5, 540, 96)]
    [InlineData(2.0, 720, 128)]
    public void It_is_360_by_64_design_units_at_every_scale(double scale, int width, int height)
    {
        using var text = new FakeText();
        using var picture = new NotificationPicture(Saved(), scale, Accent, false, text);

        Assert.Equal((width, height), (picture.Width, picture.Height));
        Assert.Equal((width, height), (NotificationPicture.WidthAt(scale), NotificationPicture.HeightAt(scale)));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void On_the_right_edge_the_corner_is_cut_the_border_left_and_the_stripe_right(double scale)
    {
        using var text = new FakeText();
        var content = Saved();
        using var picture = new NotificationPicture(content, scale, Accent, false, text);

        Assert.Equal(0u, picture.PixelAt(0, 0));
        Assert.Equal(0xFF000000 | NotificationPalette.Border, picture.PixelAt(0, picture.Height / 2));
        Assert.Equal(0xFF000000 | NotificationPalette.Fill, picture.PixelAt(picture.Width / 2, (int)(5 * scale)));
        Assert.Equal(0xFF000000 | content.Stripe, picture.PixelAt(picture.Width - 1, picture.Height / 2));
        Assert.Equal(0xFF000000 | content.Stripe, picture.PixelAt(picture.Width - 1, 0));
    }

    [Fact]
    public void Mirrored_for_the_left_edge_the_stripe_is_left_and_the_cut_corner_right()
    {
        using var text = new FakeText();
        var content = Saved();
        using var picture = new NotificationPicture(content, 1, Accent, true, text);

        Assert.Equal(0u, picture.PixelAt(picture.Width - 1, 0));
        Assert.Equal(0xFF000000 | content.Stripe, picture.PixelAt(0, picture.Height / 2));
        Assert.Equal(0xFF000000 | NotificationPalette.Border, picture.PixelAt(picture.Width - 1, picture.Height / 2));
    }

    [Fact]
    public void The_bars_are_in_the_accent_and_rise_from_their_strip()
    {
        using var text = new FakeText();
        using var picture = new NotificationPicture(Saved(), 1, Accent, false, text);
        var full = picture.Count(Accent);

        Assert.True(picture.SetBarsScale(0.05f));
        var low = picture.Count(Accent);
        Assert.False(picture.SetBarsScale(0.05f));
        Assert.True(picture.SetBarsScale(1));

        Assert.True(full > 300, $"{full} pixels of the accent");
        Assert.True(low < full / 2, $"{low} of {full} pixels of the accent with the bars at 5 %");
        Assert.Equal(full, picture.Count(Accent));
        var (x, _, w, _) = picture.BarsRect;
        Assert.InRange(x, 0, 14);
        Assert.InRange(x + w, 96, 120);
    }

    [Fact]
    public void An_icon_has_no_bars_to_raise()
    {
        using var text = new FakeText();
        using var picture = new NotificationPicture(NotificationStates.NoDevice(), 1, Accent, false, text);

        Assert.False(picture.SetBarsScale(0.5f));
        Assert.Equal(0, picture.Count(Accent));
    }

    [Theory]
    [InlineData(1.0, false, 122)]
    [InlineData(1.5, false, 122)]
    [InlineData(1.0, true, 18)]
    public void The_title_body_and_keys_are_placed_as_on_Windows(double scale, bool mirrored, float textLeft)
    {
        using var text = new FakeText();
        var s = (float)scale;
        using var picture = new NotificationPicture(Saved(), scale, Accent, mirrored, text);

        var title = text.Drawn[0];
        Assert.Equal((Face.Title, 19 * s, NotificationPalette.Title), (title.Face, title.Size, title.Rgb));
        Assert.Equal(textLeft * s, title.X, 3);
        Assert.Equal(8.7f * s, title.Top, 3);
        var body = text.Drawn[1];
        Assert.Equal((Face.Body, "Copied. Paste", NotificationPalette.Muted), (body.Face, body.Text, body.Rgb));
        Assert.Equal(textLeft * s, body.X, 3);
        Assert.Equal(["Ctrl", "V"], text.Drawn.Skip(2).Select(d => d.Text));
        Assert.All(text.Drawn.Skip(2), key => Assert.Equal(Face.Mono, key.Face));
        Assert.True(text.Drawn[2].X > body.X + text.Measure(new FontSpec(Face.Body, 13 * s), body.Text));
        Assert.Equal(0xFF000000 | NotificationPalette.CapBorder, picture.PixelAt((int)(text.Drawn[2].X - 7 * s), (int)(44 * s)));
    }

    [Fact]
    public void A_text_too_long_is_cut_with_an_ellipsis_and_one_that_cannot_fit_is_dropped()
    {
        using var text = new FakeText();
        var font = new FontSpec(Face.Body, 10);

        Assert.Equal("short", NotificationPicture.Fit(text, font, "short", 100));
        var cut = NotificationPicture.Fit(text, font, "a sentence much too long for its room", 100);
        Assert.EndsWith("...", cut, StringComparison.Ordinal);
        Assert.True(text.Measure(font, cut) <= 100);
        Assert.Equal(string.Empty, NotificationPicture.Fit(text, font, "words", 10));
    }

    [Fact]
    public unsafe void A_bitmap_of_the_wrong_size_is_refused()
    {
        using var text = new FakeText();
        var bits = new uint[100 * 64];
        fixed (uint* origin = bits)
        {
            var pixels = new Pixels(origin, 100, 64);
            Assert.Throws<ArgumentException>(() => new NotificationPicture(pixels, Saved(), 1, Accent, false, text));
        }
    }
}
