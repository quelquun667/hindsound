using Hindsound.Linux.DBus;

namespace Hindsound.Linux.Tests.DBus;

/// <summary>
/// The watches go on the connection that was claimed: the bus may hand out another one by the time they are put, and that
/// one is claimed and watched on its own path.
/// </summary>
public sealed class ConnectionClaimHandsTheConnectionTests
{
    [Fact]
    public async Task The_watch_gets_the_connection_it_claimed()
    {
        var claim = new ConnectionClaim();
        var claimed = new object();
        var watched = new List<object>();

        // By the time the watch runs, the bus hands out another connection.
        var handed = new Queue<object>([claimed, new object()]);
        Assert.True(await claim.WatchOnceAsync(() => Task.FromResult<object?>(handed.Dequeue()), Put));
        Assert.False(await claim.WatchOnceAsync(() => Task.FromResult<object?>(claimed), Put));

        Assert.Same(claimed, Assert.Single(watched));

        Task<bool> Put(object connection)
        {
            watched.Add(connection);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task A_watch_that_was_not_put_leaves_its_connection_to_the_next_try()
    {
        var claim = new ConnectionClaim();
        var claimed = new object();
        var watched = new List<object>();

        Assert.False(await claim.WatchOnceAsync(() => Task.FromResult<object?>(claimed), connection =>
        {
            watched.Add(connection);
            return Task.FromResult(false);
        }));
        Assert.True(await claim.WatchOnceAsync(() => Task.FromResult<object?>(claimed), connection =>
        {
            watched.Add(connection);
            return Task.FromResult(true);
        }));

        Assert.Equal(new[] { claimed, claimed }, watched);
    }
}
