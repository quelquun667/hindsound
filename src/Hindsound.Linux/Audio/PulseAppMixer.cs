namespace Hindsound.Linux.Audio;

/// <summary>
/// Mixes the record streams of one application into one stream that never stops. Each stream writes its samples as the server
/// gives them, on the server's thread; the capture's own clock takes as many frames as its time asks for, from every stream at
/// once, and sums them. A stream starts to count once it holds <see cref="PrimeFrames"/>, so that a packet a little late does
/// not cut its sound; one that runs dry waits for that much again. One that holds more than <see cref="MaxFrames"/> loses its
/// oldest frames: the delay stays bounded when the server's clock runs a little faster than this one. With no stream, or
/// none ready, the frames taken are silence, and time still goes on.
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
                heard |= input.ReadInto(output[..(frames * _channels)], PrimeFrames * _channels);
            }
        }

        return heard;
    }

    // DIAG: the samples dropped for want of room, and the times a stream ran dry, since the last call.
    public string DiagCounters()
    {
        lock (_gate)
        {
            var text = string.Join(" ", _inputs.Select(pair => $"[{pair.Key.SinkInput}: held {pair.Value.Held / _channels} dropped {pair.Value.Dropped / _channels} dry {pair.Value.Dry}]"));
            foreach (var input in _inputs.Values)
            {
                input.Dropped = 0;
                input.Dry = 0;
            }

            return text;
        }
    }

    /// <summary>One stream's queue: a ring of samples.</summary>
    private sealed class Input(float[] ring)
    {
        private int _start;
        private int _count;
        private bool _ready;

        public long Dropped;
        public int Dry;

        public int Held => _count;

        public void Write(ReadOnlySpan<float> samples, bool isSilence)
        {
            // More than the ring holds: only its end can stay.
            if (samples.Length > ring.Length)
            {
                Dropped += samples.Length - ring.Length;
                samples = samples[^ring.Length..];
            }

            var overflow = _count + samples.Length - ring.Length;
            if (overflow > 0)
            {
                Dropped += overflow;
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

        // Adds the next samples to the mix. A stream not ready yet adds nothing; one that runs dry gives what it has.
        public bool ReadInto(Span<float> output, int prime)
        {
            if (!_ready)
            {
                if (_count < Math.Max(prime, 1))
                {
                    return false;
                }

                _ready = true;
            }

            var take = Math.Min(_count, output.Length);
            for (var i = 0; i < take; i++)
            {
                output[i] += ring[(_start + i) % ring.Length];
            }

            Skip(take);
            if (take < output.Length)
            {
                Dry++;
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
