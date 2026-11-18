using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Linux.Audio;
using Hindsound.Linux.Desktop;
using Hindsound.Linux.Library;
using Hindsound.Linux.Tray;
using Hindsound.Linux.Window;
using Hindsound.Linux.Window.Text;
using Hindsound.Ui;

namespace Hindsound.Linux.App;

/// <summary>
/// The running app on Linux: the loop, the notification, the icon, the clipboard, the folder, the end of the session, the
/// settings window on SDL, the listening, the Trash and the reading of a clip. Where the window cannot be drawn (no SDL, no
/// text library, a font missing), the app runs without it, as before the window, and the log says why.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxShell : IAppShell
{
    private readonly Action<string> log;
    private readonly PulsePlaybackSilence silence;
    private LinuxLoop? _loop;
    private ISettingsProbe? _probe;

    public LinuxShell(Action<string> log)
        : this(log, LinuxPlatform.Silence)
    {
    }

    /// <param name="silence">The silence the capture of the PC's sound reads, held by the listening while a clip plays.</param>
    public LinuxShell(Action<string> log, PulsePlaybackSilence silence)
    {
        this.log = log;
        this.silence = silence;
        AudioChoices = new PulseAudioChoices(log);
    }

    // Kept for the window, which waits through the loop: SettingsWindowRequest does not carry it.
    public IAppLoop NewLoop() => _loop = new LinuxLoop();

    public INotificationView NewNotification(IAppLoop loop, bool offscreen) => new FreedesktopNotification(loop, log);

    public ITrayIcon NewTray(IAppLoop loop) => new StatusNotifierTray(loop, log);

    public ISessionEnd SessionEnd(IAppLoop loop) => new LinuxSessionEnd(loop, log);

    public IClipboardFiles Clipboard(IAppLoop loop) => new LinuxClipboard(log);

    public ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request) =>
        HasWindow && _loop is { } loop ? LinuxSettingsWindow.Open(request, loop, () => new FreeTypeText()) : null;

    public IFolderShell NewFolderShell() => new LinuxFolderShell(log);

    public IPreviewPlayer NewPreviewPlayer() => new PulsePreviewPlayer(new LinuxClipDecoder(), silence, log);

    public IRecycleBin NewRecycleBin(Action<string> journal) => FreedesktopTrash.ForUser(journal);

    public IClipDecoder NewClipDecoder() => new LinuxClipDecoder();

    public IAudioChoices AudioChoices { get; }

    public ISettingsProbe Probe => _probe ??= HasWindow ? new LinuxSettingsProbe() : new NoSettingsProbe();

    public bool HasWindow => LinuxWindowSupport.Available(log);
}
