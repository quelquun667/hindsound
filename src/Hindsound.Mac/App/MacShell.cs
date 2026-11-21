using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Mac.Audio.Devices;
using Hindsound.Mac.Desktop;
using Hindsound.Mac.Notification;
using Hindsound.Mac.Tray;
using Hindsound.Mac.Window;
using Hindsound.Ui;
using Hindsound.Unix.App;
using Hindsound.Unix.Library;
using Hindsound.Unix.Notification;
using Hindsound.Unix.Window;

namespace Hindsound.Mac.App;

/// <summary>
/// The running app on macOS, as far as it goes: the loop on the run loop of the main thread, the settings window on SDL
/// with the text of the Mac, its probe, and the reading of a clip. Every other part throws
/// <see cref="NotSupportedException"/> with the step that brings it: nothing here answers as if it worked.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacShell(Action<string> log) : IAppShell
{
    private UnixLoop? _loop;
    private MacRunLoopPump? _idle;

    /// <summary>
    /// The loop of the app, on the calling thread, which must be the main thread: between windows it waits in the run loop
    /// of that thread (<see cref="MacRunLoopPump"/>), so the icon of the menu bar and the Carbon shortcuts get their events
    /// with no window open; while a window is open, SDL's pump takes over, and SDL turns the same run loop.
    /// </summary>
    public IAppLoop NewLoop()
    {
        _idle = new MacRunLoopPump();
        _loop = new UnixLoop(nowMs: null, idle: _idle);

        // Disposing the loop frees the pump after it, which nothing then waits on.
        return new MacAppLoop(_loop, _idle);
    }

    /// <summary>The notification drawn by the app, in an SDL window that takes no focus; <paramref name="offscreen"/> shows nothing and keeps the times.</summary>
    public INotificationView NewNotification(IAppLoop loop, bool offscreen) =>
        new NotificationPresenter(loop, offscreen ? null : static () => new SdlNotificationSurface(new MacNotificationHook()), MacText.Create, log);

    public ITrayIcon NewTray(IAppLoop loop) =>
        MacStatusItem.TryCreate(loop, log) ?? throw new NotSupportedException("the icon of the menu bar needs the main thread");

    public ISessionEnd SessionEnd(IAppLoop loop) => new MacSessionEnd(loop, log);

    public IClipboardFiles Clipboard(IAppLoop loop) => new MacPasteboard(log);

    public ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request) =>
        HasWindow && _loop is { } loop ? UnixSettingsWindow.Open(request, loop, MacText.Create) : null;

    public IFolderShell NewFolderShell() => new MacOpen(log);

    public IPreviewPlayer NewPreviewPlayer() => throw Later("the listening", "M3");

    public IRecycleBin NewRecycleBin(Action<string> journal) => new MacTrash(journal);

    public IClipDecoder NewClipDecoder() => new UnixClipDecoder();

    public IAudioChoices AudioChoices { get; } = new MacAudioChoices(log);

    public ISettingsProbe Probe => HasWindow ? new UnixSettingsProbe() : throw Later("the probe without a window", "M2b");

    public bool HasWindow => MacWindowSupport.Available(log);

    internal static NotSupportedException Later(string part, string step) =>
        new($"{part} of the macOS app is not written yet (step {step})");
}
