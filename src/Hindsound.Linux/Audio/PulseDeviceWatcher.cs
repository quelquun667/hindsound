using System.Runtime.Versioning;
using Hindsound.Audio;

namespace Hindsound.Linux.Audio;

/// <summary>
/// Tells when an output or an input comes or goes, or the defaults change, from the events the sound server sends
/// (pa_context_subscribe). One change may come as several events, and a timer folds them into one, as on Windows. When the
/// server goes away the watch connects again a little later, and then says the devices may have changed.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class PulseDeviceWatcher : IDeviceWatcher
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(3);

    private readonly Action<string> _log;
    private readonly Timer _debounce;
    private readonly Timer _retry;
    private readonly Lock _gate = new();
    private PulseConnection? _connection;
    private bool _started;
    private bool _disposed;
    private bool _missingLogged;

    public PulseDeviceWatcher(Action<string> log)
    {
        _log = log;
        _debounce = new Timer(_ => DevicesChanged?.Invoke());
        _retry = new Timer(_ => Connect(afterLoss: true));
    }

    public event Action? DevicesChanged;

    /// <summary>Never throws: without a server, the watch tries again later.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
        }

        Connect(afterLoss: false);
    }

    /// <summary>Notes a change: the event fires once the changes have stopped for a moment.</summary>
    public void Signal()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _debounce.Change(Settle, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        PulseConnection? connection;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            connection = _connection;
            _connection = null;
            _debounce.Dispose();
            _retry.Dispose();
        }

        connection?.Dispose();
    }

    private void Connect(bool afterLoss)
    {
        PulseConnection? old;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            old = _connection;
            _connection = null;
        }

        old?.Dispose();
        PulseConnection? connection = null;
        try
        {
            connection = PulseConnection.Open();
            connection.Lost += OnLost;
            connection.Subscribe(OnEvent);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            connection?.Dispose();
            if (!_missingLogged)
            {
                _missingLogged = true;
                _log("the watch of the devices waits for the sound server: " + exception.GetType().Name + ": " + exception.Message);
            }

            Retry();
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                connection.Dispose();
                return;
            }

            _connection = connection;
        }

        _missingLogged = false;

        // The server may have come back with other devices.
        if (afterLoss)
        {
            Signal();
        }
    }

    private void OnEvent(uint eventType)
    {
        if (PulseFormats.IsDeviceChange(eventType))
        {
            Signal();
        }
    }

    // On the loop's thread of the lost connection: only arms the timers, and the retry closes it from elsewhere.
    private void OnLost()
    {
        Retry();
        Signal();
    }

    private void Retry()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _retry.Change(RetryAfter, Timeout.InfiniteTimeSpan);
            }
        }
    }
}
