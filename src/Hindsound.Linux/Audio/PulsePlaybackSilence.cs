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
/// <para>
/// The silence is kept for a few seconds after it lifts, so that a packet can be judged by when its sound was captured
/// (<see cref="IsSilentAt"/>) and not only by when the capture's thread reads it: a thread that was late still silences the
/// packets that carry the preview.
/// </para>
/// Nothing here touches the server: the player and the capture each call it from their loop's thread, and a test from its own.
/// </summary>
internal sealed class PulsePlaybackSilence
{
    /// <summary>How long the silence lasts after the last hold is released.</summary>
    public const int TailMs = 300;

    /// <summary>How long a silent stretch is remembered after it ends: the most a late reader can be behind.</summary>
    public const int HistoryMs = 10_000;

    private readonly Func<long> _nowMs;
    private readonly Lock _gate = new();
    private readonly List<Hold> _holds = [];

    /// <param name="nowMs">The clock, for a test. A monotonic count by default.</param>
    public PulsePlaybackSilence(Func<long>? nowMs = null)
    {
        _nowMs = nowMs ?? (static () => Environment.TickCount64);
    }

    /// <summary>Whether the capture of the PC's sound writes silence now.</summary>
    public bool IsSilent => IsSilentAt(_nowMs());

    /// <summary>Whether the capture of the PC's sound was silent <paramref name="millisecondsAgo"/> before now.</summary>
    public bool IsSilentAgo(long millisecondsAgo) => IsSilentAt(_nowMs() - Math.Max(millisecondsAgo, 0));

    /// <summary>Whether the capture was silent at <paramref name="ms"/> on this clock. Before the oldest stretch kept, it was not.</summary>
    public bool IsSilentAt(long ms)
    {
        var now = _nowMs();
        lock (_gate)
        {
            _holds.RemoveAll(hold => hold.EndMs < now - HistoryMs);
            return _holds.Exists(hold => hold.StartMs <= ms && ms < hold.EndMs);
        }
    }

    /// <summary>Silences the capture from now until the hold is released, or <paramref name="longest"/> has passed.</summary>
    public Hold Silence(TimeSpan longest)
    {
        var now = _nowMs();
        var hold = new Hold(this, now, now + SpanOf(longest));
        lock (_gate)
        {
            _holds.Add(hold);
        }

        return hold;
    }

    private static long SpanOf(TimeSpan span) => span < TimeSpan.Zero ? 0 : (long)Math.Min(span.TotalMilliseconds, int.MaxValue);

    private void Release(Hold hold)
    {
        var now = _nowMs();
        lock (_gate)
        {
            hold.ReleasedMs = Math.Min(hold.ReleasedMs, now);
        }
    }

    /// <summary>One track's share of the silence. Releasing it twice is releasing it once.</summary>
    internal sealed class Hold(PulsePlaybackSilence owner, long startMs, long deadlineMs)
    {
        private int _released;

        // Read and written under the owner's lock.
        internal long StartMs { get; } = startMs;

        internal long DeadlineMs { get; set; } = deadlineMs;

        internal long ReleasedMs { get; set; } = long.MaxValue;

        // A hold released before its deadline silences the tail more; one released after it lifted at the deadline, with no tail.
        internal long EndMs => ReleasedMs < DeadlineMs ? ReleasedMs + TailMs : DeadlineMs;

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(this);
            }
        }
    }
}
