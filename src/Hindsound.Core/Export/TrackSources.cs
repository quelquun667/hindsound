using Hindsound.Audio;

namespace Hindsound.Export;

/// <summary>
/// One output track, produced on demand from the snapshot a piece at a time: gain applied, padded with silence at
/// the start if its source is shorter than the clip (the sources end at the same instant). Nothing is copied
/// beyond the piece asked for, and the same range can be asked for again.
/// </summary>
internal abstract class TrackSource
{
    protected TrackSource(int channels, int frames)
    {
        Channels = channels;
        Frames = frames;
    }

    public int Channels { get; }

    public int Frames { get; }

    /// <summary>Writes frames [startFrame, startFrame + count) interleaved into the start of <paramref name="destination"/>.</summary>
    public abstract void Fill(int startFrame, int count, Span<float> destination);
}

/// <summary>PC audio and microphone summed to stereo, the microphone on both channels.</summary>
internal sealed class MixSource : TrackSource
{
    private readonly float[] _system;
    private readonly float[] _mic;
    private readonly float _systemGain;
    private readonly float _micGain;
    private readonly int _systemOffset;
    private readonly int _micOffset;

    public MixSource(AudioSnapshot snapshot, int frames, float systemGain, float micGain) : base(2, frames)
    {
        _system = snapshot.System;
        _mic = snapshot.Mic;
        _systemGain = systemGain;
        _micGain = micGain;
        _systemOffset = frames - _system.Length / InternalFormat.SystemChannels;
        _micOffset = frames - _mic.Length / InternalFormat.MicChannels;
    }

    public override void Fill(int startFrame, int count, Span<float> destination)
    {
        for (var i = 0; i < count; i++)
        {
            var frame = startFrame + i;
            float left = 0f, right = 0f;

            var s = frame - _systemOffset;
            if (s >= 0)
            {
                left = _system[2 * s] * _systemGain;
                right = _system[2 * s + 1] * _systemGain;
            }

            var m = frame - _micOffset;
            if (m >= 0)
            {
                var value = _mic[m] * _micGain;
                left += value;
                right += value;
            }

            destination[2 * i] = left;
            destination[2 * i + 1] = right;
        }
    }
}

/// <summary>The PC audio alone, stereo.</summary>
internal sealed class SystemSource : TrackSource
{
    private readonly float[] _system;
    private readonly float _gain;
    private readonly int _offset;

    public SystemSource(AudioSnapshot snapshot, int frames, float gain) : base(2, frames)
    {
        _system = snapshot.System;
        _gain = gain;
        _offset = frames - _system.Length / InternalFormat.SystemChannels;
    }

    public override void Fill(int startFrame, int count, Span<float> destination)
    {
        for (var i = 0; i < count; i++)
        {
            var s = startFrame + i - _offset;
            destination[2 * i] = s >= 0 ? _system[2 * s] * _gain : 0f;
            destination[2 * i + 1] = s >= 0 ? _system[2 * s + 1] * _gain : 0f;
        }
    }
}

/// <summary>The microphone alone, mono.</summary>
internal sealed class MicSource : TrackSource
{
    private readonly float[] _mic;
    private readonly float _gain;
    private readonly int _offset;

    public MicSource(AudioSnapshot snapshot, int frames, float gain) : base(1, frames)
    {
        _mic = snapshot.Mic;
        _gain = gain;
        _offset = frames - _mic.Length / InternalFormat.MicChannels;
    }

    public override void Fill(int startFrame, int count, Span<float> destination)
    {
        for (var i = 0; i < count; i++)
        {
            var m = startFrame + i - _offset;
            destination[i] = m >= 0 ? _mic[m] * _gain : 0f;
        }
    }
}
