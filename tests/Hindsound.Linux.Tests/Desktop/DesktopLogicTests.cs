using System.Runtime.InteropServices;
using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.Desktop;
using Hindsound.Settings;
using Hindsound.Ui;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>The logic of the desktop parts of Linux, which touches no system: it runs everywhere.</summary>
public sealed class DesktopLogicTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static NotificationContent Content(string body = "Saved", string[]? keys = null, string? clip = null) =>
        new(NotificationKind.Silent, "Clip saved", body, keys ?? [], 0, NotificationVisual.Bars, 0, []) { ClipPath = clip };

    [Fact]
    public void A_clip_notification_is_silent_low_and_offers_the_folder()
    {
        var request = NotificationRequest.For(Content(clip: "/home/u/Music/Hindsound/a.mp3"), 7, markup: false);

        Assert.Equal("Hindsound", request.AppName);
        Assert.Equal(7u, request.ReplacesId);
        Assert.Equal("io.github.quelquun667.Hindsound", request.Icon);
        Assert.Equal("Clip saved", request.Summary);
        Assert.Equal("Saved", request.Body);
        Assert.Equal(["default", "Open", "show-folder", "Show in folder"], request.Actions);
        Assert.Equal(0, request.Urgency);
        Assert.True(request.SuppressSound);
        Assert.Equal("io.github.quelquun667.Hindsound", request.DesktopEntry);
        Assert.Equal(-1, request.ExpireTimeout);
        Assert.Equal("susssasa{sv}i", NotificationRequest.Signature);
    }

    [Fact]
    public void A_notification_without_a_clip_has_only_the_click()
    {
        var request = NotificationRequest.For(Content(), 0, markup: false);

        Assert.Equal(["default", "Open"], request.Actions);
        Assert.Equal(0u, request.ReplacesId);
    }

    [Fact]
    public void The_keys_follow_the_text_and_markup_is_escaped_only_for_a_server_that_reads_it()
    {
        Assert.Equal("Saved as WAV. Paste Ctrl+V", NotificationRequest.BodyOf(Content("Saved as WAV. Paste", ["Ctrl", "V"])));
        Assert.Equal("a &amp; &lt;b&gt;", NotificationRequest.For(Content("a & <b>"), 0, markup: true).Body);
        Assert.Equal("a & <b>", NotificationRequest.For(Content("a & <b>"), 0, markup: false).Body);
    }

    [Theory]
    [InlineData("default", "Click")]
    [InlineData("show-folder", "ShowFolder")]
    [InlineData("other", "None")]
    [InlineData(null, "None")]
    public void Each_action_key_asks_for_its_own_thing(string? key, string action) =>
        Assert.Equal(Enum.Parse<NotificationAction>(action), NotificationRequest.ActionOf(key));

    [Fact]
    public void Without_a_bus_a_notification_finishes_at_once_and_says_so_once()
    {
        using var loop = new LinuxLoop();
        var log = new List<string>();
        var finished = 0;
        using var notification = new FreedesktopNotification(loop, log.Add, () => throw new InvalidOperationException("no bus"), new FakeFolder(), holdMs: 60_000);
        notification.Finished = () =>
        {
            finished++;
            if (finished == 2)
            {
                loop.Quit();
            }
        };
        using var guard = new Timer(_ => loop.Post(loop.Quit), null, 10_000, Timeout.Infinite);

        loop.Post(() => notification.Present(Content(), default, 0));
        loop.Post(() => loop.After(200, () => notification.Present(Content("again"), default, 0)));
        loop.Run();

        Assert.Equal(2, finished);
        Assert.Null(notification.Showing);
        Assert.Equal(["notification: not shown (no session bus)"], log);
    }

    [Fact]
    public void A_path_becomes_a_file_uri_with_its_odd_bytes_escaped()
    {
        Assert.Equal("file:///home/u/Music/a%20b%23c%25d%C3%A9.mp3", UriList.FileUri("/home/u/Music/a b#c%dé.mp3"));
        Assert.Equal("file:///a/B-1_x~.wav\r\nfile:///c.mp3\r\n", UriList.Of(["/a/B-1_x~.wav", "/c.mp3"]));
    }

    [Fact]
    public void The_clipboard_tool_follows_the_session()
    {
        static string? All(string name) => "/usr/bin/" + name;
        static string? OnlyXsel(string name) => name == "xsel" ? "/usr/bin/xsel" : null;

        var wayland = ClipboardTool.Choose("wayland-0", ":0", All);
        Assert.Equal("/usr/bin/wl-copy", wayland!.Program);
        Assert.Equal(["--type", "text/uri-list"], wayland.Arguments);

        var x11 = ClipboardTool.Choose(null, ":0", All);
        Assert.Equal("/usr/bin/xclip", x11!.Program);
        Assert.Equal(["-selection", "clipboard", "-t", "text/uri-list"], x11.Arguments);

        Assert.Equal("/usr/bin/xsel", ClipboardTool.Choose("wayland-0", ":0", OnlyXsel)!.Program);
        Assert.Null(ClipboardTool.Choose("wayland-0", null, OnlyXsel));
        Assert.Null(ClipboardTool.Choose(null, null, All));
        Assert.Null(ClipboardTool.Choose("", "", All));
    }

    [Fact]
    public void A_program_is_found_on_the_absolute_folders_of_the_path_only()
    {
        var files = new HashSet<string> { "/usr/local/bin/xdg-open", "/usr/bin/xdg-open", "bin/xclip" };

        Assert.Equal("/usr/local/bin/xdg-open", ExternalTools.Find("xdg-open", "bin:/usr/local/bin/:/usr/bin", files.Contains));
        Assert.Null(ExternalTools.Find("xclip", "bin::/usr/bin", files.Contains));
        Assert.Null(ExternalTools.Find("xdg-open", null, files.Contains));
    }

    [Fact]
    public void The_meter_reads_the_status_of_the_process()
    {
        const string status = "Name:\thindsound\nVmRSS:\t   51200 kB\nRssAnon:\t   20480 kB\nThreads:\t12\n";

        Assert.Equal(51200, ProcSelfMeter.KilobytesOf(status, "VmRSS"));
        Assert.Equal("working set 50.0 MB, private 20.0 MB", ProcSelfMeter.MemoryLine(status));
        Assert.Null(ProcSelfMeter.KilobytesOf("VmRSS: lots kB\n", "VmRSS"));
        Assert.Null(ProcSelfMeter.KilobytesOf(status, "VmSwap"));
        Assert.Equal("memory not measured (no VmRSS or RssAnon)", ProcSelfMeter.MemoryLine("VmRSS:\t1 kB\n"));
    }

    [Fact]
    public void Each_signal_asks_its_own_step()
    {
        Assert.Equal(SignalStep.EndSession, LinuxSessionEnd.StepFor(PosixSignal.SIGTERM, 0));
        Assert.Equal(SignalStep.EndSession, LinuxSessionEnd.StepFor(PosixSignal.SIGTERM, 3));
        Assert.Equal(SignalStep.AskToQuit, LinuxSessionEnd.StepFor(PosixSignal.SIGINT, 1));
        Assert.Equal(SignalStep.Default, LinuxSessionEnd.StepFor(PosixSignal.SIGINT, 2));
        Assert.Equal(SignalStep.Default, LinuxSessionEnd.StepFor(PosixSignal.SIGHUP, 0));
    }

    [Fact]
    public void Sigterm_ends_the_session_and_a_first_ctrl_c_asks_to_quit_on_the_loop()
    {
        using var loop = new LinuxLoop();
        var steps = new List<string>();
        var end = new LinuxSessionEnd(loop, _ => { }, register: false) { Ending = () => steps.Add("ending") };
        loop.CloseAsked = () => steps.Add("close asked");

        Assert.True(end.Handle(PosixSignal.SIGINT));
        Assert.False(end.Handle(PosixSignal.SIGINT));
        Assert.True(end.Handle(PosixSignal.SIGTERM));
        Assert.Empty(steps);
        loop.Post(loop.Quit);
        loop.Run();

        Assert.Equal(["close asked", "ending"], steps);
        Assert.False(end.Block("clips"));
    }

    [Fact]
    public void An_inhibited_server_is_quiet_time()
    {
        Assert.Equal(UserNotificationState.QuietTime, LinuxQuietState.StateOf(true));
        Assert.Equal(UserNotificationState.AcceptsNotifications, LinuxQuietState.StateOf(false));

        var changed = new Dictionary<string, VariantValue> { ["Inhibited"] = VariantValue.Bool(true) };
        Assert.True(LinuxQuietState.InhibitedIn("org.freedesktop.Notifications", changed));
        Assert.Null(LinuxQuietState.InhibitedIn("org.other", changed));
        Assert.Null(LinuxQuietState.InhibitedIn("org.freedesktop.Notifications", new() { ["Other"] = VariantValue.Bool(true) }));
        Assert.Null(LinuxQuietState.InhibitedIn("org.freedesktop.Notifications", new() { ["Inhibited"] = VariantValue.String("yes") }));
    }

    [Fact]
    public void Without_a_bus_the_quiet_state_welcomes_notifications() =>
        Assert.Equal(UserNotificationState.AcceptsNotifications, new LinuxQuietState(() => null).Read());

    [Theory]
    [InlineData("/home/u/Applications/Hindsound.AppImage", "\"/home/u/Applications/Hindsound.AppImage\"")]
    [InlineData("/home/u/a b/hindsound", "\"/home/u/a b/hindsound\"")]
    [InlineData("/x/$y`z\"w\\v%u", "\"/x/\\\\$y\\\\`z\\\\\"w\\\\\\\\v%%u\"")]
    public void The_exec_line_quotes_and_escapes_the_path(string program, string exec)
    {
        Assert.Equal(exec, AutostartFile.ExecOf(program));
        Assert.Equal(program, AutostartFile.Read(AutostartFile.Text(program)));
    }

    [Fact]
    public void The_entry_is_a_hidden_application_that_starts_the_program()
    {
        var text = AutostartFile.Text("/opt/hindsound");

        Assert.StartsWith("[Desktop Entry]\nType=Application\nName=Hindsound\nExec=\"/opt/hindsound\"\n", text);
        Assert.Contains("\nNoDisplay=true\n", text);
        Assert.Contains("\nX-GNOME-Autostart-enabled=true\n", text);
        Assert.Contains("\nIcon=io.github.quelquun667.Hindsound\n", text);
        Assert.Throws<IOException>(() => AutostartFile.Text("/a\nb"));
    }

    [Theory]
    [InlineData("[Desktop Entry]\r\nExec=/usr/bin/hindsound --x\r\n", "/usr/bin/hindsound")]
    [InlineData("[Desktop Entry]\nExec=\"/a\\sb\"\n", "/a b")]
    [InlineData("[Desktop Entry]\nHidden=true\nExec=/a\n", null)]
    [InlineData("[Desktop Entry]\nExec=/a\nX-GNOME-Autostart-enabled=false\n", null)]
    [InlineData("[Other]\nExec=/a\n", null)]
    [InlineData("[Desktop Entry]\nExec=\"/a\n", null)]
    [InlineData("[Desktop Entry]\nName=x\n", null)]
    public void An_entry_written_elsewhere_reads_as_the_session_would(string text, string? program) =>
        Assert.Equal(program, AutostartFile.Read(text));

    [Fact]
    public void The_entry_is_made_found_and_removed_in_its_folder()
    {
        var entry = new AutostartEntry(Path.Combine(_folder, "autostart"));

        Assert.Equal(RunEntry.None, entry.State("/opt/hindsound"));
        entry.Disable();
        entry.Enable("/opt/hindsound");

        Assert.Equal("io.github.quelquun667.Hindsound.desktop", Path.GetFileName(entry.FilePath));
        Assert.Equal(RunEntry.ThisProgram, entry.State("/opt/hindsound"));
        Assert.Equal(RunEntry.OtherProgram, entry.State("/home/u/Hindsound.AppImage"));
        Assert.Equal(RunEntry.OtherProgram, entry.State(null));
        Assert.False(File.Exists(entry.FilePath + ".tmp"));

        entry.Enable("/home/u/Hindsound.AppImage");
        Assert.Equal(RunEntry.ThisProgram, entry.State("/home/u/Hindsound.AppImage"));

        entry.Disable();
        Assert.Equal(RunEntry.None, entry.State("/home/u/Hindsound.AppImage"));
    }

    [Fact]
    public void A_refused_entry_throws_what_the_app_reports_as_refused()
    {
        Directory.CreateDirectory(_folder);
        var blocked = Path.Combine(_folder, "file");
        File.WriteAllText(blocked, "");

        Assert.Throws<IOException>(() => new AutostartEntry(blocked).Enable("/opt/hindsound"));
    }

    [Fact]
    public void Without_the_file_manager_on_the_bus_the_folder_opens_alone()
    {
        var log = new List<string>();
        var opened = new List<string>();

        var shell = new LinuxFolderShell(log.Add, () => null, new FakeOpener(opened, null));
        Assert.Equal(LinuxFolderShell.Ok, shell.ShowSelected("/home/u/Music/Hindsound", ["/home/u/Music/Hindsound/a.mp3"]));
        Assert.Equal(["/home/u/Music/Hindsound"], opened);

        var failing = new LinuxFolderShell(log.Add, () => null, new FakeOpener(opened, "no xdg-open on this system"));
        Assert.Equal(LinuxFolderShell.Failed, failing.ShowSelected("/f", ["/f/a.mp3"]));
        Assert.Equal(["show in folder: no xdg-open on this system"], log);
    }

    private sealed class FakeFolder : IFolderShell
    {
        public int ShowSelected(string folder, IReadOnlyList<string> files) => 0;
    }

    private sealed class FakeOpener(List<string> opened, string? error) : IFileOpener
    {
        public string? Open(string target)
        {
            opened.Add(target);
            return error;
        }
    }
}
