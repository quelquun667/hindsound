using System.Collections.Concurrent;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// A click on the notification of a clip, on an app built and never run. Where the system gives no window, the clip is shown
/// selected in its folder, as "Show in folder" does; where it gives one, the click goes to the window, as before.
/// </summary>
public sealed class AppNotificationClickWithoutWindowTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-click-").FullName;
    private readonly List<Section> _opened = [];
    private readonly Opener _opener = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A thread of the app may still hold the log a moment after the test: the system cleans its temporary folder.
        }
    }

    private string Clip => Path.Combine(_folder, "clips", "Hindsound 2026-11-10 12-00-00.mp3");

    private HindsoundApp Built(Shell shell)
    {
        var options = AppOptions.Parse(["--settings", Path.Combine(_folder, "settings.json"), "--no-mic", "--no-hotkeys", "--no-clipboard"]);
        var desktop = new DesktopParts(new ShellRunRig.MovingScreens(), new Quiet(), new Sound(), _opener, new Meter());
        return new HindsoundApp(options, new Journal(Path.Combine(_folder, "settings.log")), desktop: desktop, shell: shell) { OpenInstead = _opened.Add };
    }

    private static NotificationContent ClipNotice(string? clip) =>
        new(NotificationKind.Silent, "Clip saved", "Saved", [], 0, NotificationVisual.Bars, 0, []) { ClipPath = clip };

    private void WriteClip()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Clip)!);
        File.WriteAllBytes(Clip, [1, 2, 3]);
    }

    [Fact]
    public void Without_a_window_a_click_on_a_clip_shows_it_selected_in_its_folder()
    {
        WriteClip();
        var shell = new Shell(hasWindow: false);

        Built(shell).NotificationClicked(ClipNotice(Clip));

        Assert.True(SpinWait.SpinUntil(() => !shell.Folder.Shown.IsEmpty, Wait), "the folder shell was never asked");
        var (folder, files) = Assert.Single(shell.Folder.Shown);
        Assert.Equal(Path.GetDirectoryName(Clip), folder);
        Assert.Equal([Clip], files);
        Assert.Empty(_opened);
        Assert.Empty(_opener.Opened);
    }

    [Fact]
    public void Without_a_window_a_clip_gone_since_opens_its_folder()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Clip)!);
        var shell = new Shell(hasWindow: false);

        Built(shell).NotificationClicked(ClipNotice(Clip));

        Assert.True(SpinWait.SpinUntil(() => !_opener.Opened.IsEmpty, Wait), "nothing was opened");
        Assert.Equal([Path.GetDirectoryName(Clip)!], _opener.Opened);
        Assert.Empty(shell.Folder.Shown);
        Assert.Empty(_opened);
    }

    [Fact]
    public void Without_a_window_a_file_manager_that_refuses_leaves_the_folder_to_open()
    {
        WriteClip();
        var shell = new Shell(hasWindow: false);
        shell.Folder.Answer = unchecked((int)0x80004005);

        Built(shell).NotificationClicked(ClipNotice(Clip));

        Assert.True(SpinWait.SpinUntil(() => !_opener.Opened.IsEmpty, Wait), "nothing was opened");
        Assert.Single(shell.Folder.Shown);
        Assert.Equal([Path.GetDirectoryName(Clip)!], _opener.Opened);
        Assert.Empty(_opened);
    }

    [Fact]
    public void Without_a_window_a_notice_with_no_clip_goes_where_it_went()
    {
        var shell = new Shell(hasWindow: false);

        Built(shell).NotificationClicked(ClipNotice(null));

        // The route of the window, which a system without one turns into the folder of the clips.
        Assert.Equal([Section.Library], _opened);
        Assert.Equal(0, shell.FolderShells);
    }

    [Fact]
    public void With_a_window_a_click_on_a_clip_still_goes_to_the_window()
    {
        WriteClip();
        var shell = new Shell(hasWindow: true);

        Built(shell).NotificationClicked(ClipNotice(Clip));

        Assert.Single(_opened);
        Assert.Equal(0, shell.FolderShells);
        Assert.Empty(_opener.Opened);
    }

    private sealed class Shell(bool hasWindow) : IAppShell
    {
        private readonly ShellRunRig.RigShell _rig = new();

        public RecordingFolder Folder { get; } = new();

        public int FolderShells { get; private set; }

        public bool HasWindow => hasWindow;

        public IAudioChoices AudioChoices => _rig.AudioChoices;

        public ISettingsProbe Probe => _rig.Probe;

        public IAppLoop NewLoop() => _rig.NewLoop();

        public INotificationView NewNotification(IAppLoop loop, bool offscreen) => _rig.NewNotification(loop, offscreen);

        public ITrayIcon NewTray(IAppLoop loop) => _rig.NewTray(loop);

        public ISessionEnd SessionEnd(IAppLoop loop) => _rig.SessionEnd(loop);

        public IClipboardFiles Clipboard(IAppLoop loop) => _rig.Clipboard(loop);

        public ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request) => null;

        public IFolderShell NewFolderShell()
        {
            FolderShells++;
            return Folder;
        }

        public IPreviewPlayer NewPreviewPlayer() => throw new NotSupportedException("nothing plays in this test");
    }

    private sealed class RecordingFolder : IFolderShell
    {
        public ConcurrentQueue<(string Folder, string[] Files)> Shown { get; } = new();

        public int Answer { get; set; }

        public int ShowSelected(string folder, IReadOnlyList<string> files)
        {
            Shown.Enqueue((folder, files.ToArray()));
            return Answer;
        }
    }

    private sealed class Opener : IFileOpener
    {
        public ConcurrentQueue<string> Opened { get; } = new();

        public string? Open(string target)
        {
            Opened.Enqueue(target);
            return null;
        }
    }

    private sealed class Quiet : IQuietState
    {
        public UserNotificationState Read() => UserNotificationState.AcceptsNotifications;
    }

    private sealed class Sound : IConfirmSound
    {
        public bool Play() => false;
    }

    private sealed class Meter : IProcessMeter
    {
        public string Memory() => "memory not read";

        public double CpuMilliseconds() => 0;
    }
}
