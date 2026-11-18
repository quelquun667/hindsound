namespace Hindsound.Linux.Audio;

/// <summary>
/// When the frames a capture reads were captured, on the clock of the preview's silence. The server stamps nothing the app can
/// compare with that clock, and the stream's queue cannot say how late the reader is: when the capture's thread is held, the
/// audio waits in the socket or in the server, not in the queue. But a frame is never read before it was captured, so for each
/// packet, the time it was read less the time its frames last (from the first frame of the stream to its end) is the moment
/// the stream started, or later. The smallest of these over the last few seconds is the anchor: frame N was captured at the
/// anchor plus N frames' time. A reader that is late, then reads a burst to catch up, does not move it.
/// <para>
/// The anchor is the smallest over a window of about <see cref="WindowMs"/> only, so that a drift of a few parts per million
/// between the server's clock and this one, or a latency that grew for good, is followed within that time. A stream whose
/// position goes back to an earlier frame was started again: what was learnt before is forgotten.
/// </para>
/// Nothing here reads a clock or touches the server: the capture passes the time and the frames, and a test its own.
/// </summary>
internal sealed class PulseCaptureTimeline
{
    /// <summary>How long a reading is kept as a candidate for the anchor, about.</summary>
    public const long WindowMs = 10_000;

    private const long BucketMs = 1_000;
    private const int Buckets = (int)(WindowMs / BucketMs) + 1;

    private readonly double _msPerFrame;

    // The smallest anchor read in each second of the window, by the second it was read in.
    private readonly long[] _bucketOf = new long[Buckets];
    private readonly double[] _anchorIn = new double[Buckets];
    private long _nextFrame = -1;

    /// <param name="framesPerSecond">The stream's rate.</param>
    public PulseCaptureTimeline(int framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);
        _msPerFrame = 1000.0 / framesPerSecond;
        Forget();
    }

    /// <summary>
    /// When the frames from <paramref name="firstFrame"/> to <paramref name="endFrame"/> (excluded) were captured, read at
    /// <paramref name="readMs"/>: the capture time of the first frame, and of the end of the last, in whole milliseconds.
    /// </summary>
    public (long FromMs, long ToMs) Place(long readMs, long firstFrame, long endFrame)
    {
        endFrame = Math.Max(endFrame, firstFrame);
        if (firstFrame < _nextFrame)
        {
            // The position went back: a new stream, whose frames count from another start.
            Forget();
        }

        _nextFrame = endFrame;
        var bucket = FloorDiv(readMs, BucketMs);
        var slot = (int)(((bucket % Buckets) + Buckets) % Buckets);
        var anchor = readMs - (endFrame * _msPerFrame);
        if (_bucketOf[slot] != bucket)
        {
            // A second that left the window gives its place to this one.
            _bucketOf[slot] = bucket;
            _anchorIn[slot] = anchor;
        }
        else
        {
            _anchorIn[slot] = Math.Min(_anchorIn[slot], anchor);
        }

        var earliest = anchor;
        for (var i = 0; i < Buckets; i++)
        {
            // Only the seconds of the window: an older reading, or one from a clock that went back, no longer counts.
            if (_bucketOf[i] > bucket - Buckets && _bucketOf[i] <= bucket)
            {
                earliest = Math.Min(earliest, _anchorIn[i]);
            }
        }

        return ((long)Math.Floor(earliest + (firstFrame * _msPerFrame)), (long)Math.Floor(earliest + (endFrame * _msPerFrame)));
    }

    private void Forget()
    {
        Array.Fill(_bucketOf, long.MinValue);
        Array.Fill(_anchorIn, double.MaxValue);
        _nextFrame = -1;
    }

    private static long FloorDiv(long value, long by) => value >= 0 ? value / by : -((-value + by - 1) / by);
}
