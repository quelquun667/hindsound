using System.Runtime.InteropServices;
using Hindsound.Audio;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Hindsound.Tests.Audio;

/// <summary>Measures on a signal, by calculation: nothing here plays or records anything.</summary>
internal static class SignalMath
{
    public static float[] Resample(int rate, float[] input)
    {
        var resampler = new Resampler(rate, 48000, 1, 4096);
        var result = new List<float>((int)(input.Length * 48000L / rate) + 8);
        var output = new float[resampler.MaxOutputFrames];
        var packet = Math.Clamp(rate / 100, 1, 4096);
        for (var at = 0; at < input.Length; at += packet)
        {
            var frames = resampler.Process(input.AsSpan(at, Math.Min(packet, input.Length - at)), output);
            result.AddRange(output.AsSpan(0, frames));
        }

        return [.. result];
    }

    public static float[] Tone(int rate, double frequency, double amplitude, double seconds)
    {
        var samples = new float[(int)(rate * seconds)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate));
        }

        return samples;
    }

    public static float[] Pulse(int rate, double atSeconds, double seconds)
    {
        const double Width = 0.0002;
        var samples = new float[(int)(rate * seconds)];
        for (var i = 0; i < samples.Length; i++)
        {
            var distance = (i / (double)rate - atSeconds) / Width;
            samples[i] = (float)(0.8 * Math.Exp(-0.5 * distance * distance));
        }

        return samples;
    }

    /// <summary>Where the energy sits, in frames.</summary>
    public static double Centroid(ReadOnlySpan<float> samples)
    {
        double moment = 0, energy = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var square = (double)samples[i] * samples[i];
            moment += i * square;
            energy += square;
        }

        return moment / energy;
    }

    public static double Rms(ReadOnlySpan<float> samples)
    {
        double energy = 0;
        foreach (var sample in samples)
        {
            energy += (double)sample * sample;
        }

        return Math.Sqrt(energy / samples.Length);
    }

    public static double Db(double level) => 20 * Math.Log10(Math.Max(level, 1e-12));

    /// <summary>Least squares fit of a tone at 48 kHz: its amplitude, and how far it stands above everything else.</summary>
    public static (double Amplitude, double SinadDb) FitTone(ReadOnlySpan<float> samples, double frequency)
    {
        double ss = 0, cc = 0, sc = 0, ys = 0, yc = 0, yy = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var angle = 2 * Math.PI * frequency * i / 48000;
            var s = Math.Sin(angle);
            var c = Math.Cos(angle);
            ss += s * s;
            cc += c * c;
            sc += s * c;
            ys += samples[i] * s;
            yc += samples[i] * c;
            yy += (double)samples[i] * samples[i];
        }

        var det = ss * cc - sc * sc;
        var a = (ys * cc - yc * sc) / det;
        var b = (yc * ss - ys * sc) / det;
        var fitted = a * ys + b * yc;
        return (Math.Sqrt(a * a + b * b), 10 * Math.Log10(fitted / Math.Max(yy - fitted, 1e-30)));
    }
}

public class ResamplerGuardTests
{
    [Theory]
    [InlineData(8000)]
    [InlineData(16000)]
    [InlineData(22050)]
    [InlineData(44100)]
    [InlineData(96000)]
    [InlineData(192000)]
    public void A_pulse_comes_out_at_the_instant_it_went_in(int rate)
    {
        const double At = 0.5123;

        var output = SignalMath.Resample(rate, SignalMath.Pulse(rate, At, 1));

        var around = (int)(At * 48000) - 500;
        var lagMs = (SignalMath.Centroid(output.AsSpan(around, 1000)) + around - At * 48000) / 48;
        Assert.True(Math.Abs(lagMs) < 0.01, $"Late by {lagMs:F4} ms");
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(16000)]
    [InlineData(96000)]
    public void A_pulse_keeps_its_instant_through_the_whole_capture_path(int rate)
    {
        const double At = 0.7037;
        var clock = new FakeClock();
        var timeline = new CaptureTimeline(() => clock.Ticks);
        var track = new SourceTrack(1, 48000 * 2, 0);
        var pipeline = new CapturePipeline(WaveFormat.CreateIeeeFloatWaveFormat(rate, 1), track, timeline);
        var input = SignalMath.Pulse(rate, At, 1.5);
        var packet = rate / 100;
        var start = clock.Ticks;

        for (var at = 0; at + packet <= input.Length; at += packet)
        {
            var stamp = start + (long)Math.Round(at * 1e7 / rate);
            clock.Ticks = stamp + (long)Math.Round(packet * 1e7 / rate);
            pipeline.OnPacket(MemoryMarshal.AsBytes(input.AsSpan(at, packet)), AudioClientBufferFlags.None, 0, stamp);
        }

        var around = (int)(At * 48000) - 500;
        var stored = new float[1000];
        track.CopyFrames(around, stored);
        var lagMs = (SignalMath.Centroid(stored) + around - At * 48000) / 48;
        Assert.True(Math.Abs(lagMs) < 0.05, $"Late by {lagMs:F4} ms");
    }

    [Theory]
    [InlineData(96000, 30000)]
    [InlineData(96000, 40000)]
    [InlineData(192000, 60000)]
    [InlineData(192000, 90000)]
    public void Going_down_in_rate_what_is_too_high_to_keep_is_gone_and_not_folded_back(int rate, double frequency)
    {
        var output = SignalMath.Resample(rate, SignalMath.Tone(rate, frequency, 0.5, 2));

        var level = SignalMath.Db(SignalMath.Rms(output.AsSpan(2000, 60000)) / (0.5 / Math.Sqrt(2)));
        Assert.True(level < -80, $"Left at {level:F1} dB");
    }

    [Theory]
    [InlineData(44100, 100)]
    [InlineData(44100, 1000)]
    [InlineData(44100, 10000)]
    [InlineData(44100, 16000)]
    [InlineData(96000, 15000)]
    [InlineData(32000, 7500)]
    [InlineData(22050, 5000)]
    public void A_tone_keeps_its_level_and_stays_clean(int rate, double frequency)
    {
        var output = SignalMath.Resample(rate, SignalMath.Tone(rate, frequency, 0.5, 2));

        var (amplitude, sinad) = SignalMath.FitTone(output.AsSpan(2000, 60000), frequency);
        var gain = SignalMath.Db(amplitude / 0.5);
        Assert.True(Math.Abs(gain) < 0.02, $"Gain {gain:F3} dB");
        Assert.True(sinad > 85, $"SINAD {sinad:F1} dB");
    }
}

public class PlacementGuardTests
{
    private static float[] Ramped(long firstFrame, int frames)
    {
        var samples = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            samples[frame] = Ramp.Value(firstFrame + frame);
        }

        return samples;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_empty_packet_is_placed_nowhere(bool exact)
    {
        Assert.Equal(default, SilencePadder.Place(1000, 2000, 0, exact));
        Assert.Equal(default, SilencePadder.Place(1000, 1100, 0, exact));
        Assert.Equal(default, SilencePadder.Place(1000, 500, 0, exact));
    }

    [Fact]
    public void A_slow_clock_adds_exactly_one_frame_and_ends_on_the_last_sample()
    {
        var track = new SourceTrack(1, 48000, 0);
        track.Append(0, Ramped(0, 1000), exact: true);

        track.Append(1100, Ramped(1000, 480), exact: false);

        Assert.Equal(1481, track.EndFrame);
        var stored = new float[481];
        track.CopyFrames(1000, stored);
        Assert.Equal(Ramp.ThroughBuffer(1000), stored[0]);
        Assert.Equal(Ramp.ThroughBuffer(1479), stored[^1]);
    }

    [Fact]
    public void A_fast_clock_removes_exactly_one_frame_and_ends_on_the_last_sample()
    {
        var track = new SourceTrack(1, 48000, 0);
        track.Append(0, Ramped(0, 1000), exact: true);

        track.Append(900, Ramped(1000, 480), exact: false);

        Assert.Equal(1479, track.EndFrame);
        var stored = new float[479];
        track.CopyFrames(1000, stored);
        Assert.Equal(Ramp.ThroughBuffer(1479), stored[^1]);
        Assert.Equal(Ramp.ThroughBuffer(1478), stored[^2]);
    }

    [Fact]
    public void A_large_overlap_drops_exactly_what_overlaps()
    {
        var track = new SourceTrack(1, 48000, 0);
        track.Append(0, Ramped(0, 1000), exact: true);

        track.Append(800, Ramped(800, 480), exact: true);

        Assert.Equal(1280, track.EndFrame);
        var stored = new float[2];
        track.CopyFrames(999, stored);
        Assert.Equal(Ramp.ThroughBuffer(999), stored[0]);
        Assert.Equal(Ramp.ThroughBuffer(1000), stored[1]);
    }

    [Fact]
    public void A_stereo_frame_added_or_removed_keeps_the_channels_apart()
    {
        var track = new SourceTrack(2, 48000, 0);
        var first = new float[2000];
        var packet = new float[960];
        for (var i = 0; i < first.Length; i += 2)
        {
            first[i] = 0.5f;
            first[i + 1] = -0.5f;
        }

        Array.Copy(first, packet, packet.Length);
        track.Append(0, first, exact: true);

        track.Append(1100, packet, exact: false);
        track.Append(1481 + 900 - 1000, packet, exact: false);

        var stored = new float[(int)(track.EndFrame * 2)];
        track.CopyFrames(0, stored);
        for (var i = 0; i < stored.Length; i += 2)
        {
            Assert.Equal(stored[i], -stored[i + 1]);
        }
    }
}

public class ChannelMapGuardTests
{
    private const float Tolerance = 1e-5f;
    private const float Minus3Db = 0.70710678f;

    private static (float Left, float Right) DownmixOne(WaveFormat format, int channel)
    {
        var converter = new AudioFormatConverter(format, 2);
        var input = new float[format.Channels];
        input[channel] = 1f;
        var output = new float[converter.MaxOutputFrames * 2];
        Assert.Equal(1, converter.Convert(MemoryMarshal.AsBytes(input.AsSpan()), false, output));
        return (output[0], output[1]);
    }

    private static WaveFormatExtensible FloatFormat(int channels, int mask) =>
        new(48000, 32, channels, AudioSubtypes.MFAudioFormat_Float, 32, mask);

    [Fact]
    public void Side_speakers_of_a_7_1_go_to_their_own_side_at_minus_3_db()
    {
        // Front left, front right, centre, LFE, back left, back right, side left, side right.
        var format = FloatFormat(8, 0x63F);
        var sum = 1f + 3 * Minus3Db;

        var (sideLeftL, sideLeftR) = DownmixOne(format, 6);
        var (sideRightL, sideRightR) = DownmixOne(format, 7);

        Assert.True(Math.Abs(sideLeftL - Minus3Db / sum) <= Tolerance, $"Side left on the left: {sideLeftL}");
        Assert.Equal(0f, sideLeftR);
        Assert.Equal(0f, sideRightL);
        Assert.True(Math.Abs(sideRightR - Minus3Db / sum) <= Tolerance, $"Side right on the right: {sideRightR}");
    }

    [Fact]
    public void A_channel_mask_that_does_not_fit_the_channels_gives_the_usual_layout()
    {
        // Two bits for six channels: not believed, so this is a 5.1, whose 4th channel is the LFE.
        var format = FloatFormat(6, 0x3);
        var sum = 1f + 2 * Minus3Db;

        Assert.Equal((0f, 0f), DownmixOne(format, 3));
        var (backLeftL, backLeftR) = DownmixOne(format, 4);
        Assert.True(Math.Abs(backLeftL - Minus3Db / sum) <= Tolerance, $"Back left on the left: {backLeftL}");
        Assert.Equal(0f, backLeftR);
    }
}
