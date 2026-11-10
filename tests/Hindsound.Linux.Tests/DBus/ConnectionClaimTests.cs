using Hindsound.Linux.DBus;

namespace Hindsound.Linux.Tests.DBus;

/// <summary>The first connection reached by two paths at once is watched by one of them only.</summary>
public sealed class ConnectionClaimTests
{
    [Fact]
    public void A_connection_is_claimed_once_and_a_new_one_again()
    {
        var claim = new ConnectionClaim();
        var first = new object();
        var second = new object();

        Assert.True(claim.TryClaim(first));
        Assert.False(claim.TryClaim(first));
        Assert.True(claim.TryClaim(second));
        Assert.False(claim.TryClaim(second));
    }

    [Fact]
    public void Of_many_threads_claiming_one_connection_exactly_one_wins()
    {
        var claim = new ConnectionClaim();
        var connection = new object();
        var wins = 0;

        Parallel.For(0, 64, _ =>
        {
            if (claim.TryClaim(connection))
            {
                Interlocked.Increment(ref wins);
            }
        });

        Assert.Equal(1, wins);
    }
}
