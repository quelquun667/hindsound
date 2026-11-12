using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The watches of the clicks when a put throws: the ones already put are taken back, so the next try does not put them a
/// second time on the same connection, and the failure is written to the log.
/// </summary>
public sealed class NotificationWatchFailureTests
{
    private sealed class Failure : Exception
    {
    }

    private sealed class Watch : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    [Fact]
    public async Task A_put_that_throws_takes_back_the_watches_already_put()
    {
        var action = new Watch();
        var kept = false;

        await Assert.ThrowsAsync<Failure>(() => FreedesktopNotification.PutAllAsync(
            [
                () => Task.FromResult<IDisposable?>(action),
                () => Task.FromException<IDisposable?>(new Failure()),
            ],
            _ => kept = true));

        Assert.Equal(1, action.Disposed);
        Assert.False(kept);
    }

    [Fact]
    public async Task Every_watch_put_goes_to_keep_and_none_is_taken_back()
    {
        var action = new Watch();
        var closed = new Watch();
        IReadOnlyList<IDisposable?>? kept = null;

        var put = await FreedesktopNotification.PutAllAsync(
            [
                () => Task.FromResult<IDisposable?>(action),
                () => Task.FromResult<IDisposable?>(closed),
            ],
            watches =>
            {
                kept = watches;
                return true;
            });

        Assert.True(put);
        Assert.Equal(new IDisposable?[] { action, closed }, kept!);
        Assert.Equal(0, action.Disposed);
        Assert.Equal(0, closed.Disposed);
    }

    [Fact]
    public async Task A_failure_of_the_watches_is_written_to_the_log()
    {
        var log = new List<string>();

        await FreedesktopNotification.LogWatchFailureAsync(async () =>
        {
            await Task.Yield();
            throw new Failure();
        }, log.Add);
        await FreedesktopNotification.LogWatchFailureAsync(() => Task.CompletedTask, log.Add);

        Assert.Equal(new[] { "notification: watching the clicks failed: Failure" }, log);
    }
}
