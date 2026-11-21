namespace Hindsound.Unix.Notification;

/// <summary>
/// What the event filter of the notification's SDL window does with one event, by its numbers alone (SDL_events.h of SDL
/// 3.2), so the rule is tested on any system: the events of that window stay out of the app's queue, and a release of the
/// left button there counts a click.
/// </summary>
internal static class NotificationEvents
{
    /// <summary>SDL_EVENT_WINDOW_SHOWN, the first window event, and SDL_EVENT_MOUSE_REMOVED + 0xFF, the end of the mouse events.</summary>
    public const uint FirstWindowEvent = 0x202, LastMouseEvent = 0x4FF;

    /// <summary>SDL_EVENT_MOUSE_BUTTON_UP and SDL_BUTTON_LEFT.</summary>
    public const uint MouseButtonUp = 0x402;

    public const byte ButtonLeft = 1;

    /// <summary>False keeps the event out of the queue.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventWindow">The window it is for, 0 when it has none.</param>
    /// <param name="button">The button of a mouse button event.</param>
    /// <param name="notificationWindow">The notification's window; 0 when none is open or clicks pass through it.</param>
    /// <param name="clicks">Counts the clicks on the notification.</param>
    public static bool Keeps(uint type, uint eventWindow, byte button, uint notificationWindow, ref int clicks)
    {
        if (notificationWindow == 0 || type < FirstWindowEvent || type > LastMouseEvent || eventWindow != notificationWindow)
        {
            return true;
        }

        if (type == MouseButtonUp && button == ButtonLeft)
        {
            Interlocked.Increment(ref clicks);
        }

        return false;
    }
}
