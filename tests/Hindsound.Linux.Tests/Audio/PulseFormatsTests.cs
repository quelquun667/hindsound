using Hindsound.Audio;
using Hindsound.Linux.Audio;
using NAudio.Wave;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>What the capture asks of the sound server, and how its packets and events reach the engine.</summary>
public sealed class PulseFormatsTests
{
    [Theory]
    [InlineData(AudioFlow.System, 2)]
    [InlineData(AudioFlow.Mic, 1)]
    public void The_server_is_asked_for_float_at_the_engine_rate_and_channels(AudioFlow flow, int channels)
    {
        var spec = PulseFormats.SampleSpecFor(flow);
        var format = PulseFormats.WaveFormatFor(flow);

        Assert.Equal(5, spec.Format);
        Assert.Equal(48000u, spec.Rate);
        Assert.Equal(channels, spec.Channels);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, format.Encoding);
        Assert.Equal(48000, format.SampleRate);
        Assert.Equal(channels, format.Channels);
        Assert.Equal(channels * 4, format.BlockAlign);
    }

    [Fact]
    public void The_packets_hold_twenty_milliseconds_and_the_rest_is_left_to_the_server()
    {
        var system = PulseFormats.BufferAttrFor(AudioFlow.System);
        var mic = PulseFormats.BufferAttrFor(AudioFlow.Mic);

        Assert.Equal(960u * 2 * 4, system.FragmentSize);
        Assert.Equal(960u * 4, mic.FragmentSize);
        Assert.Equal(uint.MaxValue, system.MaxLength);
        Assert.Equal(uint.MaxValue, system.TargetLength);
        Assert.Equal(uint.MaxValue, system.PreBuffer);
        Assert.Equal(uint.MaxValue, system.MinRequest);
    }

    [Fact]
    public void The_packets_fit_the_converter_of_the_engine_in_one_piece()
    {
        var attributes = PulseFormats.BufferAttrFor(AudioFlow.System);
        var frames = attributes.FragmentSize / PulseFormats.WaveFormatFor(AudioFlow.System).BlockAlign;

        Assert.True(frames <= AudioFormatConverter.MaxInputFrames);
    }

    [Fact]
    public void The_stream_starts_corked_asks_for_its_latency_and_is_never_moved_to_another_device()
    {
        Assert.Equal(0x0001u | 0x2000u | 0x0200u, PulseFormats.RecordFlags);
    }

    [Fact]
    public void Every_packet_says_its_stamp_is_not_the_engine_clock()
    {
        Assert.Equal(PacketFlags.TimestampError, PulseFormats.FlagsOf(hole: false));
    }

    [Fact]
    public void A_hole_is_silence()
    {
        Assert.Equal(PacketFlags.TimestampError | PacketFlags.Silent, PulseFormats.FlagsOf(hole: true));
    }

    [Fact]
    public void The_watch_asks_for_the_outputs_the_inputs_and_the_server()
    {
        Assert.Equal(0x0001u | 0x0002u | 0x0080u, PulseFormats.SubscriptionMask);
    }

    [Theory]
    [InlineData(0x0000u, true)] // a new output
    [InlineData(0x0020u, true)] // an output removed
    [InlineData(0x0001u, true)] // a new input
    [InlineData(0x0021u, true)] // an input removed
    [InlineData(0x0017u, true)] // the server changed: the defaults
    [InlineData(0x0010u, false)] // an output's volume or state
    [InlineData(0x0011u, false)] // an input's volume or state
    [InlineData(0x0002u, false)] // a new playback stream
    [InlineData(0x0012u, false)] // a playback stream changed
    [InlineData(0x0003u, false)] // a new record stream
    [InlineData(0x0019u, false)] // a card changed
    [InlineData(0x0007u, false)] // a "new" server, which the server never sends
    public void Only_a_device_that_comes_or_goes_or_a_change_of_the_server_counts(uint eventType, bool counts)
    {
        Assert.Equal(counts, PulseFormats.IsDeviceChange(eventType));
    }

    private const int StereoFrame = 2 * sizeof(float);

    [Fact]
    public void A_stop_goes_on_from_what_the_server_took_less_what_the_sink_still_held()
    {
        // 0.6 s written ahead of 0.5 s taken by the server, of which 60 ms were on their way to the speaker: 0.44 s was heard.
        var frame = PulseFormats.HeardFrame(0, 0, 28_800 * StereoFrame, 24_000 * StereoFrame, 60_000, 48_000, 2);
        Assert.Equal(21_120, frame);

        // The same, one channel: the indexes count the stream's own frames.
        Assert.Equal(21_120, PulseFormats.HeardFrame(0, 0, 28_800 * sizeof(float), 24_000 * sizeof(float), 60_000, 48_000, 1));
    }

    [Fact]
    public void A_second_stop_counts_from_where_the_first_one_sent_the_file()
    {
        // The file went back to frame 21 120 at the stream's byte of frame 24 000; 0.2 s taken since, 50 ms of it still held.
        var start = 24_000L * StereoFrame;
        var frame = PulseFormats.HeardFrame(21_120, start, start + (48_000L * StereoFrame), start + (9_600L * StereoFrame), 50_000, 48_000, 2);
        Assert.Equal(21_120 + 9_600 - 2_400, frame);
    }

    [Theory]
    [InlineData(20)] // 0.96 frame: rounds to one.
    [InlineData(10)] // 0.48 frame: rounds to none.
    public void The_held_time_is_rounded_to_the_nearest_frame(ulong microseconds)
    {
        var frame = PulseFormats.HeardFrame(0, 0, 1_000 * StereoFrame, 1_000 * StereoFrame, microseconds, 48_000, 2);
        Assert.Equal(microseconds == 20 ? 999 : 1_000, frame);
    }

    [Fact]
    public void The_place_is_never_before_the_last_one_nor_past_what_the_file_gave()
    {
        // Stopped 10 ms after a play, with 60 ms held by the sink: what came before the play was heard already.
        Assert.Equal(5_000, PulseFormats.HeardFrame(5_000, 800, 800 + (4_800 * StereoFrame), 800 + (480 * StereoFrame), 60_000, 48_000, 2));

        // A read index past the writes, or a broken latency, keep the place within what was written.
        Assert.Equal(4_800, PulseFormats.HeardFrame(0, 0, 4_800 * StereoFrame, 9_600 * StereoFrame, 0, 48_000, 2));
        Assert.Equal(0, PulseFormats.HeardFrame(0, 0, 4_800 * StereoFrame, 4_800 * StereoFrame, ulong.MaxValue, 48_000, 2));
        Assert.Equal(0, PulseFormats.HeardFrame(-3, 100, 0, -500, 0, 48_000, 2));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(21_121L)]
    [InlineData(72_000L)]
    [InlineData(47_999L * 3_600)]
    public void The_time_of_a_frame_turns_back_into_the_same_frame_rounded_down_or_to_the_nearest(long frame)
    {
        var at = PulseFormats.TimeOfFrame(frame, 48_000);
        Assert.Equal(frame, (long)(at.TotalSeconds * 48_000));
        Assert.Equal(frame, (long)Math.Round(at.TotalSeconds * 48_000));
    }
}
