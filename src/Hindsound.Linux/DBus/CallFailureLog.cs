namespace Hindsound.Linux.DBus;

/// <summary>
/// The log lines of failed calls, once per method and error until that method succeeds again. A part that tries a missing
/// service again and again (the icon every 2 s while no StatusNotifierWatcher is there, a notification per clip without a
/// notification server) says it once, not once per try. A new error of the same method is said, as is the first failure
/// after a success. Thread-safe: calls end on the threads of the connection.
/// </summary>
internal sealed class CallFailureLog(Action<string> log)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _said = new(StringComparer.Ordinal);

    /// <summary>A call of <paramref name="method"/> failed with <paramref name="error"/>: written unless it was the last thing said for that method.</summary>
    public void Failed(BusMethod method, string error)
    {
        lock (_gate)
        {
            var key = KeyOf(method);
            if (_said.TryGetValue(key, out var last) && last == error)
            {
                return;
            }

            _said[key] = error;
        }

        log($"{method} failed: {error}");
    }

    /// <summary>A call of <paramref name="method"/> succeeded: its next failure is said again.</summary>
    public void Succeeded(BusMethod method)
    {
        lock (_gate)
        {
            _said.Remove(KeyOf(method));
        }
    }

    // The peer is part of the key: the same method of two services fails for each on its own.
    private static string KeyOf(BusMethod method) => method.Destination + " " + method.Path + " " + method;
}
