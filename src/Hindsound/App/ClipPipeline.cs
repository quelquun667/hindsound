using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.App;

/// <summary>
/// Runs work one piece at a time, in order, off the caller's thread, and holds no thread while it is empty. An exception
/// in a piece is reported and the next piece still runs.
/// </summary>
internal sealed class SerialWorkQueue(Action<Exception> report)
{
    private readonly Queue<Action> _work = new();
    private readonly object _lock = new();
    private bool _running;

    /// <summary>Pieces waiting or running.</summary>
    public int Pending { get; private set; }

    public void Enqueue(Action work)
    {
        lock (_lock)
        {
            _work.Enqueue(work);
            Pending++;
            if (_running)
            {
                return;
            }

            _running = true;
        }

        _ = Task.Run(Drain);
    }

    /// <summary>Waits until everything queued so far has run. False if the time ran out.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        lock (_lock)
        {
            var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (Pending > 0)
            {
                var left = deadline - Environment.TickCount64;
                if (left <= 0 || !Monitor.Wait(_lock, TimeSpan.FromMilliseconds(left)))
                {
                    return Pending == 0;
                }
            }

            return true;
        }
    }

    private void Drain()
    {
        while (true)
        {
            Action work;
            lock (_lock)
            {
                if (_work.Count == 0)
                {
                    _running = false;
                    return;
                }

                work = _work.Dequeue();
            }

            try
            {
                work();
            }
            catch (Exception ex)
            {
                Report(ex);
            }

            lock (_lock)
            {
                Pending--;
                Monitor.PulseAll(_lock);
            }
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            report(ex);
        }
        catch (Exception)
        {
            // Nothing left to tell.
        }
    }
}

/// <summary>What the pipeline needs from the audio side. Faked in tests.</summary>
internal interface IClipBackend
{
    AudioSnapshot Snapshot(TimeSpan duration);

    ClipResult Save(AudioSnapshot snapshot, ExportOptions options, string? appName);
}

/// <summary>One press of a save command.</summary>
internal sealed record ClipRequest(TimeSpan Requested, ExportOptions Options, string? AppName, bool CopyWanted)
{
    /// <summary>Where the answer goes when it does not go to the active screen: the screen of a click on the tray menu.</summary>
    public Hindsound.Ui.ScreenInfo? Screen { get; init; }
}

internal enum ClipFailure
{
    None,

    /// <summary>There was nothing in the buffer: not a failure of anything, just nothing to save yet.</summary>
    NothingCaptured,

    /// <summary>The snapshot or the writing threw.</summary>
    Failed,

    /// <summary>The clips waiting to be written already hold too much memory: this press copied nothing.</summary>
    Busy,
}

/// <summary>How one press ended. <see cref="Result"/> is null unless the clip was written.</summary>
internal sealed record ClipOutcome(ClipRequest Request, ClipResult? Result, ClipFailure Failure);

/// <summary>The files of a clip can be moved or deleted at any time after it was written. Pure: tested without any disk.</summary>
internal static class ClipFiles
{
    /// <summary>A copy to the clipboard needs every one of them.</summary>
    public static bool AllThere(IReadOnlyList<string> paths, Func<string, bool> exists) => paths.Count > 0 && paths.All(exists);
}

/// <summary>
/// From the press to the written file. The moment is copied first: a snapshot is taken at once on its own queue, even
/// while an earlier clip is encoding, and the encoding waits in a queue with a single encoder. Nothing here can throw
/// into the caller, and an empty snapshot never reaches the encoder. Each snapshot waits in memory until its clip is
/// written, so presses that come faster than the encoder stop being copied past <see cref="MaxWaitingBytes"/>.
/// </summary>
internal sealed class ClipPipeline
{
    /// <summary>Under this time the writing goes unannounced.</summary>
    public const int SavingNoticeMs = 150;

    /// <summary>What snapshots may hold while they wait. A single clip goes through whatever its size: it is what the user asked for.</summary>
    public const long MaxWaitingBytes = 1_200_000_000;

    private const long BytesPerSecond = 48_000L * 3 * sizeof(float); // the system's two channels and the microphone

    private readonly object _waitingLock = new();
    private long _waitingBytes;
    private int _waitingPieces;
    private int _inFlight;

    private readonly IClipBackend _backend;
    private readonly Action<Action> _post;
    private readonly Action<int, Action> _after;
    private readonly Action<Exception> _report;
    private readonly SerialWorkQueue _snapshots;
    private readonly SerialWorkQueue _encoder;

    /// <param name="backend">Takes the snapshots and writes the files.</param>
    /// <param name="post">Runs an action on the thread of the message loop.</param>
    /// <param name="after">On the thread of the message loop, runs an action after a delay in milliseconds.</param>
    /// <param name="report">Where exceptions go. Any thread.</param>
    public ClipPipeline(IClipBackend backend, Action<Action> post, Action<int, Action> after, Action<Exception> report)
    {
        _backend = backend;
        _post = post;
        _after = after;
        _report = report;
        _snapshots = new SerialWorkQueue(report);
        _encoder = new SerialWorkQueue(report);
    }

    /// <summary>A write has taken over <see cref="SavingNoticeMs"/>. On the thread of the message loop.</summary>
    public Action<ClipRequest>? SavingSlowly { get; set; }

    /// <summary>A press has ended, one way or another. On the thread of the message loop.</summary>
    public Action<ClipOutcome>? Finished { get; set; }

    /// <summary>A press is taken, and <see cref="Busy"/> already counts it. On the thread that pressed, the message loop's.</summary>
    public Action? Started { get; set; }

    /// <summary>
    /// A press has yet to end. It stops counting just before <see cref="Finished"/> hears of it: the queues only go empty
    /// a moment after, so whoever hears of the last clip must not ask them.
    /// </summary>
    public bool Busy => Volatile.Read(ref _inFlight) > 0;

    /// <summary>Takes the press. Returns at once.</summary>
    public void Press(ClipRequest request)
    {
        Interlocked.Increment(ref _inFlight);
        _snapshots.Enqueue(() => TakeSnapshot(request));
        Started?.Invoke();
    }

    /// <summary>Lets queued clips finish, in that time for the two queues together. False if the time ran out.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        return _snapshots.WaitIdle(timeout)
            && _encoder.WaitIdle(TimeSpan.FromMilliseconds(Math.Max(0, deadline - Environment.TickCount64)));
    }

    /// <summary>What a snapshot of this length holds at most, known before anything is copied.</summary>
    public static long EstimatedBytes(TimeSpan requested) => (long)(BytesPerSecond * requested.TotalSeconds);

    private void TakeSnapshot(ClipRequest request)
    {
        var bytes = EstimatedBytes(request.Requested);
        if (!TryHold(bytes))
        {
            Finish(new ClipOutcome(request, null, ClipFailure.Busy));
            return;
        }

        AudioSnapshot snapshot;
        try
        {
            snapshot = _backend.Snapshot(request.Requested);
        }
        catch (Exception ex)
        {
            LetGo(bytes);
            _report(ex);
            Finish(new ClipOutcome(request, null, ClipFailure.Failed));
            return;
        }

        if (snapshot.System.Length == 0 && snapshot.Mic.Length == 0)
        {
            LetGo(bytes);
            Finish(new ClipOutcome(request, null, ClipFailure.NothingCaptured));
            return;
        }

        _encoder.Enqueue(() => Encode(request, snapshot, bytes));
    }

    private bool TryHold(long bytes)
    {
        lock (_waitingLock)
        {
            if (_waitingPieces > 0 && _waitingBytes + bytes > MaxWaitingBytes)
            {
                return false;
            }

            _waitingPieces++;
            _waitingBytes += bytes;
            return true;
        }
    }

    private void LetGo(long bytes)
    {
        lock (_waitingLock)
        {
            _waitingPieces--;
            _waitingBytes -= bytes;
        }
    }

    private void Encode(ClipRequest request, AudioSnapshot snapshot, long bytes)
    {
        var done = new StrongBox();
        ClipOutcome outcome;
        try
        {
            _post(() => _after(SavingNoticeMs, () =>
            {
                if (!done.Value)
                {
                    // What is being written, not what was asked for: the memory may hold less than the request.
                    var kept = snapshot.Duration;
                    SavingSlowly?.Invoke(kept < request.Requested ? request with { Requested = kept } : request);
                }
            }));

            var result = _backend.Save(snapshot, request.Options, request.AppName);
            outcome = new ClipOutcome(request, result, ClipFailure.None);
        }
        catch (Exception ex)
        {
            _report(ex);
            outcome = new ClipOutcome(request, null, ClipFailure.Failed);
        }

        done.Value = true;

        // Before the news of the end: whoever hears it can press again at once.
        LetGo(bytes);
        Finish(outcome);
    }

    private void Finish(ClipOutcome outcome) => _post(() =>
    {
        Interlocked.Decrement(ref _inFlight);
        Finished?.Invoke(outcome);
    });

    private sealed class StrongBox
    {
        private volatile bool _value;

        public bool Value
        {
            get => _value;
            set => _value = value;
        }
    }
}
