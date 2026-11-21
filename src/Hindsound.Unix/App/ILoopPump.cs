namespace Hindsound.Unix.App;

/// <summary>
/// The wait of <see cref="UnixLoop"/> while a window is open: the window's own event queue, so that its events and the
/// loop's actions are handled on the same thread, one at a time.
/// </summary>
internal interface ILoopPump
{
    /// <summary>
    /// Called on the loop thread only. Waits until the pump has something (an event, or a wake) or the time is up, hands
    /// out the events that came, and returns. <paramref name="timeoutMs"/> is <see cref="Timeout.Infinite"/> to wait with
    /// no end. A <see cref="Wake"/> that came since the last wait returned makes this wait return at once.
    /// </summary>
    void Wait(int timeoutMs);

    /// <summary>Makes the wait in progress, or the next one, return. Safe from any thread, as often as needed.</summary>
    void Wake();
}
