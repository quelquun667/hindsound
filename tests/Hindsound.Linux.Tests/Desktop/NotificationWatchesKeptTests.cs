using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The watches of the clicks and of the ends are kept all together or not at all: one that could not be put takes the
/// others back, so the next try puts them all again and none twice. Pure logic: runs everywhere.
/// </summary>
public sealed class NotificationWatchesKeptTests
{
    private sealed class Watch : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    [Fact]
    public void Watches_all_put_are_kept_and_up()
    {
        var action = new Watch();
        var closed = new Watch();
        var kept = new List<IDisposable>();

        Assert.True(FreedesktopNotification.KeepWatches([action, closed], kept, disposed: false));

        Assert.Equal<IDisposable>([action, closed], kept);
        Assert.Equal(0, action.Disposed);
        Assert.Equal(0, closed.Disposed);
    }

    [Fact]
    public void A_watch_that_could_not_be_put_takes_the_others_back_and_asks_for_another_try()
    {
        var closed = new Watch();
        var action = new Watch();
        var kept = new List<IDisposable>();

        Assert.False(FreedesktopNotification.KeepWatches([null, closed], kept, disposed: false));
        Assert.False(FreedesktopNotification.KeepWatches([action, null], kept, disposed: false));
        Assert.False(FreedesktopNotification.KeepWatches([null, null], kept, disposed: false));

        Assert.Empty(kept);
        Assert.Equal(1, closed.Disposed);
        Assert.Equal(1, action.Disposed);
    }

    [Fact]
    public void Watches_put_after_the_view_is_gone_are_taken_back_and_not_tried_again()
    {
        var action = new Watch();
        var closed = new Watch();
        var kept = new List<IDisposable>();

        Assert.True(FreedesktopNotification.KeepWatches([action, closed], kept, disposed: true));

        Assert.Empty(kept);
        Assert.Equal(1, action.Disposed);
        Assert.Equal(1, closed.Disposed);
    }
}
