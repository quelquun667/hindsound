using Hindsound.Linux.DBus;

namespace Hindsound.Linux.Tests.DBus;

/// <summary>
/// The path a part takes to put its watches on a connection: the first connection, reached both by asking for it and by
/// <see cref="SessionBus.Connected"/>, gets the watches once; a new connection gets them again; no connection gets none.
/// </summary>
public sealed class ConnectionClaimWatchTests
{
    [Fact]
    public async Task The_same_connection_reached_twice_is_watched_once()
    {
        var claim = new ConnectionClaim();
        var connection = new object();
        var watched = 0;

        var first = await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Watch);
        var second = await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Watch);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, watched);

        Task Watch()
        {
            watched++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_new_connection_after_a_lost_one_is_watched_again()
    {
        var claim = new ConnectionClaim();
        var watched = 0;

        await claim.WatchOnceAsync(() => Task.FromResult<object?>(new object()), Watch);
        await claim.WatchOnceAsync(() => Task.FromResult<object?>(new object()), Watch);

        Assert.Equal(2, watched);

        Task Watch()
        {
            watched++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task No_connection_puts_no_watch()
    {
        var claim = new ConnectionClaim();
        var watched = 0;

        var result = await claim.WatchOnceAsync(() => Task.FromResult<object?>(null), () =>
        {
            watched++;
            return Task.CompletedTask;
        });

        Assert.False(result);
        Assert.Equal(0, watched);
    }

    [Fact]
    public async Task Two_paths_racing_on_the_first_connection_watch_it_once()
    {
        var claim = new ConnectionClaim();
        var connection = new object();
        var gate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watched = 0;

        var asked = claim.WatchOnceAsync(() => gate.Task, Watch);
        var raised = claim.WatchOnceAsync(() => gate.Task, Watch);
        gate.SetResult(connection);
        await Task.WhenAll(asked, raised);

        Assert.Equal(1, Volatile.Read(ref watched));

        Task Watch()
        {
            Interlocked.Increment(ref watched);
            return Task.CompletedTask;
        }
    }
}
