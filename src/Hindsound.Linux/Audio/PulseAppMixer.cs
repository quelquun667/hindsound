namespace Hindsound.Linux.Audio;

/// <summary>
/// Mixes the record streams of one application into one stream that never stops. Each stream writes its samples as the server
/// gives them, on the server's thread; the capture's own clock takes as many frames as its time asks for, from every stream at
/// once, and sums them. A stream starts to count once it holds <see cref="PrimeFrames"/>, so that a packet a little late does
/// not cut its sound; one that runs dry (its stream stopped) waits for that much again. One that holds more than
/// <see cref="MaxFrames"/> loses its oldest frames. With no stream, or none ready, the frames taken are silence, and time
/// still goes on.
/// <para>
/// The card's clock and this one never run at quite the same speed, and the engine dates the frames by this one. So each
/// stream is kept near what it held when it settled (the mean of its first reads, within <see cref="MarginFrames"/> of
/// <see cref="PrimeFrames"/>): while what it holds, on average, is more than <see cref="MarginFrames"/> above that, each read
/// takes one frame more than it gives, and while it is that much below, one frame less, both over a short fade. A card a
/// little faster then never fills the ring nor delays the application against the microphone, and a card a little slower
/// never runs it dry.
/// </para>
/// <para>
/// Nothing is allocated past <see cref="Add"/>, so hours of streams that come and go hold no more memory than those alive.
/// Every call is safe from any thread.
/// </para>
/// </summary>
internal sealed class PulseAppMixer
{
    private readonly Lock _gate = new();
    private readonly Dictionary<PulseMonitorKey, Input> _inputs = [];
    private readonly int _channels;
    private long _corrections;

    /// <param name="channels">The channels of every stream, and of the mix.</param>
    /// <param name="primeFrames">How much a stream holds before it counts.</param>
    /// <param name="maxFrames">How much a stream holds at most.</param>
    public PulseAppMixer(int channels, int primeFrames, int maxFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegative(primeFrames);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, Math.Max(1, primeFrames));
        _channels = channels;
        PrimeFrames = primeFrames;
        MaxFrames = maxFrames;
    }

    public int PrimeFrames { get; }

    public int MaxFrames { get; }

    /// <summary>How far from <see cref="PrimeFrames"/> what a stream holds, on average, may go before its reads are corrected.</summary>
    public int MarginFrames => PrimeFrames / 4;

    /// <summary>How many reads, over all streams, gave one frame more or one less since the start. For the tests.</summary>
    internal long Corrections
    {
        get
        {
            lock (_gate)
            {
                return _corrections;
            }
        }
    }

    /// <summary>The frames a stream holds now, 0 for a stream not there.</summary>
    public int Held(PulseMonitorKey key)
    {
        lock (_gate)
        {
            return _inputs.TryGetValue(key, out var input) ? input.Count / _channels : 0;
        }
    }

    /// <summary>The streams being mixed.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _inputs.Count;
            }
        }
    }

    /// <summary>Starts mixing a stream, empty. A stream already there is kept as it is.</summary>
    public void Add(PulseMonitorKey key)
    {
        lock (_gate)
        {
            if (!_inputs.ContainsKey(key))
            {
                _inputs[key] = new Input(new float[MaxFrames * _channels]);
            }
        }
    }

    /// <summary>Stops mixing a stream; what it held is dropped. Nothing happens for a stream not there.</summary>
    public void Remove(PulseMonitorKey key)
    {
        lock (_gate)
        {
            _inputs.Remove(key);
        }
    }

    /// <summary>Adds samples, interleaved, to a stream. Samples for a stream not there (removed meanwhile) are dropped.</summary>
    public void Write(PulseMonitorKey key, ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            if (_inputs.TryGetValue(key, out var input))
            {
                input.Write(samples, isSilence: false);
            }
        }
    }

    /// <summary>Adds silence to a stream: a stretch the server had no data for.</summary>
    public void WriteSilence(PulseMonitorKey key, int frames)
    {
        lock (_gate)
        {
            if (_inputs.TryGetValue(key, out var input))
            {
                input.WriteSilence(frames * _channels);
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="output"/> with the next frames of the mix (its length, a whole number of frames).
    /// </summary>
    /// <returns>Whether a stream gave any of them: false when the mix is silence for want of a stream.</returns>
    public bool Mix(Span<float> output)
    {
        output.Clear();
        var frames = output.Length / _channels;
        var heard = false;
        lock (_gate)
        {
            foreach (var input in _inputs.Values)
            {
                heard |= input.ReadInto(output[..(frames * _channels)], _channels, PrimeFrames, MarginFrames, ref _corrections);
            }
        }

        return heard;
    }

    /// <summary>One stream's queue: a ring of samples.</summary>
    private sealed class Input(float[] ring)
    {
        // How many reads the average of what a stream holds follows: about two thirds of a second at a read each 10 ms, so
        // that the packets of the server, which come by 20 ms, do not move it.
        private const int AverageReads = 64;

        // The fade over which a read gives one frame more or one less: 5 ms.
        private const int FadeFrames = 240;

        private int _start;
        private int _count;
        private bool _ready;
        private int _reads;
        private double _average;
        private double _reference;

        public int Count => _count;

        public void Write(ReadOnlySpan<float> samples, bool isSilence)
        {
            // More than the ring holds: only its end can stay.
            if (samples.Length > ring.Length)
            {
                samples = samples[^ring.Length..];
            }

            var overflow = _count + samples.Length - ring.Length;
            if (overflow > 0)
            {
                Skip(overflow);
            }

            var end = (_start + _count) % ring.Length;
            var first = Math.Min(samples.Length, ring.Length - end);
            if (isSilence)
            {
                ring.AsSpan(end, first).Clear();
                ring.AsSpan(0, samples.Length - first).Clear();
            }
            else
            {
                samples[..first].CopyTo(ring.AsSpan(end));
                samples[first..].CopyTo(ring);
            }

            _count += samples.Length;
        }

        // The ring only gives a length here, as silence is not copied: a stretch longer than the ring is the same as its end.
        public void WriteSilence(int samples) => Write(new ReadOnlySpan<float>(ring, 0, Math.Min(samples, ring.Length)), isSilence: true);

        // Adds the next frames to the mix. A stream not ready yet adds nothing; one that runs dry gives what it has. One whose
        // average is out of the margin around the target gives its frames over one more or one less, the last ones faded.
        public bool ReadInto(Span<float> output, int channels, int target, int margin, ref long corrections)
        {
            var frames = output.Length / channels;
            var held = _count / channels;
            if (!_ready)
            {
                if (held < Math.Max(target, 1))
                {
                    return false;
                }

                // For the first reads the average is a plain mean and nothing is corrected: the level a stream settles at is not
                // the one it is first seen at (a packet or two above it, or the saw of 20 ms packets read by 10 ms), and a
                // correction chasing the first one is a hundred of them for nothing.
                _ready = true;
                _reads = 1;
                _average = held;
                _reference = target;
            }
            else if (_reads < AverageReads)
            {
                _reads++;
                _average += (held - _average) / _reads;
                if (_reads == AverageReads)
                {
                    // The level it settled at is the one kept, within a margin of the target so that the delay cannot be far off.
                    _reference = Math.Clamp(_average, target - margin, target + margin);
                }
            }
            else
            {
                _average += (held - _average) / AverageReads;
            }

            // 1: one frame skipped, -1: one frame given twice, 0: as they are.
            var shift = 0;
            if (_reads < AverageReads)
            {
                // Still taking the mean.
            }
            else if (frames > 1 && _average > _reference + margin && held > frames)
            {
                shift = 1;
            }
            else if (frames > 1 && _average < _reference - margin && held >= frames)
            {
                shift = -1;
            }

            if (shift != 0)
            {
                corrections++;
            }

            var take = shift == 0 ? Math.Min(held, frames) : frames + shift;
            var fade = shift == 0 ? 0 : Math.Min(FadeFrames, frames - 1);
            var plain = (shift == 0 ? take : frames - fade) * channels;
            for (var i = 0; i < plain; i++)
            {
                output[i] += ring[(_start + i) % ring.Length];
            }

            // Each faded frame slides from its own to the one after (or before) it, so the read ends one frame off, with no jump.
            for (var frame = frames - fade; frame < frames; frame++)
            {
                var weight = (float)(frame - (frames - fade) + 1) / fade;
                for (var channel = 0; channel < channels; channel++)
                {
                    var current = ring[(_start + frame * channels + channel) % ring.Length];
                    var next = ring[(_start + (frame + shift) * channels + channel) % ring.Length];
                    output[frame * channels + channel] += current + (next - current) * weight;
                }
            }

            Skip(take * channels);
            _average -= shift;
            if (take < frames && shift == 0)
            {
                _ready = false;
            }

            return take > 0;
        }

        private void Skip(int samples)
        {
            _start = (_start + samples) % ring.Length;
            _count -= samples;
        }
    }
}
