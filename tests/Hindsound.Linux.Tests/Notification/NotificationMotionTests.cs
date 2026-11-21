using Hindsound.Unix.Notification;

namespace Hindsound.Linux.Tests.Notification;

/// <summary>The times and the moves of the drawn notification: those of the Windows one.</summary>
public sealed class NotificationMotionTests
{
    [Fact]
    public void It_comes_out_in_140_ms_holds_until_2740_and_is_gone_at_2920()
    {
        Assert.Equal((140, 200, 2740, 2920), (NotificationMotion.EnterMs, NotificationMotion.BarsMs, NotificationMotion.HoldEndMs, NotificationMotion.TotalMs));
        Assert.Equal(0, NotificationMotion.Visible(0), 6);
        Assert.Equal(1 - 0.125, NotificationMotion.Visible(70), 6);
        Assert.Equal(1, NotificationMotion.Visible(140), 6);
        Assert.Equal(1, NotificationMotion.Visible(2739), 6);
        Assert.Equal(1 - 0.125, NotificationMotion.Visible(2830), 6);
        Assert.Equal(0, NotificationMotion.Visible(2920), 6);
        Assert.Equal(0, NotificationMotion.Visible(5000), 6);
    }

    [Fact]
    public void The_bars_rise_from_5_percent_in_200_ms()
    {
        Assert.Equal(0.05f, NotificationMotion.BarsScale(0), 5);
        Assert.Equal(0.525f, NotificationMotion.BarsScale(100), 5);
        Assert.Equal(1f, NotificationMotion.BarsScale(200), 5);
        Assert.Equal(1f, NotificationMotion.BarsScale(2900), 5);
    }

    [Fact]
    public void It_ticks_each_frame_while_it_moves_and_every_50_ms_while_it_holds()
    {
        Assert.Equal(16, NotificationMotion.NextTick(0));
        Assert.Equal(16, NotificationMotion.NextTick(199));
        Assert.Equal(50, NotificationMotion.NextTick(200));
        Assert.Equal(20, NotificationMotion.NextTick(2720));
        Assert.Equal(16, NotificationMotion.NextTick(2740));
    }

    [Theory]
    [InlineData(0.0, false, 0, 359, 1)]
    [InlineData(0.5, false, 0, 180, 180)]
    [InlineData(1.0, false, 0, 0, 360)]
    [InlineData(0.0, true, 359, 0, 1)]
    [InlineData(0.5, true, 180, 0, 180)]
    [InlineData(1.0, true, 0, 0, 360)]
    public void The_part_shown_comes_out_of_the_edge(double visible, bool leftEdge, int source, int destination, int width)
    {
        Assert.Equal((source, destination, width), NotificationMotion.Slice(360, visible, leftEdge));
    }
}
