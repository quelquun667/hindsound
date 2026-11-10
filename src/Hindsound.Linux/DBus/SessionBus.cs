using Tmds.DBus.Protocol;

namespace Hindsound.Linux.DBus;

/// <summary>
/// The one connection of the app to the session bus, shared by every part. It is made when first asked for, not at start. A
/// bus that cannot be reached, or a connection lost, is written to the log and forgotten: the app goes on without what needs
/// the bus, and the next request tries again (the bus may come later in the session).
/// </summary>
/// <param name="address">Read when the bus is first asked for; null or empty when the session has no bus.</param>
/// <param name="log">The log of the run.</param>
internal sealed class SessionBus(Func<string?> address, Action<string> log) : IDisposable
{
    private static SessionBus? s_shared;

    private readonly object _gate = new();
    private Task<DBusConnection?>? _connection;
    private bool _failureSaid;
    private bool _disposed;

    /// <summary>The bus of the app, made by <c>LinuxPlatform.Install</c>.</summary>
    public static SessionBus Shared
    {
        get => s_shared ?? throw new InvalidOperationException("LinuxPlatform.Install makes the session bus first");
        set => s_shared = value;
    }

    /// <summary>The log of the run, for the helpers of <see cref="BusCalls"/>.</summary>
    public Action<string> Log => log;

    /// <summary>The failed calls, said once per method and error until the method succeeds again.</summary>
    internal CallFailureLog Failures { get; } = new(log);

    /// <summary>
    /// Raised after each new connection, on a thread of the pool: what a part put on the bus (an exported object, a watched
    /// signal, a name) went with the connection before it, and is put again from here.
    /// </summary>
    public event Action<DBusConnection>? Connected;

    /// <summary>The connection, or null when the bus cannot be reached now.</summary>
    public Task<DBusConnection?> ConnectionAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.FromResult<DBusConnection?>(null);
            }

            if (_connection is { IsCompleted: true, Result: null })
            {
                _connection = null;
            }

            // Off the lock and off the caller's thread: connecting waits on the socket.
            return _connection ??= Task.Run(ConnectAsync);
        }
    }

    public void Dispose()
    {
        Task<DBusConnection?>? connection;
        lock (_gate)
        {
            _disposed = true;
            connection = _connection;
            _connection = null;
        }

        if (connection is { IsCompletedSuccessfully: true, Result: { } open })
        {
            open.Dispose();
        }
    }

    private async Task<DBusConnection?> ConnectAsync()
    {
        if (address() is not { Length: > 0 } where)
        {
            SayFailure("no session bus: DBUS_SESSION_BUS_ADDRESS is not set");
            return null;
        }

        DBusConnection? connection = null;
        try
        {
            connection = new DBusConnection(where);
            await connection.ConnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            connection?.Dispose();
            SayFailure("the session bus could not be reached: " + ex.GetType().Name);
            return null;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                connection.Dispose();
                return null;
            }

            _failureSaid = false;
        }

        log("connected to the session bus");
        _ = WatchLossAsync(connection);
        Connected?.Invoke(connection);
        return connection;
    }

    private async Task WatchLossAsync(DBusConnection connection)
    {
        Exception? reason;
        try
        {
            reason = await connection.DisconnectedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            reason = ex;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_connection is { IsCompletedSuccessfully: true } current && ReferenceEquals(current.Result, connection))
            {
                _connection = null;
            }
        }

        log("the session bus was lost" + (reason is null ? string.Empty : ": " + reason.GetType().Name));
        connection.Dispose();
    }

    // Once until the next success: a desktop without a bus would otherwise fill the log, one line per request.
    private void SayFailure(string line)
    {
        lock (_gate)
        {
            if (_failureSaid)
            {
                return;
            }

            _failureSaid = true;
        }

        log(line);
    }
}
