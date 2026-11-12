namespace Hindsound.Linux.DBus;

/// <summary>
/// Which connection a part has already put its watches on. The first connection can be reached by two paths at once: the
/// part asks for it, and <see cref="SessionBus.Connected"/> is raised for it before that request completes. Only the
/// first to claim a connection puts the watches on it, so a signal is not received twice. Thread-safe.
/// </summary>
internal sealed class ConnectionClaim
{
    private readonly object _gate = new();
    private object? _claimed;

    /// <summary>True for the first claim of <paramref name="connection"/>, false when it is the one already claimed.</summary>
    public bool TryClaim(object connection)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_claimed, connection))
            {
                return false;
            }

            _claimed = connection;
            return true;
        }
    }

    /// <summary>
    /// Asks for the connection and puts the watches on it, unless it is the one already claimed or there is none. Every
    /// path a part takes to its watches goes through here, so the claim cannot be left out of one of them.
    /// </summary>
    /// <returns>True when the watches were put.</returns>
    public Task<bool> WatchOnceAsync<TConnection>(Func<Task<TConnection?>> connect, Func<Task> watch)
        where TConnection : class =>
        WatchOnceAsync(connect, async () =>
        {
            await watch().ConfigureAwait(false);
            return true;
        });

    /// <summary>
    /// As <see cref="WatchOnceAsync{TConnection}(Func{Task{TConnection}}, Func{Task})"/>, for a <paramref name="watch"/> that
    /// says whether its watches were put: false when it put none, having taken back any it put. A watch that throws or
    /// answers false leaves the connection unclaimed, so the next try puts the watches on it.
    /// </summary>
    /// <returns>True when the watches were put.</returns>
    public async Task<bool> WatchOnceAsync<TConnection>(Func<Task<TConnection?>> connect, Func<Task<bool>> watch)
        where TConnection : class
    {
        if (await connect().ConfigureAwait(false) is not { } connection || !TryClaim(connection))
        {
            return false;
        }

        // A connection left claimed after a failed put would never be watched: every later try would see it claimed.
        var put = false;
        try
        {
            put = await watch().ConfigureAwait(false);
        }
        finally
        {
            if (!put)
            {
                Release(connection);
            }
        }

        return put;
    }

    /// <summary>Gives <paramref name="connection"/> back to the next claim, unless another one was claimed since.</summary>
    private void Release(object connection)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_claimed, connection))
            {
                _claimed = null;
            }
        }
    }
}
