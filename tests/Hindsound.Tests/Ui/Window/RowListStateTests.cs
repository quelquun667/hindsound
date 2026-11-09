using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class RowListStateTests
{
    private static string[] Paths(int count) => Enumerable.Range(0, count).Select(i => $@"C:\Clips\clip{i:0000}.mp3").ToArray();

    private static RowListState ListOf(int count, int viewRows = 6, bool endRow = false)
    {
        var state = new RowListState { ViewRows = viewRows };
        var paths = Paths(count);
        state.SetRows(count, i => paths[i], endRow);
        return state;
    }

    private static void Read(RowListState state, IReadOnlyList<string> paths, bool endRow = false) =>
        state.SetRows(paths.Count, i => paths[i], endRow);

    // ---------------------------------------------------------------- how much is on screen

    [Fact]
    public void Of_2000_rows_only_those_that_fit_are_drawn()
    {
        var state = ListOf(2000);

        Assert.Equal(2000, state.Count);
        Assert.Equal(0, state.Top);
        Assert.Equal(6, state.DrawnRows);
        Assert.Equal(1994, state.MaxTop);
    }

    [Fact]
    public void A_list_shorter_than_the_view_draws_what_it_has_and_does_not_scroll()
    {
        var state = ListOf(3);

        Assert.Equal(3, state.DrawnRows);
        Assert.Equal(0, state.MaxTop);
        Assert.False(state.HasThumb);
        Assert.False(state.ScrollTo(5));
        Assert.Equal(0, state.Top);
    }

    [Fact]
    public void An_empty_list_has_nothing_to_draw_choose_or_scroll()
    {
        var state = ListOf(0);

        Assert.Equal(0, state.DrawnRows);
        Assert.Equal(RowListState.None, state.RowAt(10));
        Assert.False(state.Key(Vk.Down));
        Assert.False(state.Select(0));
        Assert.Equal(RowListState.None, state.Selected);
        Assert.False(state.HasThumb);
    }

    [Fact]
    public void Reading_2000_rows_again_does_not_allocate_per_row()
    {
        var state = ListOf(2000);
        var paths = Paths(2000);
        Func<int, string> pathAt = i => paths[i];
        state.SetRows(2000, pathAt);
        state.Select(1999);

        var before = GC.GetAllocatedBytesForCurrentThread();
        state.SetRows(2000, pathAt);
        var used = GC.GetAllocatedBytesForCurrentThread() - before;

        // One object per row would be at least 48 KB. The margin is for what the runtime may charge to this thread.
        Assert.True(used < 8_000, $"{used} bytes");
    }

    // ---------------------------------------------------------------- the keyboard

    [Theory]
    [InlineData(Vk.Down, 11)]
    [InlineData(Vk.Up, 9)]
    [InlineData(Vk.Home, 0)]
    [InlineData(Vk.End, 1999)]
    [InlineData(Vk.PageDown, 15)]
    [InlineData(Vk.PageUp, 5)]
    public void A_key_moves_the_choice_and_the_row_stays_on_screen(uint key, int expected)
    {
        var state = ListOf(2000);
        state.Select(10);

        Assert.True(state.Key(key));

        Assert.Equal(expected, state.Selected);
        Assert.Equal($@"C:\Clips\clip{expected:0000}.mp3", state.SelectedPath);
        Assert.InRange(state.Selected, state.Top, state.Top + state.ViewRows - 1);
    }

    [Theory]
    [InlineData(Vk.Up, 0, 0)]
    [InlineData(Vk.PageUp, 2, 0)]
    [InlineData(Vk.Down, 19, 19)]
    [InlineData(Vk.PageDown, 17, 19)]
    public void A_key_stops_at_the_ends(uint key, int from, int expected)
    {
        var state = ListOf(20);
        state.Select(from);

        Assert.True(state.Key(key));

        Assert.Equal(expected, state.Selected);
    }

    [Theory]
    [InlineData(Vk.Down)]
    [InlineData(Vk.Up)]
    public void With_nothing_chosen_an_arrow_lands_on_the_first_row_in_view(uint key)
    {
        var state = ListOf(2000);
        state.ScrollTo(500);

        state.Key(key);

        Assert.Equal(500, state.Selected);
        Assert.Equal(500, state.Top);
    }

    [Fact]
    public void A_page_moves_the_rows_with_the_choice()
    {
        var state = ListOf(2000);
        state.ScrollTo(100);
        state.Select(103);

        state.Key(Vk.PageDown);

        Assert.Equal(108, state.Selected);
        Assert.Equal(105, state.Top);
    }

    [Fact]
    public void A_row_scrolled_away_comes_back_with_the_next_arrow()
    {
        var state = ListOf(2000);
        state.Select(3);
        state.ScrollTo(900);

        state.Key(Vk.Down);

        Assert.Equal(4, state.Selected);
        Assert.Equal(4, state.Top);
    }

    [Fact]
    public void Other_keys_are_not_taken()
    {
        var state = ListOf(10);
        state.Select(2);

        Assert.False(state.Key(Vk.Space));
        Assert.False(state.Key(Vk.Left));
        Assert.False(state.Key(Vk.Enter));
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void A_view_of_one_row_still_pages()
    {
        var state = ListOf(10, viewRows: 1);
        state.Select(4);

        state.Key(Vk.PageDown);

        Assert.Equal(5, state.Selected);
        Assert.Equal(5, state.Top);
    }

    // ---------------------------------------------------------------- reading again

    [Fact]
    public void A_clip_arriving_above_leaves_the_same_row_chosen()
    {
        var state = ListOf(2000);
        state.Select(3);
        var chosen = state.SelectedPath;

        var fell = state.SetRows(2001, i => i == 0 ? @"C:\Clips\new.mp3" : $@"C:\Clips\clip{i - 1:0000}.mp3");

        Assert.False(fell);
        Assert.Equal(chosen, state.SelectedPath);
        Assert.Equal(4, state.Selected);
    }

    [Fact]
    public void A_clip_removed_above_leaves_the_same_row_chosen()
    {
        var state = ListOf(10);
        state.Select(5);

        Read(state, Paths(10).Where((_, i) => i != 1).ToArray());

        Assert.Equal(4, state.Selected);
        Assert.Equal(@"C:\Clips\clip0005.mp3", state.SelectedPath);
    }

    [Fact]
    public void A_file_renamed_only_in_its_case_stays_chosen()
    {
        var state = ListOf(10);
        state.Select(5);

        Read(state, Paths(10).Select(path => path.ToUpperInvariant()).ToArray());

        Assert.Equal(5, state.Selected);
        Assert.Equal(@"C:\CLIPS\CLIP0005.MP3", state.SelectedPath);
    }

    [Fact]
    public void A_row_that_disappeared_lets_go_of_the_choice()
    {
        var state = ListOf(10);
        state.Select(5);

        var fell = state.SetRows(9, i => $@"C:\Clips\clip{(i < 5 ? i : i + 1):0000}.mp3");

        Assert.True(fell);
        Assert.Equal(RowListState.None, state.Selected);
        Assert.Null(state.SelectedPath);
    }

    [Fact]
    public void Without_a_choice_a_new_reading_chooses_nothing()
    {
        var state = ListOf(10);

        var fell = state.SetRows(11, i => $@"C:\Clips\other{i}.mp3");

        Assert.False(fell);
        Assert.Equal(RowListState.None, state.Selected);
    }

    [Fact]
    public void A_list_at_its_head_stays_there_so_the_new_clip_shows()
    {
        var state = ListOf(100);

        state.SetRows(101, i => i == 0 ? @"C:\Clips\new.mp3" : $@"C:\Clips\clip{i - 1:0000}.mp3");

        Assert.Equal(0, state.Top);
    }

    [Fact]
    public void Further_down_the_top_row_stays_on_top_when_a_clip_arrives()
    {
        var state = ListOf(100);
        state.ScrollTo(40);

        state.SetRows(101, i => i == 0 ? @"C:\Clips\new.mp3" : $@"C:\Clips\clip{i - 1:0000}.mp3");

        Assert.Equal(41, state.Top);
    }

    [Fact]
    public void A_chosen_row_pushed_off_the_screen_is_brought_back()
    {
        var state = ListOf(100);
        state.Select(5);

        state.SetRows(101, i => i == 0 ? @"C:\Clips\new.mp3" : $@"C:\Clips\clip{i - 1:0000}.mp3");

        Assert.Equal(6, state.Selected);
        Assert.Equal(1, state.Top);
    }

    [Fact]
    public void A_chosen_row_the_user_scrolled_away_from_is_left_away()
    {
        var state = ListOf(100);
        state.Select(5);
        state.ScrollTo(50);

        state.SetRows(101, i => i == 0 ? @"C:\Clips\new.mp3" : $@"C:\Clips\clip{i - 1:0000}.mp3");

        Assert.Equal(6, state.Selected);
        Assert.Equal(51, state.Top);
    }

    [Fact]
    public void A_list_that_shrinks_under_the_view_keeps_its_scroll_in_bounds()
    {
        var state = ListOf(100);
        state.Select(99);
        Assert.Equal(94, state.Top);

        Read(state, Paths(10));

        Assert.Equal(4, state.Top);
        Assert.Equal(RowListState.None, state.Selected);
    }

    [Fact]
    public void A_list_read_empty_has_no_scroll_and_no_choice()
    {
        var state = ListOf(100);
        state.Select(50);

        var fell = state.SetRows(0, _ => throw new InvalidOperationException("no row to ask for"), endRow: true);

        Assert.True(fell);
        Assert.Equal(0, state.Top);
        Assert.False(state.HasEndRow);
    }

    // ---------------------------------------------------------------- choosing from outside

    [Fact]
    public void Choosing_a_path_brings_it_on_screen()
    {
        var state = ListOf(2000);

        Assert.True(state.SelectPath(@"C:\Clips\clip1500.mp3"));

        Assert.Equal(1500, state.Selected);
        Assert.InRange(1500, state.Top, state.Top + 5);
    }

    [Fact]
    public void A_path_not_listed_leaves_the_choice_as_it_is()
    {
        var state = ListOf(20);
        state.Select(4);

        Assert.False(state.SelectPath(@"C:\Clips\none.mp3"));

        Assert.Equal(4, state.Selected);
    }

    [Fact]
    public void Choosing_the_row_already_chosen_is_no_change()
    {
        var state = ListOf(20);

        Assert.True(state.Select(4));
        Assert.False(state.Select(4));
        Assert.False(state.Select(-1));
        Assert.False(state.Select(20));
        Assert.Equal(4, state.Selected);
    }

    [Fact]
    public void Deselecting_forgets_the_row()
    {
        var state = ListOf(20);
        state.Select(4);

        state.Deselect();

        Assert.Equal(RowListState.None, state.Selected);
        Assert.Null(state.SelectedPath);
        Assert.False(state.RevealSelected());
    }

    // ---------------------------------------------------------------- scrolling

    [Fact]
    public void The_wheel_stops_at_both_ends_and_leaves_the_choice_alone()
    {
        var state = ListOf(20);
        state.Select(1);

        Assert.False(state.Wheel(1));
        Assert.Equal(0, state.Top);

        Assert.True(state.Wheel(-1));
        Assert.Equal(2, state.Top);

        state.Wheel(-1000);
        Assert.Equal(14, state.Top);
        Assert.False(state.Wheel(-1));

        state.Wheel(1000);
        Assert.Equal(0, state.Top);
        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void Scrolling_to_where_it_is_reports_no_move()
    {
        var state = ListOf(20);

        Assert.True(state.ScrollTo(3));
        Assert.False(state.ScrollTo(3));
        state.ScrollTo(int.MaxValue);
        Assert.Equal(14, state.Top);
        state.ScrollTo(int.MinValue);
        Assert.Equal(0, state.Top);
    }

    [Fact]
    public void A_larger_view_pulls_the_top_back_and_a_view_is_never_empty()
    {
        var state = ListOf(10);
        state.ScrollTo(4);

        state.ViewRows = 8;
        Assert.Equal(2, state.Top);

        state.ViewRows = 0;
        Assert.Equal(1, state.ViewRows);
    }

    // ---------------------------------------------------------------- the row under a point

    [Theory]
    [InlineData(-0.5f, RowListState.None)]
    [InlineData(0f, 100)]
    [InlineData(39.9f, 100)]
    [InlineData(40f, 101)]
    [InlineData(239.9f, 105)]
    [InlineData(240f, RowListState.None)]
    public void The_row_under_a_point_counts_from_the_top_row(float y, int expected)
    {
        var state = ListOf(2000);
        state.ScrollTo(100);

        Assert.Equal(expected, state.RowAt(y));
    }

    [Fact]
    public void Below_the_last_row_there_is_no_row()
    {
        var state = ListOf(2);

        Assert.Equal(1, state.RowAt(79));
        Assert.Equal(RowListState.None, state.RowAt(80));
    }

    [Fact]
    public void The_hover_only_counts_as_a_move_on_another_row()
    {
        var state = ListOf(20);

        Assert.True(state.TrackHover(3));
        Assert.False(state.TrackHover(3));
        Assert.True(state.TrackHover(RowListState.None));
    }

    // ---------------------------------------------------------------- the slot after the last row

    [Fact]
    public void The_end_slot_scrolls_like_a_row_but_cannot_be_chosen()
    {
        var state = ListOf(10, endRow: true);

        Assert.Equal(5, state.MaxTop);
        Assert.False(state.EndRowOnScreen);

        state.ScrollTo(5);

        Assert.True(state.EndRowOnScreen);
        Assert.Equal(5, state.DrawnRows);
        Assert.Equal(9, state.RowAt(4 * RowListState.RowHeight));
        Assert.Equal(RowListState.None, state.RowAt(5 * RowListState.RowHeight));
    }

    [Theory]
    [InlineData(Vk.End, 8)]
    [InlineData(Vk.Down, 8)]
    [InlineData(Vk.PageDown, 8)]
    public void Reaching_the_last_row_by_the_keyboard_shows_the_sentence(uint key, int from)
    {
        var state = ListOf(10, endRow: true);
        state.Select(from);

        state.Key(key);

        Assert.Equal(9, state.Selected);
        Assert.True(state.EndRowOnScreen);
    }

    [Fact]
    public void A_click_on_the_last_row_in_view_does_not_move_it_from_under_the_mouse()
    {
        var state = ListOf(10, endRow: true);
        state.ScrollTo(4);

        state.Select(9);

        Assert.Equal(4, state.Top);
    }

    [Fact]
    public void Only_a_list_with_rows_has_an_end_slot()
    {
        Assert.False(ListOf(0, endRow: true).HasEndRow);
        Assert.True(ListOf(1, endRow: true).HasEndRow);
    }

    // ---------------------------------------------------------------- the thumb

    [Fact]
    public void The_thumb_is_never_too_small_to_catch_and_ends_at_the_end_of_the_track()
    {
        var state = ListOf(2000);
        const float track = 232;

        Assert.Equal(32, state.ThumbHeight(track));
        Assert.Equal(0, state.ThumbY(track));

        state.ScrollTo(state.MaxTop);

        Assert.Equal(track - 32, state.ThumbY(track), 3);
    }

    [Fact]
    public void A_short_list_has_a_thumb_in_proportion()
    {
        var state = ListOf(12);

        Assert.Equal(116, state.ThumbHeight(232), 3);
        Assert.Equal(232, ListOf(1, viewRows: 40).ThumbHeight(232), 3);
    }

    [Theory]
    [InlineData(-500f, 0)]
    [InlineData(5000f, 1994)]
    public void Dragging_the_thumb_past_the_track_stops_at_the_ends(float y, int expected)
    {
        var state = ListOf(2000);
        state.ScrollTo(1000);
        state.GrabThumb(state.ThumbY(232) + 1, 232);

        state.DragThumb(y, 232);

        Assert.Equal(expected, state.Top);
    }

    [Fact]
    public void Grabbing_the_thumb_does_not_move_the_list()
    {
        var state = ListOf(2000);
        state.ScrollTo(1000);

        state.GrabThumb(state.ThumbY(232) + 10, 232);

        Assert.Equal(1000, state.Top);
        Assert.True(state.ThumbDragging);
    }

    [Fact]
    public void A_press_on_the_track_brings_the_thumb_under_the_mouse()
    {
        var state = ListOf(2000);

        state.GrabThumb(116, 232);

        Assert.InRange(state.Top, 900, 1100);
    }

    // ---------------------------------------------------------------- a reader that reads a field

    [Fact]
    public void A_reader_of_a_replaced_field_is_not_asked_about_rows_the_list_no_longer_has()
    {
        var rows = Paths(20);
        var state = new RowListState { ViewRows = 6 };
        state.SetRows(rows.Length, i => rows[i]);
        state.ScrollTo(10);

        rows = ["row0", "row1"];
        state.SetRows(rows.Length, i => rows[i]);

        Assert.Equal(0, state.Top);
        Assert.Equal(2, state.Count);
    }

    [Fact]
    public void A_reader_of_a_replaced_field_keeps_the_top_row_on_top_when_a_clip_arrives_above()
    {
        var rows = Paths(20);
        var state = new RowListState { ViewRows = 6 };
        state.SetRows(rows.Length, i => rows[i]);
        state.ScrollTo(10);

        rows = ["new", .. rows];
        state.SetRows(rows.Length, i => rows[i]);

        Assert.Equal(11, state.Top);
    }

    [Fact]
    public void The_top_row_is_followed_through_the_wheel_the_keyboard_and_the_view()
    {
        var rows = Paths(40);
        var state = new RowListState { ViewRows = 6 };
        state.SetRows(rows.Length, i => rows[i]);

        state.Wheel(-3);
        state.ViewRows = 4;
        state.Select(30);
        rows = ["new", .. rows];
        state.SetRows(rows.Length, i => rows[i]);

        Assert.Equal(@"C:\Clips\clip0030.mp3", rows[state.Selected]);
        Assert.Equal(@"C:\Clips\clip0027.mp3", rows[state.Top]);
    }
}
