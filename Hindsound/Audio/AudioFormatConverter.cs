using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using NAudio.Dsp;
using NAudio.Wave;

namespace Hindsound.Audio;

/// <summary>
/// Turns raw WASAPI packets of any PCM or float format into <see cref="InternalFormat"/>: decodes the samples,
/// maps the channels and resamples to 48 kHz. All memory is taken in the constructor, so <see cref="Convert"/>
/// allocates nothing and can run in the audio callback. Feed it at most <see cref="MaxInputFrames"/> at a time.
/// </summary>
public sealed class AudioFormatConverter
{
    /// <summary>The most input frames one call takes, about 85 ms at 48 kHz, more than any WASAPI packet seen.</summary>
    public const int MaxInputFrames = 4096;

    private enum SampleKind
    {
        Unsigned8,
        Signed16,
        Signed24,
        Signed32,
        Float32,
        Float64,
    }

    private readonly SampleKind _kind;
    private readonly int _inputChannels;
    private readonly int _outputChannels;
    private readonly int _blockAlign;
    private readonly float[]? _mix;
    private readonly float[] _decoded;
    private readonly float[]? _mixed;
    private readonly WdlResampler? _resampler;
    private readonly int _resampledCapacityFrames;

    /// <param name="format">The format of the packets, as reported by the capture.</param>
    /// <param name="outputChannels">1 for mono, 2 for interleaved stereo.</param>
    /// <exception cref="NotSupportedException">The format is not PCM or IEEE float.</exception>
    public AudioFormatConverter(WaveFormat format, int outputChannels)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputChannels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(outputChannels, 2);

        _kind = KindOf(format);
        _inputChannels = format.Channels;
        _outputChannels = outputChannels;
        _blockAlign = format.BlockAlign;
        _mix = BuildMix(format, outputChannels);
        _decoded = new float[MaxInputFrames * _inputChannels];

        var needsResampling = format.SampleRate != InternalFormat.SampleRate;
        var outputFrames = needsResampling
            ? (int)Math.Ceiling((double)MaxInputFrames * InternalFormat.SampleRate / format.SampleRate) + 64
            : MaxInputFrames;
        MaxOutputFrames = outputFrames;
        _resampledCapacityFrames = outputFrames;

        if (needsResampling)
        {
            _mixed = new float[MaxInputFrames * outputChannels];
            _resampler = new WdlResampler();
            _resampler.SetMode(true, 2, false);
            _resampler.SetFilterParms();
            _resampler.SetFeedMode(true);
            _resampler.SetRates(format.SampleRate, InternalFormat.SampleRate);

            // Run the largest packet through once, so the resampler's own buffers are grown here and not in the callback.
            var warmUp = new float[outputFrames * outputChannels];
            Resample(MaxInputFrames, warmUp);
            _resampler.Reset();
        }
    }

    /// <summary>The largest number of bytes <see cref="Convert"/> accepts in one call.</summary>
    public int MaxInputBytes => MaxInputFrames * _blockAlign;

    /// <summary>The most output frames one call can produce: size the output span for this.</summary>
    public int MaxOutputFrames { get; }

    /// <summary>Bytes per input frame.</summary>
    public int BlockAlign => _blockAlign;

    /// <summary>Converts one packet. A partial frame at the end is ignored.</summary>
    /// <param name="input">At most <see cref="MaxInputBytes"/> bytes.</param>
    /// <param name="silent">The packet is flagged silent: WASAPI says to ignore its content and treat it as zeros.</param>
    /// <param name="output">Room for <see cref="MaxOutputFrames"/> frames.</param>
    /// <returns>The number of frames written to <paramref name="output"/>.</returns>
    public int Convert(ReadOnlySpan<byte> input, bool silent, Span<float> output)
    {
        var frames = Math.Min(input.Length / _blockAlign, MaxInputFrames);
        if (frames == 0)
        {
            return 0;
        }

        if (_resampler is null)
        {
            WriteFrames(input, frames, silent, output);
            return frames;
        }

        WriteFrames(input, frames, silent, _mixed!);
        return Resample(frames, output);
    }

    private int Resample(int frames, Span<float> output)
    {
        var wanted = _resampler!.ResamplePrepare(frames, _outputChannels, out var target);
        var usable = Math.Min(frames, wanted);
        _mixed.AsSpan(0, usable * _outputChannels).CopyTo(target);
        return _resampler.ResampleOut(output, usable, _resampledCapacityFrames, _outputChannels);
    }

    private void WriteFrames(ReadOnlySpan<byte> input, int frames, bool silent, Span<float> target)
    {
        if (silent)
        {
            target[..(frames * _outputChannels)].Clear();
            return;
        }

        var decoded = _decoded.AsSpan(0, frames * _inputChannels);
        Decode(input[..(frames * _blockAlign)], decoded);
        Mix(decoded, frames, target);
    }

    private void Decode(ReadOnlySpan<byte> input, Span<float> output)
    {
        switch (_kind)
        {
            case SampleKind.Float32:
                MemoryMarshal.Cast<byte, float>(input)[..output.Length].CopyTo(output);
                break;

            case SampleKind.Float64:
                var doubles = MemoryMarshal.Cast<byte, double>(input);
                for (var i = 0; i < output.Length; i++)
                {
                    output[i] = (float)doubles[i];
                }

                break;

            case SampleKind.Signed32:
                var ints = MemoryMarshal.Cast<byte, int>(input);
                for (var i = 0; i < output.Length; i++)
                {
                    output[i] = (float)(ints[i] / 2147483648.0);
                }

                break;

            case SampleKind.Signed24:
                for (var i = 0; i < output.Length; i++)
                {
                    var slice = input.Slice(i * 3, 3);
                    var value = (slice[0] << 8) | (slice[1] << 16) | (slice[2] << 24);
                    output[i] = (float)(value / 2147483648.0);
                }

                break;

            case SampleKind.Signed16:
                var shorts = MemoryMarshal.Cast<byte, short>(input);
                for (var i = 0; i < output.Length; i++)
                {
                    output[i] = shorts[i] / 32768f;
                }

                break;

            case SampleKind.Unsigned8:
                for (var i = 0; i < output.Length; i++)
                {
                    output[i] = (input[i] - 128) / 128f;
                }

                break;
        }
    }

    private void Mix(ReadOnlySpan<float> decoded, int frames, Span<float> target)
    {
        if (_mix is null)
        {
            decoded[..(frames * _outputChannels)].CopyTo(target);
            return;
        }

        var mix = _mix;
        for (var frame = 0; frame < frames; frame++)
        {
            var source = decoded.Slice(frame * _inputChannels, _inputChannels);
            for (var output = 0; output < _outputChannels; output++)
            {
                var sum = 0f;
                for (var channel = 0; channel < source.Length; channel++)
                {
                    sum += source[channel] * mix[output * _inputChannels + channel];
                }

                target[frame * _outputChannels + output] = sum;
            }
        }
    }

    private static SampleKind KindOf(WaveFormat format)
    {
        // An extensible format says PCM or float in its SubFormat. 32 bits alone does not tell which it is.
        var standard = format.AsStandardWaveFormat();
        return (standard.Encoding, standard.BitsPerSample) switch
        {
            (WaveFormatEncoding.IeeeFloat, 32) => SampleKind.Float32,
            (WaveFormatEncoding.IeeeFloat, 64) => SampleKind.Float64,
            (WaveFormatEncoding.Pcm, 8) => SampleKind.Unsigned8,
            (WaveFormatEncoding.Pcm, 16) => SampleKind.Signed16,
            (WaveFormatEncoding.Pcm, 24) => SampleKind.Signed24,
            (WaveFormatEncoding.Pcm, 32) => SampleKind.Signed32,
            _ => throw new NotSupportedException($"Unsupported capture format: {format}"),
        };
    }

    // Speaker bits of WAVEFORMATEXTENSIBLE, in channel order.
    private const int FrontLeft = 0x1;
    private const int FrontRight = 0x2;
    private const int FrontCenter = 0x4;
    private const int Lfe = 0x8;
    private const int BackLeft = 0x10;
    private const int BackRight = 0x20;
    private const int FrontLeftOfCenter = 0x40;
    private const int FrontRightOfCenter = 0x80;
    private const int BackCenter = 0x100;
    private const int SideLeft = 0x200;
    private const int SideRight = 0x400;

    /// <summary>
    /// The output-by-input gain matrix, or null when channels pass through as they are. Mono output is the plain
    /// average. Stereo output follows the usual downmix (centre and surrounds at -3 dB, LFE dropped), scaled so
    /// that all channels at full level stay within full scale.
    /// </summary>
    private static float[]? BuildMix(WaveFormat format, int outputChannels)
    {
        var inputs = format.Channels;
        if (inputs == outputChannels)
        {
            return null;
        }

        var mix = new float[outputChannels * inputs];
        if (outputChannels == 1)
        {
            Array.Fill(mix, 1f / inputs);
            return mix;
        }

        if (inputs == 1)
        {
            mix[0] = mix[1] = 1f;
            return mix;
        }

        var speakers = SpeakersOf(format);
        for (var channel = 0; channel < inputs; channel++)
        {
            var (left, right) = StereoGains(speakers[channel]);
            mix[channel] = left;
            mix[inputs + channel] = right;
        }

        for (var output = 0; output < outputChannels; output++)
        {
            var row = mix.AsSpan(output * inputs, inputs);
            var sum = 0f;
            foreach (var gain in row)
            {
                sum += gain;
            }

            for (var channel = 0; channel < inputs; channel++)
            {
                row[channel] /= sum;
            }
        }

        return mix;
    }

    private static int[] SpeakersOf(WaveFormat format)
    {
        var channels = format.Channels;
        var mask = format is WaveFormatExtensible extensible ? extensible.ChannelMask : 0;
        if (BitOperations.PopCount((uint)mask) != channels)
        {
            mask = channels switch
            {
                3 => FrontLeft | FrontRight | FrontCenter,
                4 => FrontLeft | FrontRight | BackLeft | BackRight,
                5 => FrontLeft | FrontRight | FrontCenter | BackLeft | BackRight,
                6 => FrontLeft | FrontRight | FrontCenter | Lfe | BackLeft | BackRight,
                7 => FrontLeft | FrontRight | FrontCenter | Lfe | BackCenter | SideLeft | SideRight,
                8 => FrontLeft | FrontRight | FrontCenter | Lfe | BackLeft | BackRight | SideLeft | SideRight,
                _ => 0,
            };
        }

        var speakers = new int[channels];
        for (var channel = 0; channel < channels; channel++)
        {
            if (mask == 0)
            {
                // No known layout: odd and even channels go left and right.
                speakers[channel] = channel % 2 == 0 ? FrontLeft : FrontRight;
                continue;
            }

            var bit = mask & -mask;
            speakers[channel] = bit;
            mask &= ~bit;
        }

        return speakers;
    }

    private static (float Left, float Right) StereoGains(int speaker)
    {
        const float Minus3Db = 0.70710678f;
        return speaker switch
        {
            FrontLeft => (1f, 0f),
            FrontRight => (0f, 1f),
            FrontCenter or BackCenter => (Minus3Db, Minus3Db),
            Lfe => (0f, 0f),
            BackLeft or SideLeft or FrontLeftOfCenter => (Minus3Db, 0f),
            BackRight or SideRight or FrontRightOfCenter => (0f, Minus3Db),
            _ => (0.5f, 0.5f),
        };
    }
}
