using System.Globalization;
using System.Text;

namespace Hindsound.Linux.Window.Text;

/// <summary>One stretch of a string drawn with one font: the font of the page, or a font of the system for what it lacks.</summary>
internal readonly record struct TextRun(int Start, int Length, int Font);

/// <summary>Cuts a string where the font that has its characters changes.</summary>
internal static class TextRuns
{
    /// <param name="fontOf">The font of one character (a code point): 0 for the font of the page, another number for a fallback.</param>
    /// <returns>The runs in order, covering the whole string. A combining mark, a joiner, a variation selector or a control stays
    /// in the run before it, with the letter it belongs to.</returns>
    public static List<TextRun> Split(string text, Func<int, int> fontOf)
    {
        var runs = new List<TextRun>();
        var start = 0;
        var font = -1;
        var at = 0;
        while (at < text.Length)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(at), out var rune, out var used) != System.Buffers.OperationStatus.Done)
            {
                rune = Rune.ReplacementChar;
                used = 1;
            }

            var next = font >= 0 && Follows(rune) ? font : fontOf(rune.Value);
            if (next != font)
            {
                if (font >= 0)
                {
                    runs.Add(new TextRun(start, at - start, font));
                }

                start = at;
                font = next;
            }

            at += used;
        }

        if (font >= 0)
        {
            runs.Add(new TextRun(start, text.Length - start, font));
        }

        return runs;
    }

    private static bool Follows(Rune rune) => Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
        or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format or UnicodeCategory.Control;
}
