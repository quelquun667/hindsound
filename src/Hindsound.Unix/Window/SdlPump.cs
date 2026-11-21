using System.Runtime.Versioning;
using Hindsound.Unix.App;

namespace Hindsound.Unix.Window;

/// <summary>The window's handler of one SDL event, on the loop thread. What the event points to is valid only during the call.</summary>
internal delegate void SdlEventHandler(in SdlNative.Event e);

/// <summary>
/// The wait of <see cref="UnixLoop"/> while an SDL window is open: SDL's event queue, so that the window's events and the
/// loop's actions take turns on the loop thread. A wake is an event of a type of its own, pushed from any thread; at most
/// one is in the queue at a time, however often the loop is woken.
/// </summary>
/// <remarks>Made after SDL's events (or video) are initialised, and used while they are, on the thread that initialised video.</remarks>
[SupportedOSPlatform("linux")]
internal sealed unsafe class SdlPump : ILoopPump
{
    /// <summary>The most events one wait hands out; the rest are for the next wait, which returns at once.</summary>
    internal const int MaxBurst = 64;

    private readonly uint _wakeType;

    // The push of a wake, for a test; null pushes to SDL's queue.
    private readonly Func<uint, bool>? _push;

    // A push from another thread and the stop of SDL's events never overlap: SDL frees its queue's lock when its events stop,
    // and a push waiting on that lock would then use it freed.
    private readonly Lock _gate = new();
    private bool _stopped;

    // 1 while a wake event is in the queue: no second one is pushed until the wait takes it.
    private int _wakePending;

    /// <summary>Registers the wake's event type. Throws <see cref="InvalidOperationException"/> if SDL has none left.</summary>
    public SdlPump()
    {
        _wakeType = SdlNative.RegisterEvents(1);
        if (_wakeType == 0)
        {
            throw new InvalidOperationException("SDL gave no event type for the wake of the loop: " + SdlNative.Error());
        }
    }

    /// <summary>A pump whose wakes go to <paramref name="push"/> instead of SDL, for a test.</summary>
    internal SdlPump(uint wakeType, Func<uint, bool> push)
    {
        _wakeType = wakeType;
        _push = push;
    }

    /// <summary>The event type of the wake, never handed to <see cref="Handler"/>.</summary>
    public uint WakeType => _wakeType;

    /// <summary>Where the window gets its events: every one but the wake, on the loop thread. Null drops them.</summary>
    public SdlEventHandler? Handler { get; set; }

    public void Wait(int timeoutMs)
    {
        SdlNative.Event e;
        if (!SdlNative.WaitEventTimeout(&e, timeoutMs))
        {
            return;
        }

        // What is already queued goes out in one turn, so a burst of mouse motion does not cost a turn of the loop each. The
        // turn ends at the wake, which says the loop has posted work to run, or after a burst's worth: a flood of events (a
        // slider dragged with a fast mouse) does not keep the posted work and the timers waiting until the queue is empty. The
        // next event is taken out of the queue only if this wait hands it out: one polled past the burst would be lost.
        for (var handed = 1; ; handed++)
        {
            if (Dispatch(ref e) || handed == MaxBurst || !SdlNative.PollEvent(&e))
            {
                return;
            }
        }
    }

    public void Wake()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 1)
        {
            return;
        }

        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            if (!Push())
            {
                // Not queued (the queue is full, or SDL is shutting down): the next wake tries again.
                Volatile.Write(ref _wakePending, 0);
            }
        }
    }

    /// <summary>
    /// No wake is pushed after this returns, and none is still being pushed: called before SDL's events stop. A wake from
    /// another thread that is being pushed is waited for.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
        }
    }

    private bool Push()
    {
        if (_push is not null)
        {
            return _push(_wakeType);
        }

        var e = default(SdlNative.Event);
        e.User.Type = _wakeType;
        return SdlNative.PushEvent(&e);
    }

    // True for the wake, which no handler sees.
    private bool Dispatch(ref SdlNative.Event e)
    {
        if (e.Type == _wakeType)
        {
            // Cleared before the loop looks at its queue again: a post made after this pushes a new wake.
            Volatile.Write(ref _wakePending, 0);
            return true;
        }

        Handler?.Invoke(in e);
        return false;
    }
}
