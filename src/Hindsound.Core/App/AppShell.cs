using Hindsound.Audio;
using Hindsound.Library;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.App;

/// <summary>
/// What the running app gets from the system it runs on, beyond <see cref="DesktopParts"/>: its loop, its notification, its icon,
/// its window, the clipboard and the end of the session. Each part is small and named by its job; a system without a window
/// gives no window, and the app goes on without one.
/// </summary>
internal interface IAppShell
{
    /// <summary>The loop of the app's thread, made on that thread before anything else of the shell.</summary>
    IAppLoop NewLoop();

    /// <summary>The notification, made empty: nothing shows before <see cref="INotificationView.Present"/>.</summary>
    /// <param name="offscreen">Drawn where no screen is, for a test or a probe: nothing shows and clicks pass through.</param>
    INotificationView NewNotification(IAppLoop loop, bool offscreen);

    /// <summary>The icon of the app and its menu. Nothing shows before <see cref="ITrayIcon.Add"/>.</summary>
    ITrayIcon NewTray(IAppLoop loop);

    /// <summary>What the end of the user's session asks of the app, and how the app asks it to wait.</summary>
    ISessionEnd SessionEnd(IAppLoop loop);

    /// <summary>Puts clip files on the clipboard, from the thread of <paramref name="loop"/>.</summary>
    IClipboardFiles Clipboard(IAppLoop loop);

    /// <summary>A new window of the settings, not shown yet; null on a system where the app has no window.</summary>
    ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request);

    /// <summary>Shows clip files in the file manager.</summary>
    IFolderShell NewFolderShell();

    /// <summary>What plays a clip on the Library page.</summary>
    IPreviewPlayer NewPreviewPlayer();

    /// <summary>The devices and the apps the settings offer. Touches the audio system: called off the loop's thread.</summary>
    IAudioChoices AudioChoices { get; }

    /// <summary>What <c>--settings-probe</c> reads of the system and sends to the window.</summary>
    ISettingsProbe Probe { get; }

    /// <summary>Whether the app has windows here: without them, the menu of the icon offers only what works without one.</summary>
    bool HasWindow => true;
}

/// <summary>
/// The thread of the app, its messages and its timers. Everything the app does runs on it, and every other thread comes back
/// through <see cref="Post"/>.
/// </summary>
internal interface IAppLoop : IDisposable
{
    /// <summary>Runs <paramref name="action"/> on the loop's thread, from any thread.</summary>
    void Post(Action action);

    /// <summary>Runs <paramref name="action"/> once on the loop's thread, <paramref name="milliseconds"/> from now.</summary>
    void After(int milliseconds, Action action);

    /// <summary>Runs the loop until <see cref="Quit"/>.</summary>
    void Run();

    void Quit();

    /// <summary>Whether the active window is one of the app's own, as it is while the menu of the icon is open.</summary>
    bool InFront();

    /// <summary>Asked from outside to quit.</summary>
    Action? CloseAsked { get; set; }

    /// <summary>The scale of a screen may have changed: the icon may need new pixels.</summary>
    Action? ScaleMayHaveChanged { get; set; }
}

/// <summary>Where a notification goes, as the app chose it.</summary>
/// <param name="Area">The rectangle it sticks to, in physical pixels: see <see cref="AreaOn"/>.</param>
/// <param name="Dpi">The DPI of that screen.</param>
/// <param name="ClickThrough">Clicks pass through to whatever is below: the active window covers the screen.</param>
/// <param name="LeftEdge">Stuck to the left edge of the screen instead of the right one.</param>
internal readonly record struct NotificationSpot(ScreenRect Area, uint Dpi, bool ClickThrough, bool LeftEdge)
{
    /// <summary>
    /// The work area, or a taskbar on that side would cover the tab. Over a full-screen window there is no taskbar to
    /// avoid, and the tab sticks to the edge of the screen itself.
    /// </summary>
    public static ScreenRect AreaOn(ScreenInfo screen, bool foregroundCoversScreen) =>
        foregroundCoversScreen ? screen.Monitor : screen.Work;
}

/// <summary>The notification: one at a time, replaced in place, never taking the focus.</summary>
internal interface INotificationView : IDisposable
{
    /// <summary>What is shown now, or null.</summary>
    NotificationContent? Showing { get; }

    /// <summary>A click landed on it. Never raised while clicks pass through.</summary>
    Action? Clicked { get; set; }

    /// <summary>The notification has left the screen.</summary>
    Action? Finished { get; set; }

    void Present(NotificationContent content, NotificationSpot spot, uint accent);
}

/// <summary>
/// The icon of the app, its tooltip, and the menu it opens. The gestures and the menu are the app's own words, never the
/// codes of a system: each system turns its clicks into <see cref="TrayGesture"/> and shows the lines of the menu its own way.
/// </summary>
internal interface ITrayIcon : IDisposable
{
    /// <summary>A gesture on the icon, on the thread of the loop: a click, or a request for the menu.</summary>
    Action<TrayGesture>? Gesture { get; set; }

    /// <summary>The line the user chose in the menu, on the thread of the loop; <see cref="TrayCommand.None"/> when the menu closed without one.</summary>
    Action<TrayCommand>? Chosen { get; set; }

    /// <summary>The place that shows the icons came back after a restart: the icon must be added again.</summary>
    Action? Lost { get; set; }

    /// <summary>Whether the icon is there now.</summary>
    bool Add(TrayState state, uint accent, string tip);

    /// <summary>Whether the icon is there now; adds it again if it had gone.</summary>
    bool Update(TrayState state, uint accent, string tip);

    /// <summary>The size of the pixels it shows, for the log.</summary>
    int IconSize { get; }

    /// <summary>Whether the scale of the screen now asks for other pixels.</summary>
    bool IconIsStale { get; }

    /// <summary>
    /// The menu of <paramref name="items"/>, in answer to a <see cref="TrayGesture.Menu"/>. Where the app shows its menu
    /// itself, it opens now where the gesture was; where the desktop shows it, it is the menu the desktop shows. The line
    /// chosen comes back through <see cref="Chosen"/>.
    /// </summary>
    void ShowMenu(IReadOnlyList<TrayMenuItem> items);
}

/// <summary>The end of the user's session, as the app hears it, and the reason the app gives to wait.</summary>
internal interface ISessionEnd
{
    /// <summary>The session ends now: the app must be gone before this returns.</summary>
    Action? Ending { get; set; }

    /// <summary>The session asks whether it may end.</summary>
    Action? Asked { get; set; }

    /// <summary>The session goes on after all.</summary>
    Action? Kept { get; set; }

    /// <summary>Tells the user why the app asks the session to wait. Whether the system took it.</summary>
    bool Block(string reason);

    /// <summary>Takes that reason back. Whether the system took it back.</summary>
    bool Unblock();
}

/// <summary>Puts files on the clipboard, as a file manager pastes them.</summary>
internal interface IClipboardFiles
{
    /// <summary>One attempt. Run it on the thread of the loop.</summary>
    bool TrySet(IReadOnlyList<string> paths);
}

/// <summary>What a new settings window is made with.</summary>
/// <param name="Settings">The settings it shows and changes.</param>
/// <param name="Backend">The app, as the window reaches it.</param>
/// <param name="ReadMicPeak">The level of the microphone, from what the capture already holds.</param>
/// <param name="FirstRun">Opens the first-launch card.</param>
/// <param name="Offscreen">Drawn where no screen is, for a test or a probe: never shown, never takes the focus.</param>
/// <param name="Screen">The screen to open on.</param>
/// <param name="Closed">The window has gone.</param>
internal sealed record SettingsWindowRequest(
    AppSettings Settings,
    ISettingsBackend Backend,
    Func<float> ReadMicPeak,
    bool FirstRun,
    bool Offscreen,
    ScreenInfo Screen,
    Action Closed);

/// <summary>The window of the settings and the Library: one at a time, gone once closed.</summary>
internal interface ISettingsWindow : IDisposable
{
    SettingsScreen Screen { get; }

    /// <summary>The window came to the front.</summary>
    Action? Activated { get; set; }

    void Show();

    /// <summary>Brings an open window back to the front.</summary>
    void BringBack();

    /// <summary>Asks the window to close, as its close button does.</summary>
    void Close();

    /// <summary>The icon of the window, in the accent colour.</summary>
    void SetIcon(uint accent);

    /// <summary>Writes what the window shows to a picture, for a probe or a test.</summary>
    void ProbePicture(string path);
}

/// <summary>The audio devices and the apps that play, as the settings offer them.</summary>
internal interface IAudioChoices
{
    IReadOnlyList<AudioDeviceInfo> Devices(AudioFlow flow);

    IReadOnlyList<AudioAppInfo> Apps();
}

/// <summary>
/// What <c>--settings-probe</c> needs from the system: the counts of its window objects, and keys sent to the window as the
/// keyboard would. Never the real keyboard: the PC may be in use.
/// </summary>
internal interface ISettingsProbe
{
    /// <summary>The drawing objects the process holds.</summary>
    uint DrawingObjects();

    /// <summary>The window objects the process holds.</summary>
    uint WindowObjects();

    /// <summary>A key pressed in <paramref name="window"/>.</summary>
    void Key(ISettingsWindow window, uint vk);

    /// <summary>A key pressed in the text field of <paramref name="window"/>.</summary>
    void KeyInTextField(ISettingsWindow window, uint vk);

    /// <summary>Replaces the text of the field of <paramref name="window"/>, as typing would.</summary>
    void TypeInTextField(ISettingsWindow window, string text);

    /// <summary>The window comes to the front, as a click on it would bring it.</summary>
    void Activate(ISettingsWindow window);

    /// <summary>The window is told its screen has another scale.</summary>
    void ScaleChange(ISettingsWindow window, uint dpi);

    /// <summary>The size of the inside of the window, for the log.</summary>
    string ClientSize(ISettingsWindow window);

    /// <summary>The widths of the icons of the window, for the log.</summary>
    string IconWidths(ISettingsWindow window);

    /// <summary>Whether the dialog that picks a folder can be made.</summary>
    bool FolderDialogUsable();
}
