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
}
