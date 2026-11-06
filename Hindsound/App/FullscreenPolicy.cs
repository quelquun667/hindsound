using Hindsound.Settings;

namespace Hindsound.App;

/// <summary>The values of SHQueryUserNotificationState (QUERY_USER_NOTIFICATION_STATE).</summary>
public enum UserNotificationState
{
    /// <summary>The call failed.</summary>
    Unknown = 0,

    /// <summary>Screen saver, locked machine, or a full-screen app that stopped responding.</summary>
    NotPresent = 1,

    /// <summary>A full-screen app that is not Direct3D exclusive. Borderless games usually land here.</summary>
    Busy = 2,

    /// <summary>A Direct3D game in exclusive full screen.</summary>
    RunningD3dFullScreen = 3,

    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7,
}

/// <summary>How to tell the user, right now.</summary>
/// <param name="ShowOverlay">Draw the notification.</param>
/// <param name="ClickThrough">Clicks pass through it.</param>
/// <param name="PlaySound">Play the two tones.</param>
/// <param name="CatchUpLater">The notification waits for the return to the desktop.</param>
internal readonly record struct Presentation(bool ShowOverlay, bool ClickThrough, bool PlaySound, bool CatchUpLater);

/// <summary>
/// PLAN sections 3 quater and 4.8: a window over an exclusive full-screen game could throw the game out of full screen.
/// So there, a sound alone, and the notification on the return to the desktop. Pure: tested without any window.
/// </summary>
internal static class FullscreenPolicy
{
    /// <summary>
    /// The decision once a clip is saved. The sound only ever confirms a saved clip, and never one that Ctrl+V will not
    /// paste: with the copy failed the silence is the warning, whatever the sound setting says, and the notification follows.
    /// </summary>
    public static Presentation ForClip(UserNotificationState state, bool foregroundCoversScreen, GeneralSettings general, bool saved, bool important, bool copyFailed = false)
    {
        var overlayAllowed = OverlayAllowed(state, foregroundCoversScreen, general.FullscreenGames);
        var wanted = general.Notifications || important;
        var showOverlay = overlayAllowed && wanted;
        var sound = saved && !copyFailed && general.ConfirmSound switch
        {
            "never" => false,
            "always" => true,
            _ => !showOverlay,
        };
        return new Presentation(showOverlay, showOverlay && ClickThrough(state, foregroundCoversScreen), sound, !overlayAllowed && wanted);
    }

    /// <summary>The decision for a status (capture on, paused, device changed): silent, and dropped when no overlay can show.</summary>
    public static Presentation ForStatus(UserNotificationState state, bool foregroundCoversScreen, GeneralSettings general, bool important)
    {
        var showOverlay = OverlayAllowed(state, foregroundCoversScreen, general.FullscreenGames) && (general.Notifications || important);
        return new Presentation(showOverlay, showOverlay && ClickThrough(state, foregroundCoversScreen), false, false);
    }

    /// <summary>Has the user come back, so a clip kept waiting can be shown?</summary>
    public static bool BackOnDesktop(UserNotificationState state, bool foregroundCoversScreen) =>
        !foregroundCoversScreen && !IsFullScreenState(state) && !NoOneThere(state);

    private static bool OverlayAllowed(UserNotificationState state, bool foregroundCoversScreen, string fullscreenGames)
    {
        // Exclusive full screen, slides on show, or nobody at the screen.
        if (state is UserNotificationState.RunningD3dFullScreen or UserNotificationState.PresentationMode || NoOneThere(state))
        {
            return false;
        }

        var soundOnly = string.Equals(fullscreenGames, "soundOnly", StringComparison.OrdinalIgnoreCase);
        return !(soundOnly && (foregroundCoversScreen || IsFullScreenState(state)));
    }

    // On the plain desktop the notification can be clicked. Anywhere else it must not catch a click meant for the game.
    private static bool ClickThrough(UserNotificationState state, bool foregroundCoversScreen) =>
        foregroundCoversScreen || state is not (UserNotificationState.AcceptsNotifications or UserNotificationState.QuietTime);

    private static bool IsFullScreenState(UserNotificationState state) =>
        state is UserNotificationState.Busy or UserNotificationState.RunningD3dFullScreen or UserNotificationState.PresentationMode;

    private static bool NoOneThere(UserNotificationState state) => state == UserNotificationState.NotPresent;
}
