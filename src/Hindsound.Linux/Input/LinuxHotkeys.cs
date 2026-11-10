using System.Runtime.Versioning;
using Hindsound.Input;
using Hindsound.Linux.DBus;

namespace Hindsound.Linux.Input;

/// <summary>
/// The global shortcuts of the Linux app: XGrabKey in an X11 session, the GlobalShortcuts portal under Wayland (and in an
/// X11 session whose display cannot be opened). Where neither works, <c>hindsound --save</c> bound to a key in the
/// desktop's own keyboard settings does the same, and the user is told so once.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxHotkeys(Action<string> log) : HotkeyChooser(
    log,
    HotkeyChoice.IsWayland(Environment.GetEnvironmentVariable),
    HotkeyChoice.HasXDisplay(Environment.GetEnvironmentVariable),
    () => X11Hotkeys.TryOpen(log) is { } x11 ? new KeyByKeyHotkeys(x11) : null,
    async () => await PortalHotkeys.OpenAsync(SessionBus.Shared, log).ConfigureAwait(false),
    () => ShortcutNotice.ShowOnce(SessionBus.Shared, log));
