using System.Runtime.Versioning;
using Hindsound.Mac.Notification;

namespace Hindsound.Mac.App;

/// <summary>
/// Makes Hindsound the active app when a window of it opens: a click on the notification makes it the active app
/// anyway (no message to the notification's window prevents it), and the window the click opens comes in front, as on Windows.
/// The window brings the app forward itself, whatever opened it.
/// SDL's raise sends activateIgnoringOtherApps:, deprecated since macOS 14, where activation is a request the system weighs;
/// <c>activate</c> is the message of macOS 14 for it. The CI proves the result (<c>focus.library_front</c>).
/// </summary>
internal static class MacActivation
{
    /// <summary>NSApplication's activation of macOS 14 and later.</summary>
    internal const string Cooperative = "activate";

    /// <summary>NSApplication's activation before macOS 14, with YES.</summary>
    internal const string Older = "activateIgnoringOtherApps:";

    /// <summary>The message to send: <see cref="Cooperative"/> when the app answers it (respondsToSelector:), the older one otherwise.</summary>
    internal static string MessageFor(Func<string, bool> answers) => answers(Cooperative) ? Cooperative : Older;

    /// <summary>Asks for the app to be the active one; the message sent, or "none" when there is no NSApp yet.</summary>
    [SupportedOSPlatform("macos")]
    public static string Activate()
    {
        var application = MacNotificationHook.Messages.Send(MacNotificationHook.Messages.Class("NSApplication"), "sharedApplication");
        if (application == 0)
        {
            return "none";
        }

        var message = MessageFor(selector => MacNotificationHook.Messages.RespondsTo(application, selector));
        if (message == Cooperative)
        {
            MacNotificationHook.Messages.Send(application, message);
        }
        else
        {
            MacNotificationHook.Messages.SendBool(application, message, true);
        }

        return message;
    }
}
