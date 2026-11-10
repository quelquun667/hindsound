using Hindsound.Audio;
using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.App;

/// <summary>
/// The running app on Windows: the hidden window of the loop and what hangs on it, the tray icon, the notification, the
/// settings window, the clipboard and the end of the session. The handle of the loop's window stays in here.
/// </summary>
internal sealed class WindowsShell : IAppShell
{
    public IAppLoop NewLoop() => new UiLoop();

    public INotificationView NewNotification(IAppLoop loop, bool offscreen) => new Notification { Offscreen = offscreen };

    public ITrayIcon NewTray(IAppLoop loop)
    {
        var owner = Owner(loop);
        var tray = new TrayIcon(owner.Hwnd, UiLoop.TrayCallbackMessage);
        owner.TrayMessage = (code, wParam) => tray.Message?.Invoke(code, wParam);
        owner.TaskbarCreated = () => tray.Lost?.Invoke();
        return tray;
    }

    public ISessionEnd SessionEnd(IAppLoop loop) => new WindowsSessionEnd(Owner(loop));

    public IClipboardFiles Clipboard(IAppLoop loop) => new WindowsClipboard(Owner(loop).Hwnd);

    public ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request) =>
        new SettingsWindow(request.Settings, request.Backend, request.ReadMicPeak, request.FirstRun, request.Offscreen, request.Screen, request.Closed);

    public IFolderShell NewFolderShell() => new ExplorerFolderShell();

    public IPreviewPlayer NewPreviewPlayer() => new MciPreviewPlayer();

    public IAudioChoices AudioChoices { get; } = new WasapiAudioChoices();

    public ISettingsProbe Probe { get; } = new WindowsSettingsProbe();

    // Every part here hangs on the window of the loop this shell made.
    private static UiLoop Owner(IAppLoop loop) =>
        loop as UiLoop ?? throw new ArgumentException("not a loop of the Windows shell", nameof(loop));
}

/// <summary>The end of the Windows session, as the loop's window hears it, and the reason it gives to wait.</summary>
internal sealed class WindowsSessionEnd : ISessionEnd
{
    private readonly nint _owner;

    public WindowsSessionEnd(UiLoop loop)
    {
        _owner = loop.Hwnd;
        loop.SessionEnding = () => Ending?.Invoke();
        loop.SessionEndAsked = () => Asked?.Invoke();
        loop.SessionKept = () => Kept?.Invoke();
    }

    public Action? Ending { get; set; }

    public Action? Asked { get; set; }

    public Action? Kept { get; set; }

    public bool Block(string reason) => Native.ShutdownBlockReasonCreate(_owner, reason) != 0;

    public bool Unblock() => Native.ShutdownBlockReasonDestroy(_owner) != 0;
}

/// <summary>The clipboard, owned by the loop's window.</summary>
internal sealed class WindowsClipboard(nint owner) : IClipboardFiles
{
    public bool TrySet(IReadOnlyList<string> paths) => ClipboardFiles.TrySet(owner, paths);
}

/// <summary>The devices and the apps WASAPI lists.</summary>
internal sealed class WasapiAudioChoices : IAudioChoices
{
    public IReadOnlyList<AudioDeviceInfo> Devices(AudioFlow flow) => WasapiCaptureSourceFactory.ListDevices(flow);

    public IReadOnlyList<AudioAppInfo> Apps() => WasapiCaptureSourceFactory.ListApps();
}

/// <summary>What the probe reads and sends on Windows: the GDI and USER counts, and messages posted to the window, never the real keyboard.</summary>
internal sealed class WindowsSettingsProbe : ISettingsProbe
{
    private const uint GdiObjects = 0, UserObjects = 1;

    public uint DrawingObjects() => WindowNative.GetGuiResources(Native.GetCurrentProcess(), GdiObjects);

    public uint WindowObjects() => WindowNative.GetGuiResources(Native.GetCurrentProcess(), UserObjects);

    public void Key(ISettingsWindow window, uint vk) => Native.PostMessageW(Of(window).Hwnd, WindowNative.WM_KEYDOWN, vk, 0);

    public void KeyInTextField(ISettingsWindow window, uint vk) => Native.PostMessageW(Of(window).EditHwnd, WindowNative.WM_KEYDOWN, vk, 0);

    public void TypeInTextField(ISettingsWindow window, string text) => WindowNative.SetWindowTextW(Of(window).EditHwnd, text);

    public void Activate(ISettingsWindow window) => Native.PostMessageW(Of(window).Hwnd, WindowNative.WM_ACTIVATE, 1, 0);

    public void ScaleChange(ISettingsWindow window, uint dpi) => Of(window).ProbeDpiChange(dpi);

    public string ClientSize(ISettingsWindow window) => Of(window).ClientSizeText();

    public string IconWidths(ISettingsWindow window) => Of(window).IconWidths().ToString();

    public bool FolderDialogUsable() => FolderPicker.SelfCheck();

    private static SettingsWindow Of(ISettingsWindow window) =>
        window as SettingsWindow ?? throw new ArgumentException("not a window of the Windows shell", nameof(window));
}
