using NAudio.CoreAudioApi;

namespace Hindsound.Audio;

/// <summary>Says that the audio devices may have changed, without saying how: the listener checks what it needs.</summary>
public interface IDeviceWatcher : IDisposable
{
    /// <summary>Raised on a thread pool thread.</summary>
    event Action DevicesChanged;

    void Start();
}

/// <summary>
/// Listens for device changes. Windows calls back on its own thread while holding an internal lock, and says
/// nothing there may touch the audio stack, so this only arms a timer. Windows also sends several notifications
/// for one change (one per role, then state changes), and the timer folds them into one event.
/// </summary>
public sealed class DeviceWatcher : IDeviceWatcher
{
    private readonly Timer _debounce;
    private readonly TimeSpan _delay;
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _client;

    public DeviceWatcher()
        : this(TimeSpan.FromMilliseconds(300))
    {
    }

    public DeviceWatcher(TimeSpan delay)
    {
        _delay = delay;
        _debounce = new Timer(_ => DevicesChanged?.Invoke());
    }

    public event Action? DevicesChanged;

    public void Start()
    {
        if (_client is not null)
        {
            return;
        }

        _enumerator = new MMDeviceEnumerator();
        _client = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _client.DefaultDeviceChanged += (_, args) =>
        {
            if (args.Role == Role.Multimedia)
            {
                Signal();
            }
        };
        _client.DeviceStateChanged += (_, _) => Signal();
        _client.DeviceAdded += (_, _) => Signal();
        _client.DeviceRemoved += (_, _) => Signal();
    }

    /// <summary>Notes a change: the event fires once the changes have stopped for the delay.</summary>
    public void Signal() => _debounce.Change(_delay, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _client?.Dispose();
        _enumerator?.Dispose();
        _client = null;
        _enumerator = null;
        _debounce.Dispose();
    }
}
