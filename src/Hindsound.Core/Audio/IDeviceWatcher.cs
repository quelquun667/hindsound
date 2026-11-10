namespace Hindsound.Audio;

/// <summary>Says that the audio devices may have changed, without saying how: the listener checks what it needs.</summary>
public interface IDeviceWatcher : IDisposable
{
    /// <summary>Raised on a thread pool thread.</summary>
    event Action DevicesChanged;

    void Start();
}
