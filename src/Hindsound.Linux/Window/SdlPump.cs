using System.Runtime.Versioning;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Window;

/// <summary>The window's handler of one SDL event, on the loop thread. What the event points to is valid only during the call.</summary>
internal delegate void SdlEventHandler(in SdlNative.Event e);

/// <summary>
/// The wait of <see cref="LinuxLoop"/> while an SDL window is open: SDL's event queue, so that the window's events and the
/// loop's actions take turns on the loop thread. A wake is an event of a type of its own, pushed from any thread; at most
/// one is in the queue at a time, however often the loop is woken.
/// </summary>
/// <remarks>Made after SDL's events (or video) are initialised, and used while they are, on the thread that initialised video.</remarks>
[SupportedOSPlatform("linux")]
internal sealed unsafe class SdlPump : ILoopPump
{
    private readonly uint _wakeType;

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

        // Everything already queued goes out in one turn, so a burst of mouse motion does not cost a turn of the loop each.
        do
        {
            Dispatch(ref e);
        }
        while (SdlNative.PollEvent(&e));
    }

    public void Wake()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 1)
        {
            return;
        }

        var e = default(SdlNative.Event);
        e.User.Type = _wakeType;
        if (!SdlNative.PushEvent(&e))
        {
            // Not queued (the queue is full, or SDL is shutting down): the next wake tries again.
            Volatile.Write(ref _wakePending, 0);
        }
    }

    private void Dispatch(ref SdlNative.Event e)
    {
        if (e.Type == _wakeType)
        {
            // Cleared before the loop looks at its queue again: a post made after this pushes a new wake.
            Volatile.Write(ref _wakePending, 0);
            return;
        }

        Handler?.Invoke(in e);
    }
}
