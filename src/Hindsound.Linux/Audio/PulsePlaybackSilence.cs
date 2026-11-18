namespace Hindsound.Linux.Audio;

/// <summary>
/// The silence of the PC's sound while the app plays a clip. The monitor of an output carries everything the output plays,
/// and the server cannot leave one application out of it: so while a preview plays, the capture of the default output's
/// monitor writes silence instead, for as long as the packets would be, and the microphone goes on. Without it the clip
/// being listened to would end up in the memory, and in the next clip.
/// <para>
/// Each playing track holds the silence. It lifts <see cref="TailMs"/> after the last hold is released: the monitor gets what
/// the output renders a little after the stream has stopped or drained. A hold also lifts by itself at its own deadline, so
/// a track that is never closed cannot leave the capture silent.
/// </para>
/// Nothing here touches the server: the player and the capture each call it from their loop's thread, and a test from its own.
/// </summary>
internal sealed class PulsePlaybackSilence
{
    /// <summary>How long the silence lasts after the last hold is released.</summary>
    public const int TailMs = 300;

    private readonly Func<long> _nowMs;
    private readonly Lock _gate = new();
    private readonly List<Hold> _holds = [];
    private long _quietUntilMs = long.MinValue;

    /// <param name="nowMs">The clock, for a test. A monotonic count by default.</param>
    public PulsePlaybackSilence(Func<long>? nowMs = null)
    {
        _nowMs = nowMs ?? (static () => Environment.TickCount64);
    }

    /// <summary>Whether the capture of the PC's sound writes silence now.</summary>
    public bool IsSilent
    {
        get
        {
            var now = _nowMs();
            lock (_gate)
            {
                _holds.RemoveAll(hold => now >= hold.DeadlineMs);
                return _holds.Count > 0 || now < _quietUntilMs;
            }
        }
    }

    /// <summary>Silences the capture from now until the hold is released, or <paramref name="longest"/> has passed.</summary>
    public Hold Silence(TimeSpan longest)
    {
        var now = _nowMs();
        var span = longest < TimeSpan.Zero ? 0 : (long)Math.Min(longest.TotalMilliseconds, int.MaxValue);
        var hold = new Hold(this, now + span);
        lock (_gate)
        {
            _holds.Add(hold);
        }

        return hold;
    }

    private void Release(Hold hold)
    {
        var now = _nowMs();
        lock (_gate)
        {
            // A hold already past its deadline lifted the silence then: releasing it late adds no tail.
            if (_holds.Remove(hold) && now < hold.DeadlineMs)
            {
                _quietUntilMs = Math.Max(_quietUntilMs, now + TailMs);
            }
        }
    }

    /// <summary>One track's share of the silence. Releasing it twice is releasing it once.</summary>
    internal sealed class Hold(PulsePlaybackSilence owner, long deadlineMs)
    {
        private int _released;

        public long DeadlineMs { get; } = deadlineMs;

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(this);
            }
        }
    }
}
