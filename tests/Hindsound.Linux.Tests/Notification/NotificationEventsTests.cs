using Hindsound.Unix.Notification;

namespace Hindsound.Linux.Tests.Notification;

/// <summary>The rule of the event filter of the notification's window.</summary>
public sealed class NotificationEventsTests
{
    [Fact]
    public void A_left_release_on_the_notification_is_a_click_and_stays_out_of_the_queue()
    {
        var clicks = 0;

        Assert.False(NotificationEvents.Keeps(0x402, 7, 1, 7, ref clicks));
        Assert.False(NotificationEvents.Keeps(0x401, 7, 1, 7, ref clicks));
        Assert.False(NotificationEvents.Keeps(0x402, 7, 3, 7, ref clicks));
        Assert.False(NotificationEvents.Keeps(0x400, 7, 0, 7, ref clicks));
        Assert.False(NotificationEvents.Keeps(0x204, 7, 0, 7, ref clicks));

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void The_events_of_other_windows_and_of_no_window_go_on()
    {
        var clicks = 0;

        Assert.True(NotificationEvents.Keeps(0x402, 3, 1, 7, ref clicks));
        Assert.True(NotificationEvents.Keeps(0x100, 0, 0, 7, ref clicks));
        Assert.True(NotificationEvents.Keeps(0x152, 0, 0, 7, ref clicks));
        Assert.True(NotificationEvents.Keeps(0x8000, 7, 1, 7, ref clicks));
        Assert.True(NotificationEvents.Keeps(0x402, 7, 1, 0, ref clicks));

        Assert.Equal(0, clicks);
    }
}
