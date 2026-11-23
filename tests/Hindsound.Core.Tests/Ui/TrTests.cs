using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui;

/// <summary>The texts in both languages, and what changes with the language besides the words: numbers and sizes.</summary>
public sealed class TrTests
{
    [Fact]
    public void English_until_something_sets_it_and_a_test_cannot_set_the_whole_process()
    {
        Assert.Equal(UiLanguage.English, Tr.Language);
        Assert.Equal("Play", Tr.Of("Play", "Lire"));

        // Without a scope, and without the app's entry point, nothing changes: the tests that run beside this one stay English.
        Assert.False(Tr.Set(UiLanguage.French));
        Assert.Equal(UiLanguage.English, Tr.Language);
    }

    [Fact]
    public void A_scope_holds_its_language_and_Set_changes_it_and_says_so()
    {
        using (Tr.Use(UiLanguage.English))
        {
            Assert.True(Tr.Set(UiLanguage.French));
            Assert.False(Tr.Set(UiLanguage.French));
            Assert.Equal("Lire", Tr.Of("Play", "Lire"));
        }

        Assert.Equal(UiLanguage.English, Tr.Language);
    }

    [Fact]
    public void A_scope_is_not_seen_by_a_thread_that_did_not_start_inside_it()
    {
        using var scope = Tr.Use(UiLanguage.French);
        UiLanguage? seen = null;
        using (ExecutionContext.SuppressFlow())
        {
            var other = new Thread(() => seen = Tr.Language);
            other.Start();
            other.Join();
        }

        Assert.Equal(UiLanguage.English, seen);
        Assert.Equal(UiLanguage.French, Tr.Language);
    }

    [Fact]
    public void A_text_with_values_is_formatted_with_the_invariant_culture_in_either_language()
    {
        var size = 1.5;
        Assert.Equal("Clip saved (1.5)", Tr.Format($"Clip saved ({size})", $"Clip enregistré ({size})"));
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Clip enregistré (1.5)", Tr.Format($"Clip saved ({size})", $"Clip enregistré ({size})"));
    }

    [Theory]
    [InlineData(UiLanguage.English, 0, "0")]
    [InlineData(UiLanguage.English, 999, "999")]
    [InlineData(UiLanguage.English, 2000, "2,000")]
    [InlineData(UiLanguage.English, 1234567, "1,234,567")]
    [InlineData(UiLanguage.French, 999, "999")]
    [InlineData(UiLanguage.French, 2000, "2 000")]
    [InlineData(UiLanguage.French, 1234567, "1 234 567")]
    [InlineData(UiLanguage.French, -2000, "-2 000")]
    public void A_whole_number_has_the_thousands_separator_of_its_language(UiLanguage language, long value, string expected)
    {
        using var scope = Tr.Use(language);
        Assert.Equal(expected, Tr.Number(value));
    }

    [Theory]
    [InlineData(UiLanguage.English, 1.44, "0.0", SizeUnit.MB, "1.4 MB")]
    [InlineData(UiLanguage.French, 1.44, "0.0", SizeUnit.MB, "1,4 Mo")]
    [InlineData(UiLanguage.English, 720, "0", SizeUnit.KB, "720 KB")]
    [InlineData(UiLanguage.French, 720, "0", SizeUnit.KB, "720 Ko")]
    [InlineData(UiLanguage.English, 2.25, "0.00", SizeUnit.GB, "2.25 GB")]
    [InlineData(UiLanguage.French, 2.25, "0.00", SizeUnit.GB, "2,25 Go")]
    [InlineData(UiLanguage.French, 1500.5, "#,0.0", SizeUnit.MB, "1 500,5 Mo")]
    public void A_size_has_the_decimal_mark_and_the_unit_of_its_language(UiLanguage language, double value, string format, SizeUnit unit, string expected)
    {
        using var scope = Tr.Use(language);
        Assert.Equal(expected, Tr.Size(value, format, unit));
    }

    [Fact]
    public void The_sizes_of_the_window_follow_the_language()
    {
        Assert.Equal("1.4 MB", StatusText.FileSize(1_400_000));
        Assert.Equal("~87 MB", StatusText.Megabytes(86_688_000));
        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("1,4 Mo", StatusText.FileSize(1_400_000));
        Assert.Equal("720 Ko", StatusText.FileSize(720_000));
        Assert.Equal("~87 Mo", StatusText.Megabytes(86_688_000));
    }
}
