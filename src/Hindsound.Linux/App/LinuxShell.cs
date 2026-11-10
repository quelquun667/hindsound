using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Linux.Audio;
using Hindsound.Linux.Desktop;
using Hindsound.Linux.Tray;
using Hindsound.Ui;

namespace Hindsound.Linux.App;

/// <summary>
/// The running app on Linux: the loop, the notification, the icon, the clipboard, the folder and the end of the session. No
/// window in this version: the settings are the same settings.json, edited by hand.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxShell : IAppShell
{
    private readonly Action<string> log;

    public LinuxShell(Action<string> log)
    {
        this.log = log;
        AudioChoices = new PulseAudioChoices(log);
    }

    public IAppLoop NewLoop() => new LinuxLoop();

    public INotificationView NewNotification(IAppLoop loop, bool offscreen) => new FreedesktopNotification(loop, log);

    public ITrayIcon NewTray(IAppLoop loop) => new StatusNotifierTray(loop, log);

    public ISessionEnd SessionEnd(IAppLoop loop) => new LinuxSessionEnd(loop, log);

    public IClipboardFiles Clipboard(IAppLoop loop) => new LinuxClipboard(log);

    public ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request) => null;

    public IFolderShell NewFolderShell() => new LinuxFolderShell(log);

    public IPreviewPlayer NewPreviewPlayer() => new NoPreviewPlayer();

    public IAudioChoices AudioChoices { get; }

    public ISettingsProbe Probe { get; } = new NoSettingsProbe();

    public bool HasWindow => false;
}
