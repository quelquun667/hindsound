using Hindsound.Settings;

namespace Hindsound.Ui.Window;

/// <summary>The colours of the interface. The accent is a setting, so it is not here, nor what is laid on it: see <see cref="UiState.OnAccent"/>.</summary>
internal static class Theme
{
    public const uint Base = 0x0D0F13, Panel = 0x15181E, Raised = 0x21252D, Hover = 0x2B303A, Line = 0x3A404C, Control = 0x6B7382,
        Text = AccentColors.LightOnAccent, Muted = 0xAAB1BE, Amber = 0xFFB547, Hot = 0xFF6A5C, Faded = 0x6B7382;

    /// <summary>
    /// <paramref name="share"/> of <paramref name="toward"/> over the rest of <paramref name="ground"/>, as a plain colour: how the
    /// dimmed accent, the two greys, the halos and a tinted ground are made, with no alpha for the canvas to blend. Work it out
    /// once per paint, not once per bar. Any colour works, white and black included, and the result never leaves the box of the two.
    /// </summary>
    public static uint Mix(uint toward, uint ground, float share)
    {
        var t = share > 0 ? Math.Min(share, 1f) : 0f;
        return Channel(toward, ground, 16, t) | Channel(toward, ground, 8, t) | Channel(toward, ground, 0, t);
    }

    private static uint Channel(uint toward, uint ground, int shift, float t)
    {
        var x = (toward >> shift) & 0xFF;
        var y = (ground >> shift) & 0xFF;
        return Math.Min(255u, (uint)MathF.Round(x * t + y * (1 - t))) << shift;
    }
}

/// <summary>The four fonts of the interface, drawn by DirectWrite (see <see cref="FontFaces"/>), by GDI if it cannot start.</summary>
internal enum Face
{
    /// <summary>Headings: Bahnschrift, weight 600, SemiCondensed, the same as the notification.</summary>
    Title,

    /// <summary>Running text: Segoe UI 400.</summary>
    Body,

    /// <summary>Buttons and the side bar: Segoe UI 600.</summary>
    Medium,

    /// <summary>Keys, durations and sizes: Cascadia Mono 400, or Consolas where it is not installed.</summary>
    Mono,
}

/// <summary>A rectangle in design pixels: the 100 % size of the mockup. The canvas does the scaling.</summary>
internal readonly record struct Box(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public float MidX => X + W / 2;
    public float MidY => Y + H / 2;

    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;

    public Box Inflate(float by) => new(X - by, Y - by, W + 2 * by, H + 2 * by);
}

/// <summary>What the widgets draw on. The real one is GDI over a bitmap; the tests measure text with a fake.</summary>
internal interface ICanvas
{
    /// <summary>Physical pixels per design unit. What counts its own pixels, like a wave, starts from this.</summary>
    double Scale => 1;

    /// <summary>Shifts everything drawn from now on: how the scrolled part of a page moves.</summary>
    void SetOrigin(float dy);

    void FillRect(Box box, uint rgb);

    void FillRound(Box box, float radius, uint rgb);

    /// <summary>A rounded rectangle with a border of one physical line.</summary>
    void Frame(Box box, float radius, uint fill, uint border);

    /// <summary>An outline, centred on the edge of the box.</summary>
    void Ring(Box box, float radius, float thickness, uint rgb);

    void Stroke(float ax, float ay, float bx, float by, float width, uint rgb);

    /// <summary>
    /// A whole lane of a wave in one call, in physical pixels (see <see cref="WaveGrid"/>), with nothing allocated: one bar per level,
    /// mirrored around the base line, the oldest on the left. A level is 0 to <c>WaveLane.FullLevel</c>: 0 draws the base line only,
    /// <c>WaveLane.Empty</c> draws nothing, not even a line. <paramref name="columns"/> gives the colour of each bar and, where
    /// it asks for one, a halo of <see cref="WaveGrid.Edge"/> around it, drawn as two opaque rectangles. A run of 0s draws one
    /// unbroken line in <paramref name="baseline"/>. Draws the shorter of the two spans, and at most <see cref="WaveGrid.Columns"/>.
    /// A fake canvas that does not care for it keeps this default and draws nothing.
    /// </summary>
    void Bars(in WaveGrid grid, ReadOnlySpan<byte> levels, ReadOnlySpan<BarColumn> columns, uint baseline)
    {
    }

    /// <summary>A soft light around the box, over what is already drawn.</summary>
    void Glow(Box box, float sigma, float opacity, uint rgb);

    void Text(string text, Face face, float size, float x, float top, uint rgb);

    float Measure(string text, Face face, float size);

    float LineHeight(Face face, float size);

    void WarningIcon(float x, float y, float size, uint rgb);

    /// <summary>The real notification bitmap, as it will appear.</summary>
    void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored);
}

internal static class CanvasText
{
    /// <summary>Text centred on a horizontal line.</summary>
    public static void TextMid(this ICanvas c, string text, Face face, float size, float x, float midY, uint rgb) =>
        c.Text(text, face, size, x, midY - c.LineHeight(face, size) / 2, rgb);

    public static void TextCentered(this ICanvas c, string text, Face face, float size, Box box, uint rgb) =>
        c.TextMid(text, face, size, box.MidX - c.Measure(text, face, size) / 2, box.MidY, rgb);

    public static void TextRight(this ICanvas c, string text, Face face, float size, float right, float midY, uint rgb) =>
        c.TextMid(text, face, size, right - c.Measure(text, face, size), midY, rgb);

    /// <summary>The text, cut with an ellipsis if it does not fit. Halves the lengths it tries: a file name can be 255 characters, and a list draws several per paint.</summary>
    public static string Fit(this ICanvas c, string text, Face face, float size, float width)
    {
        if (c.Measure(text, face, size) <= width)
        {
            return text;
        }

        // The longest cut that fits lies in (fits, tooLong). 0 stands for none at all.
        var fits = 0;
        var tooLong = text.Length;
        while (tooLong - fits > 1)
        {
            var length = fits + (tooLong - fits) / 2;
            if (c.Measure(Cut(text, length), face, size) <= width)
            {
                fits = length;
            }
            else
            {
                tooLong = length;
            }
        }

        return fits == 0 ? string.Empty : Cut(text, fits);
    }

    private static string Cut(string text, int length) => text[..length].TrimEnd() + "...";

    /// <summary>Words wrapped to a width. Returns the height used, in design pixels.</summary>
    public static float Wrapped(this ICanvas c, string text, Face face, float size, float x, float top, float width, uint rgb)
    {
        var lineHeight = c.LineHeight(face, size);
        var y = top;
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var attempt = line.Length == 0 ? word : line + " " + word;
            if (c.Measure(attempt, face, size) > width && line.Length > 0)
            {
                c.Text(line, face, size, x, y, rgb);
                y += lineHeight;
                line = word;
            }
            else
            {
                line = attempt;
            }
        }

        c.Text(line, face, size, x, y, rgb);
        return y + lineHeight - top;
    }

    /// <summary>One key, as in the notification. Returns the x after it.</summary>
    public static float Keycap(this ICanvas c, string key, float x, float midY)
    {
        const float height = 22;
        var width = MathF.Ceiling(c.Measure(key, Face.Mono, 12)) + 14;
        var box = new Box(x, MathF.Round(midY - height / 2), width, height);
        c.Frame(box, 3, Theme.Raised, Theme.Control);
        c.TextCentered(key, Face.Mono, 12, box, Theme.Text);
        return box.Right + 4;
    }

    public static void Keycaps(this ICanvas c, string gesture, float x, float midY)
    {
        foreach (var key in gesture.Split('+'))
        {
            x = c.Keycap(key, x, midY);
        }
    }
}

/// <summary>Virtual-key codes the screen reacts to.</summary>
internal static class Vk
{
    public const uint Back = 0x08, Tab = 0x09, Enter = 0x0D, Shift = 0x10, Ctrl = 0x11, Alt = 0x12, Escape = 0x1B, Space = 0x20,
        PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28,
        LWin = 0x5B, RWin = 0x5C, LShift = 0xA0, RShift = 0xA1, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5;

    public static bool IsModifier(uint vk) =>
        vk is Shift or Ctrl or Alt or LWin or RWin or LShift or RShift or LCtrl or RCtrl or LAlt or RAlt;
}
