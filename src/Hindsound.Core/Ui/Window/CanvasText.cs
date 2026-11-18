namespace Hindsound.Ui.Window;

/// <summary>
/// The text of a <see cref="PixelCanvas"/>: one line at a time, no wrapping, everything in physical pixels. On Windows it is
/// <c>TextPainter</c> (DirectWrite, GDI if DirectWrite cannot start); another system brings its own, which must give the same
/// measures, since every layout of the window was placed with them.
/// <para>
/// The size of a <see cref="FontSpec"/> is the em in physical pixels and may have a fraction (13 at 125 % is 16.25): the font is
/// made at the em rounded half up to whole pixels, <c>em = floor(Size + 0.5)</c>, and every measure below is of that em.
/// </para>
/// </summary>
internal interface ICanvasText : IDisposable
{
    /// <summary>
    /// The advance of the whole string, trailing spaces included, in physical pixels, not rounded (DirectWrite's
    /// <c>widthIncludingTrailingWhitespace</c>); 0 for an empty string. GDI, when DirectWrite cannot start, gives whole pixels.
    /// </summary>
    float Measure(FontSpec font, string text);

    /// <summary>
    /// The height of one line, in whole physical pixels: <c>ascent + descent</c>, with no line gap, where
    /// <c>ascent = round(a * em)</c> and <c>descent = round(d * em)</c>. <c>a</c> and <c>d</c> are per em, each the larger of the
    /// font's own metrics (the typographic ones, for a font that asks for them, as Bahnschrift and Cascadia Mono do) and of
    /// GDI's (usWinAscent and usWinDescent of the OS/2 table, and the GDI-compatible metrics at this em): every layout of the
    /// window was centred on GDI's line, TEXTMETRIC's Height. A font missing from the system takes Segoe UI's metrics; with no
    /// font at all, <c>a</c> = 1.08 and <c>d</c> = 0.25. GDI, when DirectWrite cannot start, gives TEXTMETRIC's Height.
    /// </summary>
    float LineHeight(FontSpec font);

    /// <summary>
    /// Draws the text in <paramref name="rgb"/> (0xRRGGBB), opaque, over what is already there. <paramref name="top"/> is the top
    /// of the line box: the baseline lies at <c>top + ascent</c>, the ascent of <see cref="LineHeight"/>, snapped to a whole pixel;
    /// <paramref name="x"/> is the start of the advance and keeps its fraction. Nothing is drawn for an empty string, and
    /// nothing outside <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The canvas's bitmap. An implementation tied to one bitmap (Windows draws through its DC) takes only that one.</param>
    void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb);
}
