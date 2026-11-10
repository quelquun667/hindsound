using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class WaveGridTests
{
    // The lane of the Now page: 527 units wide, 116 units high.
    private static readonly Box Lane = new(24, 138, 527, 116);

    [Theory]
    [InlineData(1.0, 3, 6, 87)]
    [InlineData(1.25, 4, 8, 81)]
    [InlineData(1.5, 5, 10, 78)]
    [InlineData(2.0, 6, 12, 87)]
    public void The_bar_is_three_units_the_pitch_twice_the_bar_and_the_columns_fill_the_lane(double scale, int bar, int pitch, int columns)
    {
        var grid = WaveGrid.Of(Lane, scale);

        Assert.Equal(bar, grid.Bar);
        Assert.Equal(pitch, grid.Pitch);
        Assert.Equal(bar, grid.Gap);
        Assert.Equal(columns, grid.Columns);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Every_bar_and_every_gap_has_the_same_width_in_pixels_and_the_row_keeps_four_units_free_each_side(double scale)
    {
        var grid = WaveGrid.Of(Lane, scale);
        var margin = WaveGrid.Px(WaveGrid.Margin, scale);

        for (var k = 1; k < grid.Columns; k++)
        {
            Assert.Equal(grid.Pitch, grid.X(k) - grid.X(k - 1));
        }

        Assert.True(grid.Start - grid.Left >= margin);
        Assert.True(grid.Right - grid.End >= margin);
        // One more column would not have fitted.
        Assert.True(grid.Right - (grid.End + grid.Pitch) < margin);
        // Centred, give or take the odd pixel.
        Assert.InRange((grid.Start - grid.Left) - (grid.Right - grid.End), -1, 1);
    }

    [Fact]
    public void The_lane_at_100_percent_is_87_columns_on_a_pitch_of_6_and_150_percent_78_on_10()
    {
        Assert.Equal((3, 6, 87), (WaveGrid.Of(Lane, 1.0).Bar, WaveGrid.Of(Lane, 1.0).Pitch, WaveGrid.Of(Lane, 1.0).Columns));
        Assert.Equal((5, 10, 78), (WaveGrid.Of(Lane, 1.5).Bar, WaveGrid.Of(Lane, 1.5).Pitch, WaveGrid.Of(Lane, 1.5).Columns));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_halo_reaches_one_unit_rounded_and_the_base_line_is_a_whole_pixel_count(double scale)
    {
        var grid = WaveGrid.Of(Lane, scale);

        Assert.Equal((int)Math.Round(scale, MidpointRounding.AwayFromZero), grid.Edge);
        Assert.Equal(Math.Max(1, (int)scale), grid.Line);
        // Two halos side by side never touch: the gap holds both.
        Assert.True(grid.Gap >= 2 * grid.Edge);
    }

    [Fact]
    public void The_mid_row_and_the_reach_follow_the_lane_with_four_units_free_above_and_below()
    {
        var grid = WaveGrid.Of(Lane, 1.0);

        Assert.Equal(196, grid.Mid);
        Assert.Equal((116 - 8) / 2, grid.Half);
    }

    [Fact]
    public void A_lane_too_narrow_for_a_column_has_none()
    {
        Assert.Equal(0, WaveGrid.Of(new Box(0, 0, 8, 40), 1.0).Columns);
        Assert.Equal(0, WaveGrid.Of(new Box(0, 0, 0, 0), 1.0).Columns);
    }
}
