using Hindsound.Linux.DBus;

namespace Hindsound.Linux.Tests.DBus;

/// <summary>
/// A connection whose watches could not be put is not kept claimed: the next try puts them, and still no connection ever
/// carries them twice.
/// </summary>
public sealed class ConnectionClaimRetryTests
{
    private sealed class Failure : Exception
    {
    }

    [Fact]
    public async Task A_watch_that_threw_leaves_the_connection_to_the_next_try()
    {
        var claim = new ConnectionClaim();
        var connection = new object();
        var tries = 0;

        await Assert.ThrowsAsync<Failure>(() => claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Watch));
        var retried = await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Watch);
        var again = await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Watch);

        Assert.True(retried);
        Assert.False(again);
        Assert.Equal(2, tries);

        Task Watch() => ++tries == 1 ? throw new Failure() : Task.CompletedTask;
    }

    [Fact]
    public async Task A_watch_that_was_not_put_leaves_the_connection_to_the_next_try()
    {
        var claim = new ConnectionClaim();
        var connection = new object();
        var tries = 0;

        var failed = await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Put);
        var retried = await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Put);
        var again = await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Put);

        Assert.False(failed);
        Assert.True(retried);
        Assert.False(again);
        Assert.Equal(2, tries);

        Task<bool> Put() => Task.FromResult(++tries > 1);
    }

    [Fact]
    public async Task A_failure_on_a_lost_connection_does_not_free_the_new_one()
    {
        var claim = new ConnectionClaim();
        var lost = new object();
        var current = new object();
        var failing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var puts = 0;

        // The put on the lost connection is still on its way when the new connection is claimed and watched.
        var onLost = claim.WatchOnceAsync(() => Task.FromResult<object?>(lost), () => failing.Task);
        Assert.True(await claim.WatchOnceAsync(() => Task.FromResult<object?>(current), Put));
        failing.SetResult(false);
        Assert.False(await onLost);

        Assert.False(await claim.WatchOnceAsync(() => Task.FromResult<object?>(current), Put));
        Assert.Equal(1, puts);

        Task<bool> Put()
        {
            puts++;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task A_try_during_a_put_that_fails_puts_nothing_and_the_one_after_it_puts_the_watches()
    {
        var claim = new ConnectionClaim();
        var connection = new object();
        var failing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var puts = 0;

        var first = claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), () => failing.Task);
        Assert.False(await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Put));
        failing.SetResult(false);
        Assert.False(await first);

        Assert.True(await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Put));
        Assert.False(await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Put));
        Assert.Equal(1, puts);

        Task<bool> Put()
        {
            puts++;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Tries_racing_on_one_connection_whose_puts_fail_at_random_never_hold_two_watches()
    {
        var claim = new ConnectionClaim();
        var connection = new object();
        var held = 0;
        var most = 0;
        var random = new Random(7);
        var fails = Enumerable.Range(0, 400).Select(_ => random.Next(2) == 0).ToArray();
        var next = -1;

        await Task.WhenAll(Enumerable.Range(0, 400).Select(_ => Task.Run(() => claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), Put))));

        // Whatever the race left, one more try ends with the watches put, once.
        await claim.WatchOnceAsync(() => Task.FromResult<object?>(connection), () => Task.FromResult(Hold()));

        Assert.Equal(1, Volatile.Read(ref held));
        Assert.Equal(1, Volatile.Read(ref most));

        async Task<bool> Put()
        {
            Hold();
            await Task.Yield();
            if (fails[Interlocked.Increment(ref next) % fails.Length])
            {
                // Takes back what it put, as a failed put does.
                Interlocked.Decrement(ref held);
                return false;
            }

            return true;
        }

        bool Hold()
        {
            var now = Interlocked.Increment(ref held);
            int seen;
            while (now > (seen = Volatile.Read(ref most)) && Interlocked.CompareExchange(ref most, now, seen) != seen)
            {
            }

            return true;
        }
    }
}
