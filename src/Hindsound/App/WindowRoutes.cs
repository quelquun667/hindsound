using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.App;

/// <summary>Every gesture that can lead to the window, or that used to.</summary>
internal enum WindowGesture
{
    /// <summary>A click on the icon, Open Hindsound in its menu, or a second launch.</summary>
    OpenApp,

    /// <summary>Settings in the menu of the icon.</summary>
    SettingsMenu,

    /// <summary>The app just started for the first time.</summary>
    FirstRunCard,

    /// <summary>A click on a notification that says "Clip saved".</summary>
    ClipSavedClick,

    /// <summary>A click on any other notification about a clip.</summary>
    OtherClipClick,

    /// <summary>A click on Capture on, Microphone lost or another word about the state of the capture.</summary>
    StatusClick,

    /// <summary>A click on Capture on when it says the save hotkey does not work.</summary>
    HotkeyRefusedClick,

    /// <summary>A click on a notification about the settings file.</summary>
    SettingsProblemClick,

    /// <summary>A click on the notification that says Start with Windows could not change: that row is on General.</summary>
    StartupProblemClick,

    /// <summary>A click on a notification about a new version.</summary>
    UpdateClick,

    /// <summary>"Update to X..." in the menu of the icon.</summary>
    UpdateMenu,

    /// <summary>Library in the menu of the icon.</summary>
    LibraryMenu,
}

/// <summary>What a gesture ends up doing, on top of bringing the window up at <see cref="WindowRoute.Section"/>.</summary>
internal enum WindowAction
{
    /// <summary>Nothing more.</summary>
    Show,

    /// <summary>Shows General, scrolled to its Updates section.</summary>
    ShowUpdates,

    /// <summary>Shows the card that confirms an update.</summary>
    ShowUpdateCard,
}

/// <param name="Action">What to do.</param>
/// <param name="Section">Where the window lands.</param>
internal readonly record struct WindowRoute(WindowAction Action, Section? Section = null);

/// <summary>
/// Where each gesture leads, with the window closed and with the window open. Pure: tested without any window.
/// </summary>
internal static class WindowRoutes
{
    /// <param name="gesture">What the user did.</param>
    /// <param name="openOn">The view of the window; null when it is closed.</param>
    /// <param name="lastSettingsPage">The last page of the settings seen since the app started. The window forgets it: it is destroyed when it closes.</param>
    public static WindowRoute Of(WindowGesture gesture, Section? openOn, Section lastSettingsPage) => gesture switch
    {
        // An open window only comes to the front: the user was looking at something.
        WindowGesture.OpenApp => new(WindowAction.Show, openOn ?? Section.Now),
        WindowGesture.FirstRunCard => new(WindowAction.Show, openOn ?? Section.FirstRun),
        WindowGesture.SettingsMenu or WindowGesture.SettingsProblemClick => LandOn(lastSettingsPage, openOn),
        WindowGesture.StatusClick => LandOn(Section.Now, openOn),
        WindowGesture.HotkeyRefusedClick => LandOn(Section.Hotkeys, openOn),
        WindowGesture.StartupProblemClick => LandOn(Section.General, openOn),

        // Not LandOn: the two of them have their own rules, on the screen, about the cards.
        WindowGesture.UpdateClick => new(WindowAction.ShowUpdates, openOn ?? Section.Now),
        WindowGesture.UpdateMenu => new(WindowAction.ShowUpdateCard, openOn ?? Section.Now),

        // The clip lines, and the line of the menu.
        _ => LandOn(Section.Library, openOn),
    };

    /// <summary>
    /// What the click on a notification is. With nothing showing, which a click on a window that is shown should not meet, it
    /// is a clip line: the Library opens with no clip chosen.
    /// </summary>
    public static WindowGesture ClickOn(NotificationContent? content)
    {
        if (content is null)
        {
            return WindowGesture.OtherClipClick;
        }

        // Before the settings: a failed update is a Problem, which opens the settings too, but at its own section.
        if (content.ClickShowsUpdates)
        {
            return WindowGesture.UpdateClick;
        }

        if (content.ClickOpensSettings)
        {
            return content.Kind switch
            {
                NotificationKind.CaptureOn => WindowGesture.HotkeyRefusedClick,
                NotificationKind.StartupNotChanged => WindowGesture.StartupProblemClick,
                _ => WindowGesture.SettingsProblemClick,
            };
        }

        return content.Kind switch
        {
            NotificationKind.Saved or NotificationKind.Partial or NotificationKind.Silent or NotificationKind.CopyFailed
                or NotificationKind.NotCopied or NotificationKind.WavFallback => WindowGesture.ClipSavedClick,
            NotificationKind.CaptureOn or NotificationKind.Paused or NotificationKind.NoDevice or NotificationKind.DeviceChanged
                or NotificationKind.MicLost or NotificationKind.MicBack => WindowGesture.StatusClick,
            _ => WindowGesture.OtherClipClick,
        };
    }

    // A card is never left by force: the first-launch one would lose its steps and hold the updater back for nothing, and
    // the update one is a question the user is being asked.
    private static WindowRoute LandOn(Section target, Section? openOn) =>
        new(WindowAction.Show, openOn is Section.FirstRun or Section.UpdateCard ? openOn : target);
}
