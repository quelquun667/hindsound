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
    /// The flags of one packet of a capture that the preview may silence: while it plays, the packet says it is silent, so the
    /// engine writes silence of its length, and the frames that follow keep their place.
    /// </summary>
    public static PacketFlags FlagsOf(bool hole, bool silenced) => silenced ? FlagsOf(hole) | PacketFlags.Silent : FlagsOf(hole);

    /// <summary>How much the preview's stream keeps ahead of the output, about: what it still plays once told to stop is less.</summary>
    public const int PlaybackAheadMilliseconds = 100;

    /// <summary>A playback stream reads maxlength, tlength, prebuf and minreq; only the target length is set here.</summary>
    public static PaBufferAttr PlaybackBufferAttrFor(int channels) => new()
    {
        MaxLength = ServerDefault,
        TargetLength = (uint)(InternalFormat.SampleRate * PlaybackAheadMilliseconds / 1000 * channels * sizeof(float)),
        PreBuffer = ServerDefault,
        MinRequest = ServerDefault,
        FragmentSize = ServerDefault,
    };

    /// <summary>
    /// The frame of the file a stopped preview stream goes on from: the last one the listener hears. The stream wrote the
    /// file's frame <paramref name="startFrame"/> at its byte <paramref name="startIndex"/> and has written up to
    /// <paramref name="writtenIndex"/>; once corked, the server has taken up to <paramref name="readIndex"/> from it, of
    /// which the last <paramref name="sinkMicroseconds"/> were still on their way to the speaker. Those were heard if the
    /// sink plays them out, or dropped if it took them back without giving them to the stream again: the server says
    /// neither, and a sound lost at a stop is worse than a few milliseconds heard twice, so they are played again.
    /// The frame is never before <paramref name="startFrame"/>, which was heard already, nor past what the file gave.
    /// </summary>
    public static long HeardFrame(long startFrame, long startIndex, long writtenIndex, long readIndex, ulong sinkMicroseconds, int rate, int channels)
    {
        var frameBytes = (long)channels * sizeof(float);
        var given = Math.Max(0, writtenIndex - startIndex) / frameBytes;
        var taken = Math.Max(0, readIndex - startIndex) / frameBytes;

        // Rounded to the nearest frame; a sink latency of an hour is a broken answer, and holds the whole of any clip anyway.
        var held = (long)((Math.Min(sinkMicroseconds, 3_600_000_000UL) * (ulong)rate + 500_000) / 1_000_000);
        var heard = Math.Clamp(taken - held, 0, given);
        return Math.Max(0, startFrame) + heard;
    }

    /// <summary>
    /// The time of the file's frame <paramref name="frame"/>, the first tick after its exact time: a decoder that turns it back
    /// into a frame, in floating point, rounding down or to the nearest, finds the same frame (a tick is a twentieth of one).
    /// </summary>
    public static TimeSpan TimeOfFrame(long frame, int rate) =>
        TimeSpan.FromTicks((long)((Int128)Math.Max(0, frame) * TimeSpan.TicksPerSecond / rate) + 1);

    /// <summary>The format of the preview's stream: what the decoder reads, 32-bit float at the engine's rate.</summary>
    public static PaSampleSpec PlaybackSpecFor(int channels) =>
        new() { Format = Float32Le, Rate = InternalFormat.SampleRate, Channels = (byte)channels };

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
