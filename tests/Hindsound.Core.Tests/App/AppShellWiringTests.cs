using System.Reflection;
using System.Text.Json;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// The app run whole on a shell of fakes, as a system without Windows would give it: what the loop, the icon and the end of the
/// session say reaches the app, and a shell with no window leaves the app working.
/// </summary>
public sealed class AppShellWiringTests : IDisposable
{
    private static readonly ScreenInfo Screen = new(new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1040), 96);

    private static readonly ScreenInfo Primary = new(new ScreenRect(-2560, 0, 0, 1440), new ScreenRect(-2560, 0, 0, 1400), 144);

    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-shell-").FullName;
    private readonly FakeShell _shell = new();
    private readonly FakeInstance _instance = new();
    private HindsoundApp? _app;

    private string LogPath => Path.Combine(_folder, "settings.log");

    private string Clips => Path.Combine(_folder, "clips");

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

    private int RunWhile(Action<FakeShell> whileRunning, IScreens? screens = null, Func<IHotkeyNative>? hotkeyLayer = null)
    {
        var settings = Path.Combine(_folder, "settings.json");
        File.WriteAllText(settings, $$$"""
            {"audio":{"micDeviceId":"disabled"},
             "export":{"outputFolder":{{{JsonSerializer.Serialize(Clips)}}},"copyToClipboard":false},
             "general":{"startWithWindows":false,"confirmSound":"never"}}
            """);
        var options = AppOptions.Parse(["--settings", settings, "--no-mic", "--no-hotkeys", "--no-clipboard", "--offscreen"]);
        _shell.Loop.WhileRunning = () => whileRunning(_shell);
        var desktop = new DesktopParts(screens ?? new FakeScreens(), new FakeQuiet(), new FakeSound(), new FakeOpener(), new FakeMeter());
        _app = new HindsoundApp(options, new Journal(LogPath), desktop: desktop, shell: _shell, instance: _instance, hotkeyLayer: hotkeyLayer);
        return _app.Run();
    }

    private List<string> Log() => File.Exists(LogPath) ? Journal.ReadShared(LogPath) : [];

    [Fact]
    public void The_icon_is_added_once_at_the_start_and_again_when_the_shell_says_it_was_lost()
    {
        var addsBefore = -1;

        RunWhile(shell =>
        {
            addsBefore = shell.Tray.Adds;
            shell.Tray.Lost!();
        });

        Assert.Equal(1, addsBefore);
        Assert.Equal(2, _shell.Tray.Adds);
    }

    [Fact]
    public void A_close_asked_from_outside_quits_the_loop()
    {
        RunWhile(shell => shell.Loop.CloseAsked!());

        Assert.Equal(1, _shell.Loop.Quits);
    }

    [Fact]
    public void The_end_of_the_session_takes_the_icon_away_before_it_returns()
    {
        var disposedAtEnd = false;

        RunWhile(shell =>
        {
            shell.Session.Ending!();
            disposedAtEnd = shell.Tray.Disposed;
        });

        Assert.True(disposedAtEnd);
    }

    [Fact]
    public void The_notification_is_made_off_screen_for_an_off_screen_run()
    {
        RunWhile(_ => { });

        Assert.True(_shell.NotificationOffscreen);
    }

    [Fact]
    public void A_second_launch_asks_the_shell_for_a_window_and_without_one_the_clips_folder_stands_in()
    {
        RunWhile(_ => _instance.Received!(InstanceMessage.Show));

        var request = Assert.Single(_shell.WindowRequests);
        Assert.False(request.FirstRun);
        Assert.True(request.Offscreen);
        Assert.Contains(Log(), line => line.Contains("settings window: none on this system", StringComparison.Ordinal));

        // The folder opens off the loop: the run is over before that thread says what it would open.
        var until = Environment.TickCount64 + 5000;
        while (!Log().Exists(line => line.Contains(Clips + " would open now", StringComparison.Ordinal)) && Environment.TickCount64 < until)
        {
            Thread.Sleep(20);
        }

        Assert.Contains(Log(), line => line.Contains(Clips + " would open now", StringComparison.Ordinal));
    }

    [Fact]
    public void The_app_hears_second_launches_on_its_own_loop()
    {
        RunWhile(_ => { });

        Assert.Same(_shell.Loop, _instance.ListenedOn);
    }

    [Fact]
    public void Without_the_screen_of_the_pointer_the_window_is_asked_on_the_main_screen()
    {
        RunWhile(_ => _instance.Received!(InstanceMessage.Show), new FakeScreens(says: false));

        Assert.Equal(Primary, Assert.Single(_shell.WindowRequests).Screen);
    }

    [Fact]
    public void With_the_screen_of_the_pointer_the_window_is_asked_on_that_screen()
    {
        RunWhile(_ => _instance.Received!(InstanceMessage.Show));

        Assert.Equal(Screen, Assert.Single(_shell.WindowRequests).Screen);
    }

    [Fact]
    public void Without_the_screen_of_the_active_window_the_notification_goes_on_the_main_screen()
    {
        RunWhile(_ => ((ISettingsBackend)_app!).ShowTestNotification(), new FakeScreens(says: false));

        var spot = Assert.Single(_shell.Notification.Spots);
        Assert.Equal(NotificationSpot.AreaOn(Primary, false), spot.Area);
        Assert.Equal(Primary.Dpi, spot.Dpi);
    }

    [Fact]
    public void The_settings_probe_on_a_system_with_no_window_says_so_and_leaves_without_touching_a_window()
    {
        RunWhile(_ => typeof(HindsoundApp).GetMethod("RunSettingsProbe", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_app, null));

        Assert.Single(_shell.WindowRequests);
        Assert.Contains(Log(), line => line.Contains("probe: no settings window on this system", StringComparison.Ordinal));
        Assert.Equal(1, _shell.Loop.Quits);
        Assert.Equal(0, _shell.Probe.WindowCalls);
    }

    private sealed class FakeProbe : ISettingsProbe
    {
        public int WindowCalls { get; private set; }

        public uint DrawingObjects() => 0;

        public uint WindowObjects() => 0;

        public void Key(ISettingsWindow window, uint vk) => WindowCalls++;

        public void KeyInTextField(ISettingsWindow window, uint vk) => WindowCalls++;

        public void TypeInTextField(ISettingsWindow window, string text) => WindowCalls++;

        public void Activate(ISettingsWindow window) => WindowCalls++;

        public void ScaleChange(ISettingsWindow window, uint dpi) => WindowCalls++;

        public string ClientSize(ISettingsWindow window) => "";

        public string IconWidths(ISettingsWindow window) => "";

        public bool FolderDialogUsable() => false;
    }

    private sealed class FakeInstance : IInstanceChannel
    {
        public Action<InstanceMessage>? Received { get; private set; }

        public IAppLoop? ListenedOn { get; private set; }

        public bool First => true;

        public bool Tell(InstanceMessage message) => false;

        public void Listen(IAppLoop loop, Action<InstanceMessage> received)
        {
            ListenedOn = loop;
            Received = received;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeShell : IAppShell
    {
        public FakeLoop Loop { get; } = new();

        public FakeTray Tray { get; } = new();

        public FakeSessionEnd Session { get; } = new();

        public bool NotificationOffscreen { get; private set; }

        public FakeNotification Notification { get; } = new();

        public List<SettingsWindowRequest> WindowRequests { get; } = [];

        public IAudioChoices AudioChoices { get; } = new NoChoices();

        public FakeProbe Probe { get; } = new();

        ISettingsProbe IAppShell.Probe => Probe;

        public IAppLoop NewLoop() => Loop;

        public INotificationView NewNotification(IAppLoop loop, bool offscreen)
        {
            NotificationOffscreen = offscreen;
            return Notification;
        }

        public ITrayIcon NewTray(IAppLoop loop) => Tray;

        public ISessionEnd SessionEnd(IAppLoop loop) => Session;

        public IClipboardFiles Clipboard(IAppLoop loop) => new NoClipboard();

        public ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request)
        {
            WindowRequests.Add(request);
            return null;
        }

        public IFolderShell NewFolderShell() => new OffscreenFolderShell(_ => { });

        public IPreviewPlayer NewPreviewPlayer() => throw new NotSupportedException("nothing plays in this test");
    }

    private sealed class FakeLoop : IAppLoop
    {
        public Action? WhileRunning { get; set; }

        public int Quits { get; private set; }

        public Action? CloseAsked { get; set; }

        public Action? ScaleMayHaveChanged { get; set; }

        private int _runThread;

        /// <summary>What was posted from the thread that runs the loop: what another thread posts, such as the engine failing to start without a capture, is dropped.</summary>
        public List<Action> Posted { get; } = [];

        public void Post(Action action)
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref _runThread))
            {
                Posted.Add(action);
            }
        }

        public void After(int milliseconds, Action action)
        {
        }

        public void Run()
        {
            Volatile.Write(ref _runThread, Environment.CurrentManagedThreadId);
            WhileRunning?.Invoke();
        }

        public void Quit() => Quits++;

        public bool InFront() => false;

        public void Dispose()
        {
        }
    }

    private sealed class FakeTray : ITrayIcon
    {
        public int Adds { get; private set; }

        public bool Disposed { get; private set; }

        public Action<uint, nuint>? Message { get; set; }

        public Action? Lost { get; set; }

        public int IconSize => 16;

        public bool IconIsStale => false;

        public bool Add(TrayState state, uint accent, string tip)
        {
            Adds++;
            return true;
        }

        public bool Update(TrayState state, uint accent, string tip) => true;

        public TrayCommand ShowMenu(int x, int y, IReadOnlyList<TrayMenuItem> items) => TrayCommand.None;

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSessionEnd : ISessionEnd
    {
        public Action? Ending { get; set; }

        public Action? Asked { get; set; }

        public Action? Kept { get; set; }

        public bool Block(string reason) => true;

        public bool Unblock() => true;
    }

    private sealed class FakeNotification : INotificationView
    {
        public NotificationContent? Showing => null;

        public Action? Clicked { get; set; }

        public Action? Finished { get; set; }

        public List<NotificationSpot> Spots { get; } = [];

        public void Present(NotificationContent content, NotificationSpot spot, uint accent) => Spots.Add(spot);

        public void Dispose()
        {
        }
    }

    private sealed class NoClipboard : IClipboardFiles
    {
        public bool TrySet(IReadOnlyList<string> paths) => false;
    }

    private sealed class NoChoices : IAudioChoices
    {
        public IReadOnlyList<AudioDeviceInfo> Devices(AudioFlow flow) => [];

        public IReadOnlyList<AudioAppInfo> Apps() => [];
    }

    /// <param name="says">False for a system that does not say where the active window or the pointer is.</param>
    private sealed class FakeScreens(bool says = true) : IScreens
    {
        public ForegroundInfo Foreground() => new(null, false);

        public bool ForegroundCovers() => false;

        public bool ForegroundCovers(ScreenInfo screen) => false;

        public ScreenInfo? ActiveScreen() => says ? Screen : null;

        public ScreenInfo? CursorScreen() => says ? Screen : null;

        public ScreenInfo PrimaryScreen() => Primary;
    }

    /// <summary>A layer that says the first shortcut is bound, and can later say the system took it away.</summary>
    private sealed class ChangingLayer : IHotkeyNative
    {
        public bool Taken { get; set; }

        public event Action<HotkeyAction>? Pressed { add { } remove { } }

        public event Action? BindingsChanged;

        public bool Pending => false;

        public void Change() => BindingsChanged?.Invoke();

        public IReadOnlyList<HotkeyBinding> Bind(IReadOnlyList<HotkeyRequest> requests) =>
            requests.Select(r => new HotkeyBinding(r.Action, r.Proposed, r.Proposed is null ? HotkeyBindingState.Unbound : HotkeyBindingState.Bound)).ToList();

        public HotkeyGesture? BoundKey(HotkeyAction action) => null;

        public HotkeyBinding Current(HotkeyAction action) =>
            new(action, null, action == HotkeyAction.SaveDefault && Taken ? HotkeyBindingState.Refused : HotkeyBindingState.Unbound);

        public void Dispose()
        {
        }
    }

    [Fact]
    public void A_change_the_system_makes_to_the_shortcuts_asks_the_loop_to_read_them_again()
    {
        var layer = new ChangingLayer();
        var journalBefore = new List<string>();
        var posted = 0;

        RunWhile(shell =>
        {
            journalBefore.AddRange(Log());
            layer.Taken = true;
            layer.Change();
            posted = shell.Loop.Posted.Count;
            foreach (var action in shell.Loop.Posted.ToList())
            {
                action();
            }
        }, hotkeyLayer: () => layer);

        Assert.DoesNotContain(journalBefore, line => line.Contains("SaveDefault Ctrl+Shift+F10: InUse", StringComparison.Ordinal));
        Assert.True(posted > 0, "the change reached the loop");
        Assert.Contains(Log(), line => line.Contains("SaveDefault Ctrl+Shift+F10: InUse", StringComparison.Ordinal));
    }

    private sealed class FakeQuiet : IQuietState
    {
        public UserNotificationState Read() => UserNotificationState.AcceptsNotifications;
    }

    private sealed class FakeSound : IConfirmSound
    {
        public bool Play() => false;
    }

    private sealed class FakeOpener : IFileOpener
    {
        public string? Open(string target) => "never opened in a test";
    }

    private sealed class FakeMeter : IProcessMeter
    {
        public string Memory() => "memory not read";

        public double CpuMilliseconds() => 0;
    }
}
