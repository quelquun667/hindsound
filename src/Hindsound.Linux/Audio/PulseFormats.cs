using Hindsound.Audio;
using NAudio.Wave;

namespace Hindsound.Linux.Audio;

/// <summary>
/// What the capture asks of the sound server, and how its answers become what the engine reads. Nothing here touches the
/// server, so it is tested everywhere.
/// </summary>
internal static class PulseFormats
{
    /// <summary>PA_SAMPLE_FLOAT32LE.</summary>
    public const int Float32Le = 5;

    /// <summary>A field of pa_buffer_attr left to the server.</summary>
    public const uint ServerDefault = uint.MaxValue;

    /// <summary>How much audio one packet holds, about: short enough for the live wave, long enough to wake up rarely.</summary>
    public const int FragmentMilliseconds = 20;

    // pa_stream_flags_t.
    public const uint StartCorked = 0x0001;
    public const uint DontMove = 0x0200;
    public const uint AdjustLatency = 0x2000;

    /// <summary>
    /// Corked until the engine starts it; the fragment size is the latency asked for; and never moved by the server to another
    /// device. A capture that loses its device fails instead, and the engine opens the one it then resolves: without this the
    /// server would move the capture of a vanished output's monitor to the default source, which may be a microphone.
    /// </summary>
    public const uint RecordFlags = StartCorked | AdjustLatency | DontMove;

    // pa_subscription_mask_t and pa_subscription_event_type_t.
    public const uint SubscribeSinks = 0x0001;
    public const uint SubscribeSources = 0x0002;
    public const uint SubscribeServer = 0x0080;
    public const uint SubscriptionMask = SubscribeSinks | SubscribeSources | SubscribeServer;

    private const uint FacilityMask = 0x000F;
    private const uint FacilitySink = 0x0000;
    private const uint FacilitySource = 0x0001;
    private const uint FacilityServer = 0x0007;
    private const uint TypeMask = 0x0030;
    private const uint TypeNew = 0x0000;
    private const uint TypeChange = 0x0010;
    private const uint TypeRemove = 0x0020;

    /// <summary>The channels the engine keeps for a side: the server mixes the device's channels down to them.</summary>
    public static int ChannelsOf(AudioFlow flow) => flow == AudioFlow.System ? InternalFormat.SystemChannels : InternalFormat.MicChannels;

    /// <summary>
    /// The format asked of the server: 32-bit float at the engine's rate and channels. The server converts, so the engine's
    /// converter has nothing to resample.
    /// </summary>
    public static PaSampleSpec SampleSpecFor(AudioFlow flow) =>
        new() { Format = Float32Le, Rate = InternalFormat.SampleRate, Channels = (byte)ChannelsOf(flow) };

    /// <summary>The format the source reports to the engine, the same as <see cref="SampleSpecFor"/>.</summary>
    public static WaveFormat WaveFormatFor(AudioFlow flow) => WaveFormat.CreateIeeeFloatWaveFormat(InternalFormat.SampleRate, ChannelsOf(flow));

    /// <summary>A record stream reads only maxlength and fragsize; the rest is left to the server.</summary>
    public static PaBufferAttr BufferAttrFor(AudioFlow flow) => new()
    {
        MaxLength = ServerDefault,
        TargetLength = ServerDefault,
        PreBuffer = ServerDefault,
        MinRequest = ServerDefault,
        FragmentSize = (uint)(InternalFormat.SampleRate * FragmentMilliseconds / 1000 * ChannelsOf(flow) * sizeof(float)),
    };

    /// <summary>
    /// The flags of one packet. The server gives no stamp the engine's clock can read, so every packet says so, and the
    /// engine places it as received. A hole is a stretch the server has no data for: silence, of its length. No packet is
    /// marked as coming after lost audio: the server's overflow callback is for playback streams only, so a record stream
    /// is never told that audio was dropped.
    /// </summary>
    public static PacketFlags FlagsOf(bool hole) => hole ? PacketFlags.TimestampError | PacketFlags.Silent : PacketFlags.TimestampError;

    /// <summary>
    /// Whether an event of the server may change what the engine captures: an output or an input that comes or goes, or a
    /// change of the server, which is where the defaults change. A change of an output or input itself is its volume or
    /// its state, and is left out: a volume slider sends dozens of them.
    /// </summary>
    public static bool IsDeviceChange(uint eventType)
    {
        var facility = eventType & FacilityMask;
        var type = eventType & TypeMask;
        return facility switch
        {
            FacilityServer => type == TypeChange,
            FacilitySink or FacilitySource => type is TypeNew or TypeRemove,
            _ => false,
        };
    }
}
