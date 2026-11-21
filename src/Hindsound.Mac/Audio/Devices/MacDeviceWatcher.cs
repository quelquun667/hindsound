using System.Runtime.Versioning;
using Hindsound.Audio;

namespace Hindsound.Mac.Audio.Devices;

/// <summary>
/// Tells when a device comes or goes, or the default output or input changes, from the listeners of the HAL on its own
/// properties (kAudioHardwarePropertyDevices, DefaultOutputDevice, DefaultInputDevice). One change may come as several
/// notifications (a device plugged in is listed, then made the default), and a timer folds them into one, as on Windows and
/// Linux. The HAL calls on a thread of its own: the listener only arms the timer.
/// </summary>
internal sealed class MacDeviceWatcher : IDeviceWatcher
{
    /// <summary>The properties of the HAL listened to.</summary>
    public static readonly IReadOnlyList<uint> Selectors = [HalCodes.Devices, HalCodes.DefaultOutput, HalCodes.DefaultInput];

    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    private readonly Action<string> _log;
    private readonly Func<IReadOnlyList<uint>, Action, (IDisposable Remove, int[] Statuses)> _listen;
    private readonly Timer _debounce;
    private readonly Lock _gate = new();
    private IDisposable? _listeners;
    private bool _started;
    private bool _disposed;

    /// <summary>The watch of the HAL.</summary>
    [SupportedOSPlatform("macos")]
    public MacDeviceWatcher(Action<string> log)
        : this(log, CoreAudioHal.Listen)
    {
    }

    /// <param name="log">Where a listener the HAL refused is said.</param>
    /// <param name="listen">Adds the listeners on the selectors, calling the action at each change; for a test, a fake HAL.</param>
    internal MacDeviceWatcher(Action<string> log, Func<IReadOnlyList<uint>, Action, (IDisposable Remove, int[] Statuses)> listen)
    {
        _log = log;
        _listen = listen;
        _debounce = new Timer(_ => DevicesChanged?.Invoke());
    }

    public event Action? DevicesChanged;

    /// <summary>Never throws: a HAL that refuses is said in the log, and the devices are then only read when asked.</summary>
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

        IDisposable remove;
        int[] statuses;
        try
        {
            (remove, statuses) = _listen(Selectors, Signal);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log("the watch of the devices could not start: " + exception.GetType().Name + ": " + exception.Message);
            return;
        }

        for (var i = 0; i < statuses.Length && i < Selectors.Count; i++)
        {
            if (statuses[i] != 0)
            {
                _log($"the watch of the devices: the HAL refused the listener on '{Text(Selectors[i])}' (status {statuses[i]})");
            }
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _listeners = remove;
                return;
            }
        }

        remove.Dispose();
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
        IDisposable? listeners;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            listeners = _listeners;
            _listeners = null;
            _debounce.Dispose();
        }

        listeners?.Dispose();
    }

    private static string Text(uint code) =>
        new([(char)(code >> 24), (char)((code >> 16) & 0xFF), (char)((code >> 8) & 0xFF), (char)(code & 0xFF)]);
}
