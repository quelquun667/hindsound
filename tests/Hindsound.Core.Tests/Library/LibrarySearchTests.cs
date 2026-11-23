using System.Globalization;
using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>
/// What the search of the Library finds. The letters with an accent are written as escapes: these tests run on Linux and Mac with
/// InvariantGlobalization, where a culture-aware comparison would fold nothing, and must not depend on how a file was saved.
/// </summary>
public sealed class LibrarySearchTests
{
    private const string EAcute = "\u00E9", UpperEAcute = "\u00C9", Grave = "\u00E8", Circumflex = "\u00EA", Cedilla = "\u00E7";

    private static ClipGroup Clip(string name) => new([new ClipFile(Path.Combine(@"Z:\no-such-folder", name), 1000, new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc))]);

    private static string[] Found(string? query, params string[] names) =>
        [.. LibraryFilter.Search([.. names.Select(Clip)], query, ClipNameOf).Select(line => Path.GetFileName(line.Main.Path))];

    private static string ClipNameOf(ClipGroup line) => Path.GetFileName(line.Main.Path);

    [Theory]
    [InlineData("e", "caf" + EAcute)]
    [InlineData("E", "caf" + EAcute)]
    [InlineData(EAcute, "cafe")]
    [InlineData(UpperEAcute, "cafe")]
    [InlineData("cafe", "CAF" + UpperEAcute)]
    [InlineData("a", "\u00E0 la maison")]
    [InlineData("naive", "na\u00EFve")]
    [InlineData("francais", "Fran" + Cedilla + "ais")]
    [InlineData("ete", "\u00E9t\u00E9")]
    [InlineData("el nino", "El Ni\u00F1o")]
    [InlineData("strasse", "Stra\u00DFe")]
    [InlineData("zazie", "\u017Da\u017Eie")]
    public void An_accent_and_a_capital_make_no_difference(string query, string name)
    {
        Assert.True(LibrarySearch.Matches(name, query));
    }

    [Fact]
    public void A_ligature_reads_as_its_two_letters_both_ways()
    {
        Assert.True(LibrarySearch.Matches("c\u0153ur", "coeur"));
        Assert.True(LibrarySearch.Matches("coeur", "c\u0153ur"));
        Assert.True(LibrarySearch.Matches("C\u0152UR", "coeur"));
        Assert.True(LibrarySearch.Matches("\u00E6sop", "aesop"));
        Assert.True(LibrarySearch.Matches("aesop", "\u00C6sop"));
        Assert.True(LibrarySearch.Matches("Stra\u00DFe", "ss"));
    }

    [Fact]
    public void A_letter_typed_apart_from_its_accent_is_the_same_as_the_one_with_it()
    {
        // e then a combining acute, as a file of a Mac may hold it.
        Assert.True(LibrarySearch.Matches("cafe\u0301", "caf" + EAcute));
        Assert.True(LibrarySearch.Matches("caf" + EAcute, "cafe\u0301"));
        Assert.Equal("cafe", LibrarySearch.Fold("cafe\u0301"));
    }

    [Fact]
    public void An_accent_is_taken_off_with_no_help_of_the_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            // The Turkish rules turn I into a dotless letter: the folding is the same whatever the culture is.
            foreach (var culture in new[] { "", "tr-TR", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.Equal("cafe", LibrarySearch.Fold("CAF" + UpperEAcute));
                Assert.Equal("i", LibrarySearch.Fold("I"));
                Assert.Equal("title", LibrarySearch.Fold("T\u0130TLE"));
                Assert.True(LibrarySearch.Matches("Indigo", "indigo"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Every_letter_with_an_accent_of_french_is_folded()
    {
        const string accented = "\u00E0\u00E2\u00E4\u00E7\u00E8\u00E9\u00EA\u00EB\u00EE\u00EF\u00F4\u00F6\u00F9\u00FB\u00FC\u00FF\u00C0\u00C2\u00C4\u00C7\u00C8\u00C9\u00CA\u00CB\u00CE\u00CF\u00D4\u00D6\u00D9\u00DB\u00DC\u0178";
        Assert.Equal("aaaceeeeiioouuuyaaaceeeeiioouuuy", LibrarySearch.Fold(accented));
    }

    [Fact]
    public void A_letter_that_is_not_latin_is_kept_and_lowered()
    {
        Assert.Equal("\u043F\u0440\u0438\u0432\u0435\u0442", LibrarySearch.Fold("\u041F\u0420\u0418\u0412\u0415\u0422"));
        Assert.True(LibrarySearch.Matches("\u0416\u0430\u0431\u0430", "\u0436\u0430"));
        Assert.False(LibrarySearch.Matches("Clip", "\u0436"));
    }

    [Fact]
    public void Words_are_all_needed_in_any_order()
    {
        Assert.True(LibrarySearch.Matches("Hindsound_Discord_2026-11-08_20-47-51", "discord 2026"));
        Assert.True(LibrarySearch.Matches("Hindsound_Discord_2026-11-08_20-47-51", "2026 discord"));
        Assert.True(LibrarySearch.Matches("Hindsound_Discord_2026-11-08_20-47-51", "47 hind  discord"));
        Assert.False(LibrarySearch.Matches("Hindsound_Discord_2026-11-08_20-47-51", "discord 2025"));
        Assert.False(LibrarySearch.Matches("Hindsound_Discord_2026-11-08_20-47-51", "zoom discord"));
    }

    [Fact]
    public void A_word_may_be_found_inside_another_and_used_twice()
    {
        Assert.True(LibrarySearch.Matches("goal of the match", "goal goal"));
        Assert.True(LibrarySearch.Matches("goal of the match", "oal atc"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t ")]
    [InlineData(null)]
    public void A_search_with_no_word_keeps_every_name(string? query)
    {
        Assert.False(LibrarySearch.IsActive(query));
        Assert.Empty(LibrarySearch.Words(query));
        Assert.True(LibrarySearch.Matches("anything", query));
        Assert.Equal(["a.mp3", "b.mp3"], Found(query, "a.mp3", "b.mp3"));
    }

    [Fact]
    public void White_space_around_and_between_words_is_ignored()
    {
        Assert.Equal(["goal", "win"], LibrarySearch.Words("  GOAL \t  Win "));
        Assert.True(LibrarySearch.IsActive(" x "));
    }

    [Fact]
    public void The_search_reads_the_name_without_its_extension()
    {
        Assert.Equal(["match.mp3"], Found("match", "match.mp3", "other.mp3"));
        Assert.Empty(Found("mp3", "match.mp3", "other.mp3"));
        Assert.Empty(Found(".mp3", "match.mp3"));

        // Only the last extension goes: a dot inside the name is part of the name.
        Assert.Equal(["v1.2 final.mp3"], Found("1.2", "v1.2 final.mp3"));
    }

    [Fact]
    public void The_lines_come_back_in_the_order_they_came_and_the_same_list_when_nothing_is_asked()
    {
        var lines = new[] { Clip("b clip.mp3"), Clip("a clip.mp3"), Clip("c other.mp3") };
        Assert.Equal(["b clip.mp3", "a clip.mp3"], LibraryFilter.Search(lines, "clip", ClipNameOf).Select(ClipNameOf));
        Assert.Same(lines, LibraryFilter.Search(lines, "  ", ClipNameOf));
        Assert.Empty(LibraryFilter.Search(lines, "nothing like it", ClipNameOf));
    }

    [Fact]
    public void The_empty_view_of_a_search_names_the_text_and_says_what_to_try()
    {
        var empty = LibraryFilter.NoMatch("goal");
        Assert.Equal("No clips match \"goal\"", empty.Title);
        Assert.Equal("Try other words, or clear the search.", empty.Body);
    }

    [Fact]
    public void Letters_with_an_accent_in_the_circumflex_and_grave_forms_match_their_plain_letters()
    {
        Assert.True(LibrarySearch.Matches("pr" + Grave + "s", "pres"));
        Assert.True(LibrarySearch.Matches("for" + Circumflex + "t", "foret"));
    }
}
