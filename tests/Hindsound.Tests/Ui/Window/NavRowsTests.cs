using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The lines of the side bar and their heights, as drawn on the approved picture.</summary>
public class NavRowsTests
{
    private static readonly Section[] Pages = [Section.Hotkeys, Section.Durations, Section.Audio, Section.Export, Section.General];

    // Library has no section yet: any section stands in for its line.
    private static readonly NavRows TwoViews = new([new(Section.Now, "Now"), new(Section.UpdateCard, "Library")]);

    [Fact]
    public void Without_now_there_are_the_five_pages_and_no_caption()
    {
        var rows = NavRows.SettingsOnly;

        Assert.Equal(Pages, rows.Rows.Select(row => row.Section));
        Assert.Equal(["Hotkeys", "Durations", "Audio", "Export", "General"], rows.Rows.Select(row => row.Label));
        Assert.Null(rows.CaptionMidY);
        Assert.Equal(5 * 36 + 4 * 4, rows.Height);
        Assert.Equal([0f, 40f, 80f, 120f, 160f], Enumerable.Range(0, 5).Select(rows.TopOf));
    }

    [Fact]
    public void With_now_it_comes_first_and_the_caption_sits_above_the_pages()
    {
        var rows = NavRows.WithViews;

        Assert.Equal("Now", rows.Rows[0].Label);
        Assert.Equal([Section.Now, .. Pages], rows.Rows.Select(row => row.Section));
        Assert.Equal(54f, rows.CaptionMidY);
        Assert.Equal([0f, 64f, 104f, 144f, 184f, 224f], Enumerable.Range(0, 6).Select(rows.TopOf));
        Assert.Equal(260, rows.Height);
    }

    [Fact]
    public void The_line_of_library_will_give_the_heights_of_the_picture()
    {
        // On the picture: Now at 88, Library at 128, the caption around 182, Hotkeys at 192, from a list that starts at 88.
        Assert.Equal(0f, TwoViews.TopOf(0));
        Assert.Equal(40f, TwoViews.TopOf(1));
        Assert.Equal(94f, TwoViews.CaptionMidY);
        Assert.Equal(104f, TwoViews.TopOf(2));
        Assert.Equal(300, TwoViews.Height);
    }

    [Fact]
    public void Everything_fits_above_the_foot()
    {
        Assert.True(SettingsScreen.BandHeight + 16 + TwoViews.Height <= SettingsScreen.ViewBottom);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(35.9f, 0)]
    [InlineData(36, -1)]
    [InlineData(54, -1)]
    [InlineData(63.9f, -1)]
    [InlineData(64, 1)]
    [InlineData(99.9f, 1)]
    [InlineData(100, -1)]
    [InlineData(104, 2)]
    [InlineData(259.9f, 5)]
    [InlineData(260, -1)]
    [InlineData(-1, -1)]
    public void A_point_is_on_a_line_or_between_two(float y, int line)
    {
        Assert.Equal(line, NavRows.WithViews.IndexAt(y));
    }

    [Fact]
    public void The_five_lines_answer_as_they_always_did()
    {
        Assert.Equal(0, NavRows.SettingsOnly.IndexAt(0));
        Assert.Equal(-1, NavRows.SettingsOnly.IndexAt(37));
        Assert.Equal(1, NavRows.SettingsOnly.IndexAt(40));
        Assert.Equal(4, NavRows.SettingsOnly.IndexAt(195));
        Assert.Equal(-1, NavRows.SettingsOnly.IndexAt(196));
    }

    [Fact]
    public void A_section_finds_its_line_and_a_card_has_none()
    {
        Assert.Equal(0, NavRows.WithViews.IndexOf(Section.Now));
        Assert.Equal(5, NavRows.WithViews.IndexOf(Section.General));
        Assert.Equal(-1, NavRows.SettingsOnly.IndexOf(Section.Now));
        Assert.Equal(-1, NavRows.WithViews.IndexOf(Section.FirstRun));
        Assert.Equal(-1, NavRows.WithViews.IndexOf(Section.UpdateCard));
    }

    [Fact]
    public void Only_the_five_pages_count_as_pages_of_the_settings()
    {
        Assert.All(Pages, page => Assert.True(NavRows.IsSettingsPage(page)));
        Assert.Equal(Pages, Enum.GetValues<Section>().Where(NavRows.IsSettingsPage));
    }
}
