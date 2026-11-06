using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hindsound.Audio;

/// <summary>
/// What runs in the audio callback, for one open capture: stamp the packet on the timeline, convert it, put it on
/// the track. All memory is taken in the constructor; <see cref="OnPacket"/> allocates nothing.
/// </summary>
public sealed class CapturePipeline
{
    // A stamp further than this from the current time is not believed.
    private const long MaxStampErrorTicks = 2 * 10_000_000;

    private readonly AudioFormatConverter _converter;
    private readonly SourceTrack _track;
    private readonly CaptureTimeline _timeline;
    private readonly float[] _output;
    private readonly int _sampleRate;
    private bool _nextIsExact = true;
    private long _packets;

    public CapturePipeline(WaveFormat format, SourceTrack track, CaptureTimeline timeline)
    {
        _converter = new AudioFormatConverter(format, track.Channels);
        _track = track;
        _timeline = timeline;
        _sampleRate = format.SampleRate;
        _output = new float[_converter.MaxOutputFrames * track.Channels];
    }

    /// <summary>Packets holding audio received so far.</summary>
    public long Packets => Volatile.Read(ref _packets);

    /// <summary>Has the signature of <see cref="CaptureDataAvailableHandler"/>.</summary>
    public void OnPacket(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        var inputFrames = buffer.Length / _converter.BlockAlign;
        if (inputFrames == 0)
        {
            return;
        }

        Volatile.Write(ref _packets, _packets + 1);
        if (_timeline.IsFrozen)
        {
            // Stopping and resuming happen with this source still delivering: what it delivers then has no place.
            _nextIsExact = true;
            return;
        }

        var startFrame = _timeline.ToFrames(StampOf(flags, qpcPosition, inputFrames));
        var exact = _nextIsExact || (flags & AudioClientBufferFlags.DataDiscontinuity) != 0;
        _nextIsExact = false;
        var silent = (flags & AudioClientBufferFlags.Silent) != 0;
        var channels = _track.Channels;

        var first = true;
        while (buffer.Length >= _converter.BlockAlign)
        {
            var chunk = buffer[..Math.Min(buffer.Length, _converter.MaxInputBytes)];
            buffer = buffer[chunk.Length..];

            var frames = _converter.Convert(chunk, silent, _output);
            var samples = _output.AsSpan(0, frames * channels);
            if (first)
            {
                _track.Append(startFrame, samples, exact);
                first = false;
            }
            else
            {
                _track.AppendContinuation(samples);
            }
        }
    }

    // The packet's own stamp says when its first frame was captured, whatever the scheduler did to this callback.
    // Without a usable one, the packet has just been received, so it started its own length ago.
    private long StampOf(AudioClientBufferFlags flags, long qpcPosition, int inputFrames)
    {
        var now = _timeline.NowTicks;
        var usable = (flags & AudioClientBufferFlags.TimestampError) == 0
            && qpcPosition > 0
            && Math.Abs(qpcPosition - now) <= MaxStampErrorTicks;
        return usable ? qpcPosition : now - CaptureTimeline.FramesToTicks(inputFrames, _sampleRate);
    }
}
