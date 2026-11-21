using Hindsound.Linux.Window.Text;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Linux.Tests.Window.Text;

/// <summary>Where a string is cut between the font of the page and the fonts of the system.</summary>
public sealed class TextRunsTests
{
    // The page's font has everything below U+3000; above, a fallback.
    private static int FontOf(int codePoint) => codePoint >= 0x3000 ? 1 : 0;

    [Fact]
    public void A_name_with_Japanese_in_it_is_cut_where_the_font_changes()
    {
        var runs = TextRuns.Split("Clip \u65E5\u672C clip", FontOf);

        Assert.Equal([new TextRun(0, 5, 0), new TextRun(5, 2, 1), new TextRun(7, 5, 0)], runs);
    }

    [Fact]
    public void A_string_in_one_font_is_one_run_and_an_empty_one_none()
    {
        Assert.Equal([new TextRun(0, 8, 0)], TextRuns.Split("Settings", FontOf));
        Assert.Empty(TextRuns.Split(string.Empty, FontOf));
    }

    [Fact]
    public void A_combining_mark_stays_with_the_letter_before_it()
    {
        // U+3042 then the combining acute accent U+0301, which the page's font "has": it still goes with the fallback's letter.
        var runs = TextRuns.Split("a\u3042\u0301b", FontOf);

        Assert.Equal([new TextRun(0, 1, 0), new TextRun(1, 2, 1), new TextRun(3, 1, 0)], runs);
    }

    [Fact]
    public void A_character_beyond_the_basic_plane_is_one_code_point_and_a_lone_surrogate_does_not_throw()
    {
        var seen = new List<int>();
        var runs = TextRuns.Split("a\U0001F600\uD800z", codePoint =>
        {
            seen.Add(codePoint);
            return codePoint == 0x1F600 ? 2 : 0;
        });

        Assert.Equal([(int)'a', 0x1F600, 0xFFFD, (int)'z'], seen);
        Assert.Equal([new TextRun(0, 1, 0), new TextRun(1, 2, 2), new TextRun(3, 2, 0)], runs);
    }
}
