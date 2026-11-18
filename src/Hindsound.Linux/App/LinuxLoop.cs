using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Linux.App;

/// <summary>
/// The thread of the app on Linux: a queue of actions and a list of timers, run one at a time on the thread that calls
/// <see cref="Run"/>. Every other thread (the sound server, D-Bus, the shortcuts) comes back through <see cref="Post"/>.
/// As with the Windows message loop, posted actions go before due timers, and <see cref="Quit"/> lets the actions already
/// posted run before <see cref="Run"/> returns; the timers left are dropped.
/// While a window is open, its <see cref="Pump"/> does the waiting, so the loop thread also handles the window's events:
/// <see cref="Run"/> waits in <see cref="ILoopPump.Wait"/> instead of on its own lock, and <see cref="Post"/>,
/// <see cref="After"/>, <see cref="Quit"/> and <see cref="Dispose"/> wake it from any thread. Every guarantee above holds
/// with or without a pump.
/// </summary>
/// <remarks>An exception thrown by an action, a timer or a window event the pump hands out is reported through
/// <see cref="UiErrors"/> and the loop goes on, as the Windows loop does: one failed piece of work must not end the app,
/// and the buffer with it.</remarks>
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
    private ILoopPump? _pump;

    /// <param name="nowMs">The clock of the timers, in milliseconds. A monotonic count by default.</param>
    public LinuxLoop(Func<long>? nowMs = null) => _nowMs = nowMs ?? (static () => Environment.TickCount64);

    public Action? CloseAsked { get; set; }

    public Action? ScaleMayHaveChanged { get; set; }

    /// <summary>
    /// What the loop waits through, set while a window is open; null, it waits on its own. It can be set or cleared from any
    /// thread, even while the loop waits: the pump it replaces is woken, and the loop goes on with the new one.
    /// </summary>
    public ILoopPump? Pump
    {
        get
        {
            lock (_gate)
            {
                return _pump;
            }
        }

        set
        {
            ILoopPump? old;
            lock (_gate)
            {
                old = _pump;
                _pump = value;
                Monitor.PulseAll(_gate);
            }

            if (old is not null && !ReferenceEquals(old, value))
            {
                old.Wake();
            }
        }
    }

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
        ILoopPump? pump;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _actions.Enqueue(action);
            Monitor.PulseAll(_gate);
            pump = _pump;
        }

        pump?.Wake();
    }

    public void After(int milliseconds, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ILoopPump? pump;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // The order breaks ties: two timers due at the same moment run in the order they were set.
            _timers.Enqueue(action, (_nowMs() + Math.Max(0, milliseconds), _order++));
            Monitor.PulseAll(_gate);
            pump = _pump;
        }

        pump?.Wake();
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
                RunGuarded(action);
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

    /// <summary>
    /// One turn of the loop from inside an action of the loop thread that waits for something outside (a dialog of the
    /// desktop): runs the actions posted so far, then the timers due, then waits once for at most <paramref name="timeoutMs"/>
    /// in the pump, which hands out the window's events. What those post or set waits for the next turn. A quit asked meanwhile
    /// is left to <see cref="Run"/>.
    /// </summary>
    public void RunOnce(int timeoutMs)
    {
        List<Action> ready = [];
        ILoopPump? pump;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Only what is there now: an action that posts itself again does not keep this turn from ending.
            ready.AddRange(_actions);
            _actions.Clear();
            var now = _nowMs();
            while (_timers.TryPeek(out var timer, out var key) && key.Due <= now)
            {
                _timers.Dequeue();
                ready.Add(timer);
            }

            pump = _pump;
        }

        foreach (var action in ready)
        {
            RunGuarded(action);
        }

        if (ready.Count > 0)
        {
            timeoutMs = 0;
        }

        if (pump is null)
        {
            lock (_gate)
            {
                Monitor.Wait(_gate, timeoutMs);
            }

            return;
        }

        WaitGuarded(pump, timeoutMs);
    }

    public void Quit()
    {
        ILoopPump? pump;
        lock (_gate)
        {
            _quit = true;
            Monitor.PulseAll(_gate);
            pump = _pump;
        }

        pump?.Wake();
    }

    /// <summary>
    /// Never: on Windows this is the hidden window a closed menu of the icon leaves active, so the last click on the icon
    /// still speaks for the user (TrayClick.StandingIn). The menu of the Linux icon is the desktop's and leaves nothing of
    /// the app active; the settings window having the focus is not that case, so it does not count either.
    /// </summary>
    public bool InFront() => false;

    public void Dispose()
    {
        ILoopPump? pump;
        lock (_gate)
        {
            _disposed = true;
            _actions.Clear();
            _timers.Clear();
            Monitor.PulseAll(_gate);
            pump = _pump;
        }

        pump?.Wake();
    }

    // The next thing to run, waiting for it; null once the loop is asked to end and nothing posted is left.
    private Action? Next()
    {
        while (true)
        {
            ILoopPump? pump;
            int wait;
            lock (_gate)
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

                wait = Timeout.Infinite;
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

                pump = _pump;
                if (pump is null)
                {
                    Monitor.Wait(_gate, wait);
                    continue;
                }
            }

            // Outside the lock: the pump hands the window's events out here, on the loop thread, and they may post. A wake
            // that comes between the lock and this wait is not lost, since a pump keeps it for its next wait.
            WaitGuarded(pump, wait);
        }
    }

    private static void WaitGuarded(ILoopPump pump, int timeoutMs)
    {
        try
        {
            pump.Wait(timeoutMs);
        }
        catch (Exception ex)
        {
            // A handler of the window threw: the events it left stay in the queue of the pump, for the next wait.
            UiErrors.Report(ex);
        }
    }

    // One failed piece of work must not take the rest of the queue with it.
    private static void RunGuarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            UiErrors.Report(ex);
        }
    }
}
