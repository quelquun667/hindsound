using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>ICanvas.Bars on a real bitmap: the pixels, not the calls.</summary>
public class WaveBarsTests
{
    private const uint Ground = Theme.Panel, Bar = 0x2EF2C4, Other = 0xFFB547, Halo = 0x1D5A4E, Line = Theme.Line;
    private static readonly Box Lane = new(24, 138, 527, 116);

    private static unsafe uint At(GdiCanvas canvas, int x, int y) => canvas.Bits[y * canvas.Width + x] & 0xFFFFFF;

    private static GdiCanvas Canvas(double scale)
    {
        var canvas = new GdiCanvas((int)(600 * scale), (int)(300 * scale), scale, null);
        canvas.FillRect(new Box(0, 0, 600, 300), Ground);
        return canvas;
    }

    private static BarColumn[] Colors(int count, uint color, bool halo = false)
    {
        var columns = new BarColumn[count];
        Array.Fill(columns, halo ? new BarColumn(color, Halo) : new BarColumn(color));
        return columns;
    }

    private static byte[] Levels(int count, byte level)
    {
        var levels = new byte[count];
        Array.Fill(levels, level);
        return levels;
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Every_bar_and_every_gap_has_one_width_in_pixels(double scale)
    {
        using var canvas = Canvas(scale);
        var grid = WaveGrid.Of(Lane, scale);

        canvas.Bars(grid, Levels(grid.Columns, WaveLane.FullLevel), Colors(grid.Columns, Bar), Line);

        var row = grid.Mid - 3;
        var runs = new List<(bool Bar, int Width)>();
        for (var x = grid.Left; x < grid.Right; x++)
        {
            var isBar = At(canvas, x, row) == Bar;
            if (runs.Count > 0 && runs[^1].Bar == isBar)
            {
                runs[^1] = (isBar, runs[^1].Width + 1);
            }
            else
            {
                runs.Add((isBar, 1));
            }
        }

        var bars = runs.Where(r => r.Bar).ToList();
        Assert.Equal(grid.Columns, bars.Count);
        Assert.All(bars, r => Assert.Equal(grid.Bar, r.Width));
        // The gaps between two bars; the two ends are the free margin.
        var gaps = runs.Skip(1).Take(runs.Count - 2).Where(r => !r.Bar).ToList();
        Assert.Equal(grid.Columns - 1, gaps.Count);
        Assert.All(gaps, r => Assert.Equal(grid.Gap, r.Width));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void A_bar_is_mirrored_around_the_base_line_and_the_loudest_reaches_the_limit(double scale)
    {
        using var canvas = Canvas(scale);
        var grid = WaveGrid.Of(Lane, scale);
        var x = grid.X(5);

        canvas.Bars(grid, new byte[] { 0, 0, 0, 0, 0, WaveLane.FullLevel }, Colors(6, Bar), Line);

        var top = grid.Mid - grid.Half;
        var bottom = grid.Mid + grid.Half + grid.Line;
        Assert.Equal(Ground, At(canvas, x, top - 1));
        Assert.Equal(Bar, At(canvas, x, top));
        Assert.Equal(Bar, At(canvas, x, bottom - 1));
        Assert.Equal(Ground, At(canvas, x, bottom));
        // Above the base line there are as many rows as below it, and the line itself sits in the middle.
        Assert.Equal(grid.Mid - top, bottom - grid.Line - grid.Mid);
    }

    [Fact]
    public void A_level_in_between_gives_a_proportional_half_height_and_a_quiet_one_still_shows()
    {
        using var canvas = Canvas(1.0);
        var grid = WaveGrid.Of(Lane, 1.0);

        canvas.Bars(grid, new byte[] { 127, 1 }, Colors(2, Bar), Line);

        var half = (int)Math.Round(127 / (float)WaveLane.FullLevel * grid.Half);
        Assert.Equal(Bar, At(canvas, grid.X(0), grid.Mid - half));
        Assert.Equal(Ground, At(canvas, grid.X(0), grid.Mid - half - 1));
        Assert.Equal(Bar, At(canvas, grid.X(1), grid.Mid - 1));
        Assert.Equal(Ground, At(canvas, grid.X(1), grid.Mid - 2));
    }

    [Fact]
    public void Empty_draws_nothing_at_all_not_even_the_line()
    {
        using var canvas = Canvas(1.5);
        var grid = WaveGrid.Of(Lane, 1.5);

        canvas.Bars(grid, Levels(grid.Columns, WaveLane.Empty), Colors(grid.Columns, Bar, halo: true), Line);

        var changed = 0;
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                changed += At(canvas, x, y) == Ground ? 0 : 1;
            }
        }

        Assert.Equal(0, changed);
    }

    [Fact]
    public void Zero_draws_the_base_line_in_the_given_colour_and_nothing_above_or_below()
    {
        using var canvas = Canvas(1.5);
        var grid = WaveGrid.Of(Lane, 1.5);

        canvas.Bars(grid, new byte[] { 0 }, Colors(1, Bar, halo: true), Line);

        var x = grid.X(0);
        for (var dx = 0; dx < grid.Bar; dx++)
        {
            Assert.Equal(Line, At(canvas, x + dx, grid.Mid));
            Assert.Equal(Ground, At(canvas, x + dx, grid.Mid - 1));
            Assert.Equal(Ground, At(canvas, x + dx, grid.Mid + grid.Line));
        }

        Assert.Equal(Ground, At(canvas, x - 1, grid.Mid));
        Assert.Equal(Ground, At(canvas, x + grid.Bar, grid.Mid));
    }

    [Fact]
    public void A_run_of_zeros_is_one_unbroken_line_that_stops_at_the_last_bar_and_does_not_leak_beside_a_bar()
    {
        using var canvas = Canvas(1.0);
        var grid = WaveGrid.Of(Lane, 1.0);

        canvas.Bars(grid, new byte[] { 0, 0, 0, 100, 0 }, Colors(5, Bar), Line);

        for (var x = grid.X(0); x < grid.X(2) + grid.Bar; x++)
        {
            Assert.Equal(Line, At(canvas, x, grid.Mid));
        }

        // The gap before the loud bar and the gap after it stay clear.
        Assert.Equal(Ground, At(canvas, grid.X(2) + grid.Bar, grid.Mid));
        Assert.Equal(Ground, At(canvas, grid.X(3) + grid.Bar, grid.Mid));
        Assert.Equal(Line, At(canvas, grid.X(4), grid.Mid));
        Assert.Equal(Ground, At(canvas, grid.X(4) + grid.Bar, grid.Mid));
    }

    [Fact]
    public void An_empty_column_breaks_a_run_of_zeros()
    {
        using var canvas = Canvas(1.0);
        var grid = WaveGrid.Of(Lane, 1.0);

        canvas.Bars(grid, new byte[] { 0, WaveLane.Empty, 0 }, Colors(3, Bar), Line);

        Assert.Equal(Line, At(canvas, grid.X(0), grid.Mid));
        Assert.Equal(Ground, At(canvas, grid.X(0) + grid.Bar, grid.Mid));
        Assert.Equal(Ground, At(canvas, grid.X(1), grid.Mid));
        Assert.Equal(Line, At(canvas, grid.X(2), grid.Mid));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_halo_is_two_opaque_rectangles_one_unit_rounded_beyond_the_bar_and_only_where_asked(double scale)
    {
        using var canvas = Canvas(scale);
        var grid = WaveGrid.Of(Lane, scale);
        var columns = new[] { new BarColumn(Bar, Halo), new BarColumn(Bar) };

        canvas.Bars(grid, new byte[] { 200, 200 }, columns, Line);

        var h = (int)Math.Round(200 / (float)WaveLane.FullLevel * grid.Half);
        int top = grid.Mid - h, bottom = grid.Mid + h + grid.Line, x = grid.X(0), e = grid.Edge;
        // Left, right, above and below the bar.
        Assert.Equal(Halo, At(canvas, x - e, grid.Mid));
        Assert.Equal(Halo, At(canvas, x + grid.Bar + e - 1, grid.Mid));
        Assert.Equal(Halo, At(canvas, x, top - e));
        Assert.Equal(Halo, At(canvas, x + grid.Bar - 1, bottom + e - 1));
        Assert.Equal(Ground, At(canvas, x - e - 1, grid.Mid));
        Assert.Equal(Ground, At(canvas, x, top - e - 1));
        // The corners are left clear: two rectangles make a cross.
        Assert.Equal(Ground, At(canvas, x - e, top - e));
        Assert.Equal(Ground, At(canvas, x + grid.Bar + e - 1, bottom + e - 1));
        // The bar itself is on top of it.
        Assert.Equal(Bar, At(canvas, x, top));

        // The next column asked for none.
        var next = grid.X(1);
        Assert.Equal(Ground, At(canvas, next - e, grid.Mid));
        Assert.Equal(Ground, At(canvas, next, top - 1));
        Assert.Equal(Bar, At(canvas, next, top));
    }

    [Fact]
    public void The_halos_of_neighbours_never_cover_a_bar()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            using var canvas = Canvas(scale);
            var grid = WaveGrid.Of(Lane, scale);
            var count = grid.Columns;
            canvas.Bars(grid, Levels(count, 180), Colors(count, Bar, halo: true), Line);

            // Every bar still has its full width after its neighbours' halos.
            var h = (int)Math.Round(180 / (float)WaveLane.FullLevel * grid.Half);
            for (var k = 0; k < count; k++)
            {
                for (var dx = 0; dx < grid.Bar; dx++)
                {
                    Assert.Equal(Bar, At(canvas, grid.X(k) + dx, grid.Mid - h));
                }
            }
        }
    }

    [Fact]
    public void Each_column_has_its_own_colour()
    {
        using var canvas = Canvas(1.0);
        var grid = WaveGrid.Of(Lane, 1.0);

        canvas.Bars(grid, new byte[] { 100, 100, 100 }, new[] { new BarColumn(Bar), new BarColumn(Other), new BarColumn(Bar) }, Line);

        Assert.Equal(Bar, At(canvas, grid.X(0), grid.Mid));
        Assert.Equal(Other, At(canvas, grid.X(1), grid.Mid));
        Assert.Equal(Bar, At(canvas, grid.X(2), grid.Mid));
    }

    [Fact]
    public void Only_the_shorter_span_and_only_the_columns_that_fit_are_drawn()
    {
        using var canvas = Canvas(1.0);
        var grid = WaveGrid.Of(Lane, 1.0);

        canvas.Bars(grid, Levels(grid.Columns + 20, 100), Colors(2, Bar), Line);

        Assert.Equal(Bar, At(canvas, grid.X(1), grid.Mid));
        Assert.Equal(Ground, At(canvas, grid.X(2), grid.Mid));

        canvas.Bars(grid, Levels(grid.Columns + 20, 100), Colors(grid.Columns + 20, Other), Line);
        Assert.Equal(Other, At(canvas, grid.X(grid.Columns - 1), grid.Mid));
        Assert.Equal(Ground, At(canvas, grid.End + grid.Gap, grid.Mid));
    }

    [Fact]
    public void The_origin_of_a_scrolled_page_moves_the_lane()
    {
        using var canvas = Canvas(1.5);
        var grid = WaveGrid.Of(Lane, 1.5);
        canvas.SetOrigin(-60);

        canvas.Bars(grid, new byte[] { 100 }, Colors(1, Bar), Line);

        var shift = (int)Math.Round(-60 * 1.5f);
        Assert.Equal(Bar, At(canvas, grid.X(0), grid.Mid + shift));
        Assert.Equal(Ground, At(canvas, grid.X(0), grid.Mid));
    }

    [Fact]
    public void A_lane_wider_than_one_pass_draws_every_column_and_keeps_a_line_across_the_join()
    {
        var wide = new Box(0, 138, 8000, 116);
        using var canvas = new GdiCanvas(8100, 300, 1.0, null);
        canvas.FillRect(new Box(0, 0, 8100, 300), Ground);
        var grid = WaveGrid.Of(wide, 1.0);
        Assert.True(grid.Columns > 1100);
        var levels = Levels(grid.Columns, 150);
        // Silence on both sides of the 512th column.
        levels[510] = levels[511] = levels[512] = levels[513] = 0;

        canvas.Bars(grid, levels, Colors(grid.Columns, Bar, halo: true), Line);

        for (var k = 0; k < grid.Columns; k++)
        {
            var silent = k is >= 510 and <= 513;
            Assert.Equal(silent ? Line : Bar, At(canvas, grid.X(k), grid.Mid));
            Assert.Equal(silent ? Ground : Bar, At(canvas, grid.X(k), grid.Mid - 20));
        }

        // The line runs on through the gaps of the run, including the one at the join.
        for (var x = grid.X(510); x < grid.X(513) + grid.Bar; x++)
        {
            Assert.Equal(Line, At(canvas, x, grid.Mid));
        }
    }

    [Fact]
    public void A_canvas_that_does_not_draw_bars_takes_the_call_and_ignores_it()
    {
        ICanvas fake = new FakeCanvas();

        fake.Bars(WaveGrid.Of(Lane, 1.0), new byte[] { 10 }, Colors(1, Bar), Line);
    }

    [Fact]
    public void Bars_past_the_edge_of_the_bitmap_are_cut_not_a_crash()
    {
        using var canvas = new GdiCanvas(100, 60, 1.0, null);
        var grid = WaveGrid.Of(Lane, 1.0);

        canvas.Bars(grid, Levels(grid.Columns, WaveLane.FullLevel), Colors(grid.Columns, Bar, halo: true), Line);
    }
}
