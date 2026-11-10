using Hindsound.Linux.DBus;
using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Input;

/// <summary>
/// The notice of a desktop with no global shortcut, as a request: written by the same code as every other notification of the
/// app, so it carries the same hints. Apart from <see cref="ShortcutNotice"/> so that what it says runs in tests on any system.
/// </summary>
internal static class ShortcutNoticeRequest
{
    /// <summary>Normal urgency, written out, silent, no action.</summary>
    public static NotificationRequest Build() => new(
        NotificationRequest.AppNameText,
        ReplacesId: 0,
        Icon: "",
        Summary: "Hindsound",
        Body: HotkeyChoice.NoShortcutText,
        Actions: [],
        NotificationRequest.NormalUrgency,
        SuppressSound: true,
        DBusNames.AppId,
        NotificationRequest.ServerTimeout);
}
