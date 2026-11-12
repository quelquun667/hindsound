using System.Runtime.Versioning;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Desktop;
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
        var marker = HotkeyChoice.NoticeMarkerPath(Environment.GetEnvironmentVariable);
        _ = Task.Run(() => HotkeyChoice.ShowNoticeOnceAsync(marker, () => SendAsync(bus), log));
    }

    /// <summary>Sends the notice itself, apart from <see cref="ShowOnce"/> so that a test can read back what a server receives.</summary>
    internal static Task<BusAnswer<bool>> SendAsync(SessionBus bus)
    {
        // Notify(s app_name, u replaces_id, s app_icon, s summary, s body, as actions, a{sv} hints, i expire_timeout).
        return bus.CallAsync(Notify, "susssasa{sv}i", Write);
    }

    private static void Write(ref MessageWriter writer) => FreedesktopNotification.Write(ref writer, ShortcutNoticeRequest.Build());
}
