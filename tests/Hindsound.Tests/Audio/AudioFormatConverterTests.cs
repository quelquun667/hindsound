using System.Runtime.InteropServices;
using Hindsound.Audio;
using NAudio.Wave;

namespace Hindsound.Tests.Audio;

[Collection(nameof(TimingSensitive))]
public class AudioFormatConverterTests
{
    private const float Tolerance = 1e-5f;

    private static float[] Convert(WaveFormat format, int outputChannels, byte[] input, bool silent = false)
    {
        var converter = new AudioFormatConverter(format, outputChannels);
        var output = new float[converter.MaxOutputFrames * outputChannels];
        var frames = converter.Convert(input, silent, output);
        return output[..(frames * outputChannels)];
    }

    private static byte[] Bytes<T>(params T[] values)
        where T : struct => MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    private static void AssertSamples(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) <= Tolerance, $"Sample {i}: expected {expected[i]}, got {actual[i]}");
        }
    }

    [Fact]
    public void Decodes_8_bit_unsigned()
    {
        var output = Convert(new WaveFormat(48000, 8, 1), 1, [128, 192, 64, 0, 255]);
        AssertSamples([0f, 0.5f, -0.5f, -1f, 127f / 128f], output);
    }

    [Fact]
    public void Decodes_16_bit()
    {
        var output = Convert(new WaveFormat(48000, 16, 1), 1, Bytes<short>(0, 16384, -16384, -32768, 32767));
        AssertSamples([0f, 0.5f, -0.5f, -1f, 32767f / 32768f], output);
    }

    [Fact]
    public void Decodes_packed_24_bit()
    {
        byte[] input =
        [
            0x00, 0x00, 0x00, // 0
            0x00, 0x00, 0x40, // 0x400000 = half scale
            0x00, 0x00, 0xC0, // -0x400000
            0x00, 0x00, 0x80, // minimum
            0xFF, 0xFF, 0x7F, // maximum
        ];

        var output = Convert(new WaveFormat(48000, 24, 1), 1, input);

        AssertSamples([0f, 0.5f, -0.5f, -1f, 8388607f / 8388608f], output);
    }

    [Fact]
    public void Decodes_32_bit_integers()
    {
        var output = Convert(new WaveFormat(48000, 32, 1), 1, Bytes(0, 0x40000000, -0x40000000, int.MinValue));
        AssertSamples([0f, 0.5f, -0.5f, -1f], output);
    }

    [Fact]
    public void Decodes_32_bit_float_unchanged_even_past_full_scale()
    {
        var output = Convert(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), 1, Bytes(0f, 0.25f, -0.75f, 1.5f));
        AssertSamples([0f, 0.25f, -0.75f, 1.5f], output);
    }

    [Fact]
    public void Decodes_64_bit_float()
    {
        var format = WaveFormat.CreateCustomFormat(WaveFormatEncoding.IeeeFloat, 48000, 1, 48000 * 8, 8, 64);
        var output = Convert(format, 1, Bytes(0.0, 0.25, -0.75));
        AssertSamples([0f, 0.25f, -0.75f], output);
    }

    [Fact]
    public void Extensible_32_bit_pcm_is_read_as_integers_and_not_as_float()
    {
        var format = new WaveFormatExtensible(48000, 32, 1);
        Assert.Equal(WaveFormatEncoding.Extensible, format.Encoding);

        var pcm = new WaveFormatExtensible(48000, 32, 1, NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_PCM, 32, 0x4);

        var output = Convert(pcm, 1, Bytes(0x40000000));

        AssertSamples([0.5f], output);
    }

    [Fact]
    public void Extensible_32_bit_float_is_read_as_float()
    {
        var format = new WaveFormatExtensible(48000, 32, 2, NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_Float, 32, 0x3);

        var output = Convert(format, 2, Bytes(0.25f, -0.5f));

        AssertSamples([0.25f, -0.5f], output);
    }

    [Fact]
    public void Extensible_24_bit_in_32_bit_container_is_full_scale()
    {
        var format = new WaveFormatExtensible(48000, 32, 1, NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_PCM, 24, 0x4);

        var output = Convert(format, 1, Bytes(0x40000000));

        AssertSamples([0.5f], output);
    }

    [Fact]
    public void Refuses_a_format_it_cannot_read()
    {
        Assert.Throws<NotSupportedException>(() => new AudioFormatConverter(WaveFormat.CreateALawFormat(8000, 1), 1));
    }

    [Fact]
    public void Mono_becomes_stereo_at_the_same_level()
    {
        var output = Convert(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), 2, Bytes(0.5f, -0.25f));
        AssertSamples([0.5f, 0.5f, -0.25f, -0.25f], output);
    }

    [Fact]
    public void Stereo_becomes_mono_by_averaging()
    {
        var output = Convert(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), 1, Bytes(0.5f, 0.25f, 1f, -1f));
        AssertSamples([0.375f, 0f], output);
    }

    [Fact]
    public void Stereo_stays_stereo()
    {
        var output = Convert(new WaveFormat(48000, 16, 2), 2, Bytes<short>(16384, -16384));
        AssertSamples([0.5f, -0.5f], output);
    }

    [Fact]
    public void Surround_5_1_downmixes_with_fixed_gains()
    {
        // 1 + 0.7071 + 0.7071 = 2.4142 is what all channels at full level would add up to on one side.
        const float Front = 1f / 2.41421356f;
        const float Other = 0.70710678f / 2.41421356f;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 6);

        // Order: front left, front right, centre, LFE, back left, back right.
        AssertSamples([Front, 0f], Convert(format, 2, Bytes(1f, 0f, 0f, 0f, 0f, 0f)));
        AssertSamples([0f, Front], Convert(format, 2, Bytes(0f, 1f, 0f, 0f, 0f, 0f)));
        AssertSamples([Other, Other], Convert(format, 2, Bytes(0f, 0f, 1f, 0f, 0f, 0f)));
        AssertSamples([0f, 0f], Convert(format, 2, Bytes(0f, 0f, 0f, 1f, 0f, 0f)));
        AssertSamples([Other, 0f], Convert(format, 2, Bytes(0f, 0f, 0f, 0f, 1f, 0f)));
        AssertSamples([0f, Other], Convert(format, 2, Bytes(0f, 0f, 0f, 0f, 0f, 1f)));
        AssertSamples([1f, 1f], Convert(format, 2, Bytes(1f, 1f, 1f, 1f, 1f, 1f)));
    }

    [Fact]
    public void Channel_mask_decides_which_channel_is_which()
    {
        // Quad given as front left, front right, front centre, LFE: the mask says the 3rd is the centre.
        var format = new WaveFormatExtensible(48000, 32, 4, NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_Float, 32, 0xF);
        const float Other = 0.70710678f / 1.70710678f;

        AssertSamples([Other, Other], Convert(format, 2, Bytes(0f, 0f, 1f, 0f)));
        AssertSamples([0f, 0f], Convert(format, 2, Bytes(0f, 0f, 0f, 1f)));
    }

    [Fact]
    public void Silent_packets_are_zeros_whatever_they_hold()
    {
        var output = Convert(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), 2, Bytes(0.5f, 0.5f), silent: true);
        Assert.Equal([0f, 0f], output);
    }

    [Fact]
    public void A_partial_frame_at_the_end_is_ignored()
    {
        var output = Convert(new WaveFormat(48000, 16, 2), 2, [0, 0, 0, 0, 1, 2, 3]);
        Assert.Equal(2, output.Length);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(96000)]
    [InlineData(16000)]
    [InlineData(8000)]
    public void Resampling_keeps_length_level_and_pitch(int sourceRate)
    {
        const double Frequency = 1000;
        const double Seconds = 2;
        var converter = new AudioFormatConverter(WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, 1), 1);
        var output = new float[converter.MaxOutputFrames];
        var result = new List<float>();
        var total = (int)(sourceRate * Seconds);
        var packet = new float[Math.Min(sourceRate / 100, AudioFormatConverter.MaxInputFrames)];
        for (var done = 0; done + packet.Length <= total; done += packet.Length)
        {
            for (var i = 0; i < packet.Length; i++)
            {
                packet[i] = 0.5f * MathF.Sin((float)(2 * Math.PI * Frequency * (done + i) / sourceRate));
            }

            var frames = converter.Convert(MemoryMarshal.AsBytes(packet.AsSpan()), false, output);
            result.AddRange(output[..frames]);
        }

        // The length comes out right to within the resampler's few frames of latency.
        Assert.InRange(result.Count, 2 * 48000 - 600, 2 * 48000 + 64);

        var settled = result.Skip(2000).ToArray();
        var tone = ToneLevel(settled, Frequency);
        Assert.InRange(tone, 0.45, 0.55);
        Assert.True(ToneLevel(settled, Frequency * 1.3) < 0.02, "Energy away from the tone");
    }

    [Fact]
    public void Resampling_a_constant_gives_the_same_constant()
    {
        var converter = new AudioFormatConverter(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1), 1);
        var output = new float[converter.MaxOutputFrames];
        var packet = Enumerable.Repeat(0.25f, 441).ToArray();
        var last = 0;
        for (var i = 0; i < 20; i++)
        {
            last = converter.Convert(MemoryMarshal.AsBytes(packet.AsSpan()), false, output);
        }

        Assert.True(last > 0);
        Assert.All(output[..last], sample => Assert.Equal(0.25f, sample, 3));
    }

    [Theory]
    [InlineData(48000, 2, 2)]
    [InlineData(44100, 2, 2)]
    [InlineData(44100, 2, 1)]
    [InlineData(96000, 6, 2)]
    [InlineData(16000, 1, 1)]
    public void Converting_allocates_nothing(int sourceRate, int inputChannels, int outputChannels)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, inputChannels);
        var converter = new AudioFormatConverter(format, outputChannels);
        var output = new float[converter.MaxOutputFrames * outputChannels];
        var input = new byte[AudioFormatConverter.MaxInputFrames * format.BlockAlign];

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            converter.Convert(input, false, output);
            converter.Convert(input.AsSpan(0, 512 * format.BlockAlign), i % 2 == 0, output);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static double ToneLevel(float[] samples, double frequency)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / 48000);
        double previous = 0, beforePrevious = 0;
        foreach (var sample in samples)
        {
            var current = sample + coefficient * previous - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        var power = previous * previous + beforePrevious * beforePrevious - coefficient * previous * beforePrevious;
        return 2 * Math.Sqrt(Math.Max(0, power)) / samples.Length;
    }
}
