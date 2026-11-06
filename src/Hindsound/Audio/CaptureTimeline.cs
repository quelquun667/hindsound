using System.Diagnostics;

namespace Hindsound.Audio;

/// <summary>
/// The one clock both sources are placed on, counted in frames at 48 kHz. WASAPI stamps each packet with the
/// performance counter in 100 ns units, so a packet maps to the timeline whichever device it came from.
/// While frozen (pause, or nothing left to capture from) the timeline stands still, and on resuming it
/// carries on from the same frame: the frozen time does not exist on it.
/// </summary>
public sealed class CaptureTimeline
{
    private const long TicksPerSecond = 10_000_000;

    private readonly Func<long> _clock;
    private readonly Lock _gate = new();
    private long _originTicks;
    private long _frozenAtTicks;
    private volatile bool _frozen;

    /// <param name="clock">The current time in 100 ns units, on the same clock as packet stamps. Tests pass their own.</param>
    public CaptureTimeline(Func<long>? clock = null)
    {
        _clock = clock ?? PerformanceCounterTicks;
        _originTicks = _clock();
    }

    public bool IsFrozen => _frozen;

    /// <summary>The current time on the same clock as packet stamps.</summary>
    public long NowTicks => _clock();

    /// <summary>The current timeline position. Stands still while frozen.</summary>
    public long NowFrames => ToFrames(_frozen ? Volatile.Read(ref _frozenAtTicks) : _clock());

    /// <summary>Maps a packet stamp to the timeline.</summary>
    public long ToFrames(long ticks)
    {
        // 48000 / 10^7 reduced to 3 / 625, which cannot overflow within any realistic uptime.
        return (ticks - Volatile.Read(ref _originTicks)) * 3 / 625;
    }

    /// <summary>The length of a packet in clock ticks.</summary>
    public static long FramesToTicks(long frames, int sampleRate) => frames * TicksPerSecond / sampleRate;

    public void Freeze()
    {
        lock (_gate)
        {
            if (_frozen)
            {
                return;
            }

            Volatile.Write(ref _frozenAtTicks, _clock());
            _frozen = true;
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (!_frozen)
            {
                return;
            }

            Volatile.Write(ref _originTicks, _originTicks + (_clock() - _frozenAtTicks));
            _frozen = false;
        }
    }

    private static long PerformanceCounterTicks()
    {
        var counter = Stopwatch.GetTimestamp();
        var frequency = Stopwatch.Frequency;

        // Split to stay exact and avoid overflowing on a long uptime.
        return counter / frequency * TicksPerSecond + counter % frequency * TicksPerSecond / frequency;
    }
}
