using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Desktop;
using Hindsound.Ui;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The server keeps a notification in its list long after the app counts it gone, and a click there still reaches the app:
/// the click is about the notice clicked, found by its id, not about whatever the app shows at that moment.
/// </summary>
public sealed class NotificationClickAfterHoldTests
{
    private static readonly NotificationContent Older =
        new(NotificationKind.Silent, "Clip saved", "older", [], 0, NotificationVisual.Bars, 0, []) { ClipPath = "/home/u/Music/Hindsound/older.mp3" };

    private static readonly NotificationContent Newer =
        new(NotificationKind.Silent, "Clip saved", "newer", [], 0, NotificationVisual.Bars, 0, []) { ClipPath = "/home/u/Music/Hindsound/newer.mp3" };

    private static readonly NotificationContent Update =
        new(NotificationKind.UpdateReady, "Hindsound 0.6.0 is out", "Click to download it.", [], 0, NotificationVisual.Bars, 0, []);

    private static SessionBus NoBus() => throw new InvalidOperationException("no bus");

    // The loop is never run here: the ends of the notices it would carry never come, and nothing else is posted to it.
    private static (FreedesktopNotification Notification, List<NotificationContent?> Clicks) View(LinuxLoop loop)
    {
        var clicks = new List<NotificationContent?>();
        var notification = new FreedesktopNotification(loop, _ => { }, NoBus, new NoFolder(), holdMs: 60_000);

        // As the core reads it: what is showing when the click comes.
        notification.Clicked = () => clicks.Add(notification.Showing);
        return (notification, clicks);
    }

    [Fact]
    public void A_click_on_a_clip_after_its_hold_passes_that_clip_on()
    {
        using var loop = new LinuxLoop();
        var (notification, clicks) = View(loop);
        using var _ = notification;
        notification.Keep(5, Older);

        notification.OnAction(5, "default");

        Assert.Same(Older, Assert.Single(clicks));
        Assert.Null(notification.Showing);
    }

    [Fact]
    public void A_click_on_the_update_notice_after_its_hold_passes_the_update_notice_on()
    {
        using var loop = new LinuxLoop();
        var (notification, clicks) = View(loop);
        using var _ = notification;
        notification.Keep(9, Update);

        notification.OnAction(9, "default");

        Assert.Same(Update, Assert.Single(clicks));
        Assert.Null(notification.Showing);
    }

    [Fact]
    public void A_click_on_an_older_notice_passes_that_one_on_and_leaves_the_newer_one_showing()
    {
        using var loop = new LinuxLoop();
        var (notification, clicks) = View(loop);
        using var _ = notification;
        notification.Keep(5, Older);
        notification.Present(Newer, default, 0);
        notification.Keep(6, Newer);

        notification.OnAction(5, "default");
        notification.OnAction(6, "default");

        Assert.Equal([Older, Newer], clicks);
        Assert.Same(Newer, notification.Showing);
    }

    [Fact]
    public void A_click_on_a_notification_of_another_app_passes_nothing_on()
    {
        using var loop = new LinuxLoop();
        var (notification, clicks) = View(loop);
        using var _ = notification;
        notification.Keep(5, Older);

        notification.OnAction(12, "default");

        Assert.Empty(clicks);
    }

    private sealed class NoFolder : IFolderShell
    {
        public int ShowSelected(string folder, IReadOnlyList<string> files) => 0;
    }
}
