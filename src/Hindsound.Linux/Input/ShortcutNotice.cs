using System.Runtime.Versioning;
using Hindsound.Linux.DBus;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Input;

/// <summary>
/// Tells the user, at the first launch only, that the desktop has no global shortcut and how to save from the keyboard
/// anyway: a silent notification through org.freedesktop.Notifications.Notify.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class ShortcutNotice
{
    private static readonly BusMethod Notify = new("org.freedesktop.Notifications", "/org/freedesktop/Notifications", "org.freedesktop.Notifications", "Notify");

    public static void ShowOnce(SessionBus bus, Action<string> log)
    {
        if (!HotkeyChoice.ClaimNotice(HotkeyChoice.NoticeMarkerPath(Environment.GetEnvironmentVariable), log))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            // Notify(s app_name, u replaces_id, s app_icon, s summary, s body, as actions, a{sv} hints, i expire_timeout).
            var sent = await bus.CallAsync(Notify, "susssasa{sv}i", Write).ConfigureAwait(false);
            if (!sent.Ok)
            {
                log("hotkeys: the notice could not be shown: " + sent.Error);
            }
        });
    }

    private static void Write(ref MessageWriter writer)
    {
        writer.WriteString("Hindsound");
        writer.WriteUInt32(0);
        writer.WriteString("");
        writer.WriteString("Hindsound");
        writer.WriteString(HotkeyChoice.NoShortcutText);
        writer.WriteArray(Array.Empty<string>());
        var hints = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("suppress-sound");
        writer.WriteVariantBool(true);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("desktop-entry");
        writer.WriteVariantString(DBusNames.AppId);
        writer.WriteDictionaryEnd(hints);
        writer.WriteInt32(-1);
    }
}
