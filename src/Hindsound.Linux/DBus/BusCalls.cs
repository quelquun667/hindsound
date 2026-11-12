using Tmds.DBus.Protocol;

namespace Hindsound.Linux.DBus;

/// <summary>Writes the body of a message: the arguments of a call or of a signal, in the order of its signature.</summary>
internal delegate void BodyWriter(ref MessageWriter writer);

/// <summary>The answer of a call: its value, or what went wrong, for the log.</summary>
internal readonly record struct BusAnswer<T>(bool Ok, T? Value, string? Error)
{
    public static BusAnswer<T> Failed(string error) => new(false, default, error);
}

/// <summary>
/// Calls, signals and objects on the shared bus. None of them throws: a bus out of reach, a peer that refuses or a
/// connection lost is an answer, written to the log, and the app goes on.
/// </summary>
internal static class BusCalls
{
    private const string NoBus = "no session bus";

    /// <summary>Calls <paramref name="method"/> and reads its answer with <paramref name="read"/>, on the thread of the connection.</summary>
    /// <param name="signature">The D-Bus signature of the arguments <paramref name="body"/> writes; null without arguments.</param>
    public static async Task<BusAnswer<T>> CallAsync<T>(this SessionBus bus, BusMethod method, string? signature, BodyWriter? body, MessageValueReader<T> read)
    {
        if (await bus.ConnectionAsync().ConfigureAwait(false) is not { } connection)
        {
            return BusAnswer<T>.Failed(NoBus);
        }

        try
        {
            var value = await connection.CallMethodAsync(MethodCall(connection, method, signature, body), read, null).ConfigureAwait(false);
            bus.Failures.Succeeded(method);
            return new BusAnswer<T>(true, value, null);
        }
        catch (Exception ex)
        {
            return Failed<T>(bus, method, ex);
        }
    }

    /// <summary>Calls <paramref name="method"/>, whose answer carries nothing.</summary>
    public static async Task<BusAnswer<bool>> CallAsync(this SessionBus bus, BusMethod method, string? signature, BodyWriter? body)
    {
        if (await bus.ConnectionAsync().ConfigureAwait(false) is not { } connection)
        {
            return BusAnswer<bool>.Failed(NoBus);
        }

        try
        {
            await connection.CallMethodAsync(MethodCall(connection, method, signature, body)).ConfigureAwait(false);
            bus.Failures.Succeeded(method);
            return new BusAnswer<bool>(true, true, null);
        }
        catch (Exception ex)
        {
            return Failed<bool>(bus, method, ex);
        }
    }

    /// <summary>
    /// Watches <paramref name="signal"/>: <paramref name="received"/> gets each one, read by <paramref name="read"/>, on the
    /// thread of the connection; what it does to the app goes through the loop. Dispose the answer to stop. Null when the
    /// bus could not be asked. A connection lost takes the watch with it: <see cref="SessionBus.Connected"/> says when to watch again.
    /// </summary>
    public static async Task<IDisposable?> WatchSignalAsync<T>(this SessionBus bus, BusSignal signal, MessageValueReader<T> read, Action<T> received)
    {
        if (await bus.ConnectionAsync().ConfigureAwait(false) is not { } connection)
        {
            return null;
        }

        return await connection.WatchSignalAsync(bus, signal, read, received).ConfigureAwait(false);
    }

    /// <summary>
    /// As <see cref="WatchSignalAsync{T}(SessionBus, BusSignal, MessageValueReader{T}, Action{T})"/>, on
    /// <paramref name="connection"/> itself rather than on the one the bus hands out now. Null when the watch could not be put.
    /// </summary>
    public static async Task<IDisposable?> WatchSignalAsync<T>(this DBusConnection connection, SessionBus bus, BusSignal signal, MessageValueReader<T> read, Action<T> received)
    {
        try
        {
            return await connection.AddMatchAsync(
                signal.Rule(),
                read,
                (Notification<T> notification) =>
                {
                    if (notification.HasValue)
                    {
                        received(notification.Value);
                    }
                    else if (notification.CompletionReason is { } ex)
                    {
                        bus.Log($"watching {signal}: {ex.GetType().Name}");
                    }
                },
                false,
                ObserverFlags.None,
                null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            bus.Log($"watching {signal} failed: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>Puts <paramref name="handler"/> on the bus at its path. Whether it is there.</summary>
    public static async Task<bool> ExportAsync(this SessionBus bus, IPathMethodHandler handler)
    {
        if (await bus.ConnectionAsync().ConfigureAwait(false) is not { } connection)
        {
            return false;
        }

        try
        {
            connection.AddMethodHandler(handler);
            return true;
        }
        catch (Exception ex)
        {
            bus.Log($"putting {handler.Path} on the bus failed: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>Sends a signal from the object at <paramref name="path"/>, to every listener. Whether it left.</summary>
    public static async Task<bool> EmitSignalAsync(this SessionBus bus, string path, string @interface, string member, string? signature, BodyWriter? body)
    {
        if (await bus.ConnectionAsync().ConfigureAwait(false) is not { } connection)
        {
            return false;
        }

        try
        {
            return connection.TrySendMessage(Signal(connection, path, @interface, member, signature, body));
        }
        catch (Exception ex)
        {
            bus.Log($"sending {@interface}.{member} failed: {ex.GetType().Name}");
            return false;
        }
    }

    private static MessageBuffer MethodCall(DBusConnection connection, BusMethod method, string? signature, BodyWriter? body)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(method.Destination, method.Path, method.Interface, method.Member, signature, MessageFlags.None);
            body?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static MessageBuffer Signal(DBusConnection connection, string path, string @interface, string member, string? signature, BodyWriter? body)
    {
        BusChecks.Require(path, DBusNames.IsObjectPath, "object path");
        BusChecks.Require(@interface, DBusNames.IsInterfaceName, "interface name");
        BusChecks.Require(member, DBusNames.IsMemberName, "member name");
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteSignalHeader(null, path, @interface, member, signature);
            body?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    // The name of an error from the peer, or the type of the failure: never its message, which can hold a path. Said once
    // per method and error: a caller that tries a missing service again would otherwise write a line per try.
    private static BusAnswer<T> Failed<T>(SessionBus bus, BusMethod method, Exception ex)
    {
        var error = ex is DBusErrorReplyException reply ? reply.ErrorName : ex.GetType().Name;
        bus.Failures.Failed(method, error);
        return BusAnswer<T>.Failed(error);
    }
}
