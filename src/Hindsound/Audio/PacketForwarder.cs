using NAudio.CoreAudioApi;

namespace Hindsound.Audio;

/// <summary>
/// The packets of a recorder, passed on to the handlers of <see cref="ICaptureSource.PacketAvailable"/>. The recorder is
/// listened to only while a handler is there. The flags keep their values: <see cref="PacketFlags"/> copies those of WASAPI,
/// and a lost <c>Silent</c> would store the silence of a packet as sound.
/// </summary>
/// <param name="subscribe">Starts listening to the recorder.</param>
/// <param name="unsubscribe">Stops listening to it.</param>
internal sealed class PacketForwarder(Action subscribe, Action unsubscribe)
{
    private readonly object _lock = new();
    private CapturePacketHandler? _handlers;

    public void Add(CapturePacketHandler handler)
    {
        lock (_lock)
        {
            if (_handlers is null)
            {
                subscribe();
            }

            _handlers += handler;
        }
    }

    public void Remove(CapturePacketHandler handler)
    {
        lock (_lock)
        {
            _handlers -= handler;
            if (_handlers is null)
            {
                unsubscribe();
            }
        }
    }

    public void Forward(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition) =>
        Volatile.Read(ref _handlers)?.Invoke(buffer, (PacketFlags)flags, devicePosition, qpcPosition);
}
