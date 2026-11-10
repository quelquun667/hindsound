using Hindsound.App;

namespace Hindsound.Linux.App;

/// <summary>
/// The thread of the app on Linux: a queue of actions and a list of timers, run one at a time on the thread that calls
/// <see cref="Run"/>. Every other thread (the sound server, D-Bus, the shortcuts) comes back through <see cref="Post"/>.
/// As with the Windows message loop, posted actions go before due timers, and <see cref="Quit"/> lets the actions already
/// posted run before <see cref="Run"/> returns; the timers left are dropped.
/// </summary>
/// <remarks>An exception thrown by an action leaves <see cref="Run"/>, as one thrown in a window procedure ends the app on Windows.</remarks>
internal sealed class LinuxLoop : IAppLoop
{
    private readonly object _gate = new();
    private readonly Queue<Action> _actions = new();
    private readonly PriorityQueue<Action, (long Due, long Order)> _timers = new();
    private readonly Func<long> _nowMs;
    private long _order;
    private bool _quit;
    private bool _disposed;
    private Thread? _running;

    /// <param name="nowMs">The clock of the timers, in milliseconds. A monotonic count by default.</param>
    public LinuxLoop(Func<long>? nowMs = null) => _nowMs = nowMs ?? (static () => Environment.TickCount64);

    public Action? CloseAsked { get; set; }

    public Action? ScaleMayHaveChanged { get; set; }

    /// <summary>Whether the calling thread is the one running the loop now.</summary>
    public bool OnLoopThread
    {
        get
        {
            lock (_gate)
            {
                return _running == Thread.CurrentThread;
            }
        }
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _actions.Enqueue(action);
            Monitor.PulseAll(_gate);
        }
    }

    public void After(int milliseconds, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // The order breaks ties: two timers due at the same moment run in the order they were set.
            _timers.Enqueue(action, (_nowMs() + Math.Max(0, milliseconds), _order++));
            Monitor.PulseAll(_gate);
        }
    }

    public void Run()
    {
        lock (_gate)
        {
            _running = Thread.CurrentThread;
        }

        try
        {
            while (Next() is { } action)
            {
                action();
            }
        }
        finally
        {
            lock (_gate)
            {
                _running = null;
            }
        }
    }

    public void Quit()
    {
        lock (_gate)
        {
            _quit = true;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>The Linux app has no window of its own to be in front.</summary>
    public bool InFront() => false;

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _actions.Clear();
            _timers.Clear();
            Monitor.PulseAll(_gate);
        }
    }

    // The next thing to run, waiting for it; null once the loop is asked to end and nothing posted is left.
    private Action? Next()
    {
        lock (_gate)
        {
            while (true)
            {
                if (_actions.Count > 0)
                {
                    return _actions.Dequeue();
                }

                if (_quit || _disposed)
                {
                    _quit = false;
                    _timers.Clear();
                    return null;
                }

                var wait = Timeout.Infinite;
                if (_timers.TryPeek(out var timer, out var key))
                {
                    var left = key.Due - _nowMs();
                    if (left <= 0)
                    {
                        _timers.Dequeue();
                        return timer;
                    }

                    wait = (int)Math.Min(left, int.MaxValue);
                }

                Monitor.Wait(_gate, wait);
            }
        }
    }
}
