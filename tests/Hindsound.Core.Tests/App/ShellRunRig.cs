using System.Text.Json;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// The app run whole off screen on a shell and a desktop of fakes, with a desktop whose foreground app and pointer the test
/// moves: what the app reads of them, and what it shows, is written down.
/// </summary>
internal sealed class ShellRunRig : IDisposable
{
    public static readonly ScreenInfo Left = new(new ScreenRect(-1920, 0, 0, 1080), new ScreenRect(-1920, 0, 0, 1040), 96);

    public static readonly ScreenInfo Right = new(new ScreenRect(0, 0, 2560, 1440), new ScreenRect(0, 0, 2560, 1400), 144);

    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-run-").FullName;

    public RigShell Shell { get; } = new();

    public MovingScreens Screens { get; } = new();

    public HindsoundApp? App { get; private set; }

    private string LogPath => Path.Combine(_folder, "settings.log");

    public void Run(Action<ShellRunRig> whileRunning)
    {
        var settings = Path.Combine(_folder, "settings.json");
        File.WriteAllText(settings, $$$"""
            {"audio":{"micDeviceId":"disabled"},
             "export":{"outputFolder":{{{JsonSerializer.Serialize(Path.Combine(_folder, "clips"))}}},"copyToClipboard":false},
             "general":{"startWithWindows":false,"confirmSound":"never"}}
            """);
        var options = AppOptions.Parse(["--settings", settings, "--no-mic", "--no-hotkeys", "--no-clipboard", "--offscreen"]);
        Shell.Loop.WhileRunning = () => whileRunning(this);
        var desktop = new DesktopParts(Screens, new RigQuiet(), new RigSound(), new RigOpener(), new RigMeter());
        App = new HindsoundApp(options, new Journal(LogPath), desktop: desktop, shell: Shell, instance: new RigInstance());
        App.Run();
    }

    public List<string> Log() => File.Exists(LogPath) ? Journal.ReadShared(LogPath) : [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A thread of the app may still hold the log a moment after the run: the system cleans its temporary folder.
        }
    }

    /// <summary>The foreground app and the screen of the pointer are what the test last said.</summary>
    internal sealed class MovingScreens : IScreens
    {
        public string? App { get; set; }

        public ScreenInfo Pointer { get; set; } = Right;

        public ForegroundInfo Foreground() => new(App, false);

        public bool ForegroundCovers() => false;

        public bool ForegroundCovers(ScreenInfo screen) => false;

        public ScreenInfo? ActiveScreen() => Pointer;

        public ScreenInfo? CursorScreen() => Pointer;

        public ScreenInfo PrimaryScreen() => Right;
    }

    internal sealed class RigShell : IAppShell
    {
        public RigLoop Loop { get; } = new();

        public RigTray Tray { get; } = new();

        public RigNotification Notification { get; } = new();

        public IAudioChoices AudioChoices { get; } = new RigChoices();

        public ISettingsProbe Probe { get; } = new RigProbe();

        public IAppLoop NewLoop() => Loop;

        public INotificationView NewNotification(IAppLoop loop, bool offscreen) => Notification;

        public ITrayIcon NewTray(IAppLoop loop) => Tray;

        public ISessionEnd SessionEnd(IAppLoop loop) => new RigSessionEnd();

        public IClipboardFiles Clipboard(IAppLoop loop) => new RigClipboard();

        public ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request) => null;

        public IFolderShell NewFolderShell() => new OffscreenFolderShell(_ => { });

        public IPreviewPlayer NewPreviewPlayer() => throw new NotSupportedException("nothing plays in this test");
    }

    internal sealed class RigLoop : IAppLoop
    {
        public Action? WhileRunning { get; set; }

        public Action? CloseAsked { get; set; }

        public Action? ScaleMayHaveChanged { get; set; }

        // Nothing posted or delayed runs: the test drives the app itself, on the thread that runs the loop.
        public void Post(Action action)
        {
        }

        public void After(int milliseconds, Action action)
        {
        }

        public void Run() => WhileRunning?.Invoke();

        public void Quit()
        {
        }

        public bool InFront() => false;

        public void Dispose()
        {
        }
    }

    internal sealed class RigTray : ITrayIcon
    {
        public Action<TrayGesture>? Gesture { get; set; }

        public Action<TrayCommand>? Chosen { get; set; }

        public Action? Lost { get; set; }

        public int IconSize => 16;

        public bool IconIsStale => false;

        public bool Add(TrayState state, uint accent, string tip) => true;

        public bool Update(TrayState state, uint accent, string tip) => true;

        public void ShowMenu(IReadOnlyList<TrayMenuItem> items)
        {
        }

        public void Dispose()
        {
        }
    }

    internal sealed class RigNotification : INotificationView
    {
        public NotificationContent? Showing => null;

        public Action? Clicked { get; set; }

        public Action? Finished { get; set; }

        public List<NotificationContent> Presented { get; } = [];

        public void Present(NotificationContent content, NotificationSpot spot, uint accent) => Presented.Add(content);

        public void Dispose()
        {
        }
    }

    private sealed class RigSessionEnd : ISessionEnd
    {
        public Action? Ending { get; set; }

        public Action? Asked { get; set; }

        public Action? Kept { get; set; }

        public bool Block(string reason) => true;

        public bool Unblock() => true;
    }

    private sealed class RigClipboard : IClipboardFiles
    {
        public bool TrySet(IReadOnlyList<string> paths) => false;
    }

    private sealed class RigChoices : IAudioChoices
    {
        public IReadOnlyList<AudioDeviceInfo> Devices(AudioFlow flow) => [];

        public IReadOnlyList<AudioAppInfo> Apps() => [];
    }

    private sealed class RigProbe : ISettingsProbe
    {
        public uint DrawingObjects() => 0;

        public uint WindowObjects() => 0;

        public void Key(ISettingsWindow window, uint vk)
        {
        }

        public void KeyInTextField(ISettingsWindow window, uint vk)
        {
        }

        public void TypeInTextField(ISettingsWindow window, string text)
        {
        }

        public void Activate(ISettingsWindow window)
        {
        }

        public void ScaleChange(ISettingsWindow window, uint dpi)
        {
        }

        public string ClientSize(ISettingsWindow window) => "";

        public string IconWidths(ISettingsWindow window) => "";

        public bool FolderDialogUsable() => false;
    }

    private sealed class RigInstance : IInstanceChannel
    {
        public bool First => true;

        public bool Tell(InstanceMessage message) => false;

        public void Listen(IAppLoop loop, Action<InstanceMessage> received)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RigQuiet : IQuietState
    {
        public UserNotificationState Read() => UserNotificationState.AcceptsNotifications;
    }

    private sealed class RigSound : IConfirmSound
    {
        public bool Play() => false;
    }

    private sealed class RigOpener : IFileOpener
    {
        public string? Open(string target) => "never opened in a test";
    }

    private sealed class RigMeter : IProcessMeter
    {
        public string Memory() => "memory not read";

        public double CpuMilliseconds() => 0;
    }
}
