using System.Runtime.InteropServices;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Mac.Desktop;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Unix.App;

namespace Hindsound.Mac.Tests.Desktop;

/// <summary>The desktop parts of the Mac app apart from macOS: what they write, read and work out runs on every system.</summary>
public sealed class MacDesktopLogicTests : IDisposable
{
    private const string Program = "/Applications/Hindsound.app/Contents/MacOS/hindsound";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hindsound-mac-desktop-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void The_launch_agent_starts_the_program_at_login_in_the_graphic_session_once()
    {
        var text = LaunchAgentFile.Text(Program);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC", text, StringComparison.Ordinal);
        Assert.Contains("<key>Label</key>", text, StringComparison.Ordinal);
        Assert.Contains("<string>io.github.quelquun667.hindsound</string>", text, StringComparison.Ordinal);
        Assert.Contains("<key>RunAtLoad</key>", text, StringComparison.Ordinal);
        Assert.Contains("<true />", text, StringComparison.Ordinal);
        Assert.Contains("<string>Aqua</string>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepAlive", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.Equal(Program, LaunchAgentFile.Read(text));
    }

    [Theory]
    [InlineData("/Users/you/My Apps/Hindsound.app/Contents/MacOS/hindsound")]
    [InlineData("/Users/you/a&b<c>\"d'/hindsound")]
    [InlineData("/Users/you/é 日本/hindsound")]
    public void A_path_the_XML_must_escape_reads_back_whole(string program) =>
        Assert.Equal(program, LaunchAgentFile.Read(LaunchAgentFile.Text(program)));

    [Fact]
    public void A_path_with_a_control_character_cannot_be_written() =>
        Assert.Throws<IOException>(() => LaunchAgentFile.Text("/Users/you/a\nb/hindsound"));

    [Theory]
    [InlineData("not a plist")]
    [InlineData("<plist><dict><key>Label</key><string>other.job</string><key>ProgramArguments</key><array><string>/x</string></array></dict></plist>")]
    [InlineData("<plist><dict><key>Label</key><string>io.github.quelquun667.hindsound</string><key>Disabled</key><true/><key>ProgramArguments</key><array><string>/x</string></array></dict></plist>")]
    [InlineData("<plist><dict><key>Label</key><string>io.github.quelquun667.hindsound</string></dict></plist>")]
    public void An_entry_that_starts_nothing_of_ours_reads_as_none(string text) => Assert.Null(LaunchAgentFile.Read(text));

    [Fact]
    public void An_entry_with_Program_alone_names_it() => Assert.Equal("/x/hindsound", LaunchAgentFile.Read(
        "<plist><dict><key>Label</key><string>io.github.quelquun667.hindsound</string><key>Program</key><string>/x/hindsound</string></dict></plist>"));

    [Fact]
    public void The_entry_is_written_read_and_removed_in_its_folder()
    {
        var entry = new LaunchAgentEntry(_folder);

        Assert.Equal(RunEntry.None, entry.State(Program));
        entry.Enable(Program);
        Assert.Equal(Path.Combine(_folder, "io.github.quelquun667.hindsound.plist"), entry.FilePath);
        Assert.Equal(RunEntry.ThisProgram, entry.State(Program));
        Assert.Equal(RunEntry.OtherProgram, entry.State("/Applications/Old/Hindsound.app/Contents/MacOS/hindsound"));
        Assert.Equal(RunEntry.OtherProgram, entry.State(null));
        Assert.False(File.Exists(entry.FilePath + ".tmp"));

        var registration = new StartupRegistration(entry, Program);
        Assert.True(registration.IsEnabled());
        Assert.True(registration.Disable());
        Assert.Equal(RunEntry.None, entry.State(Program));
        entry.Disable();
    }

    [Fact]
    public void Without_a_home_folder_there_is_no_entry_and_enabling_is_refused()
    {
        var entry = new LaunchAgentEntry(LaunchAgentEntry.FolderIn(null));

        Assert.Equal(RunEntry.None, entry.State(Program));
        Assert.Throws<IOException>(() => entry.Enable(Program));
        entry.Disable();
        Assert.False(new StartupRegistration(entry, Program).Enable());
        Assert.Equal("/Users/you/Library/LaunchAgents", LaunchAgentEntry.FolderIn("/Users/you/"));
        Assert.Null(LaunchAgentEntry.FolderIn("relative"));
        Assert.Null(LaunchAgentEntry.FolderIn(""));
    }

    [Fact]
    public void Open_opens_a_target_and_reveals_files_never_reading_a_name_as_an_option()
    {
        Assert.Equal(["/Users/you/Music/Hindsound"], MacOpen.OpenArguments("/Users/you/Music/Hindsound"));
        Assert.Equal(["./-x.mp3"], MacOpen.OpenArguments("-x.mp3"));
        Assert.Equal(["-R", "/m/a.mp3", "/m/b.mp3"], MacOpen.RevealArguments("/m", ["/m/a.mp3", "/m/b.mp3"]));
        Assert.Equal(["/m"], MacOpen.RevealArguments("/m", []));
    }

    [Fact]
    public void Open_that_cannot_start_fails_with_an_HRESULT_and_says_why()
    {
        var log = new List<string>();
        var open = new MacOpen(log.Add, Path.Combine(_folder, "no-such-open"));

        Assert.Equal(MacOpen.Failed, open.ShowSelected("/m", ["/m/a.mp3"]));
        Assert.NotNull(open.Open("/m"));
        Assert.Contains(log, line => line.StartsWith("show in folder: open -R: could not start", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rectangle_of_Cocoa_turns_to_a_top_left_origin_on_the_main_screen()
    {
        // The main screen, 1512 x 982 points; its visible part without a menu bar of 33 and a Dock of 70 at the bottom.
        Assert.Equal(new ScreenRect(0, 0, 1512, 982), ScreenGeometry.FromCocoa(new CGRect(0, 0, 1512, 982), 982));
        Assert.Equal(new ScreenRect(0, 33, 1512, 912), ScreenGeometry.FromCocoa(new CGRect(0, 70, 1512, 879), 982));

        // A second screen above the main one, and one to its left that reaches lower.
        Assert.Equal(new ScreenRect(0, -1080, 1920, 0), ScreenGeometry.FromCocoa(new CGRect(0, 982, 1920, 1080), 982));
        Assert.Equal(new ScreenRect(-1920, 0, 0, 1200), ScreenGeometry.FromCocoa(new CGRect(-1920, -218, 1920, 1200), 982));
        Assert.Equal((100, 882), ScreenGeometry.FromCocoa(new CGPoint(100.5, 100), 982));
    }

    [Fact]
    public void A_window_covers_its_screen_only_when_it_holds_all_of_it()
    {
        var monitor = new ScreenRect(0, 0, 1512, 982);

        Assert.True(ScreenGeometry.Covers(new ScreenRect(0, 0, 1512, 982), monitor));
        Assert.True(ScreenGeometry.Covers(new ScreenRect(-1, -1, 1600, 1000), monitor));
        Assert.False(ScreenGeometry.Covers(new ScreenRect(0, 33, 1512, 982), monitor));
        Assert.False(ScreenGeometry.Covers(new ScreenRect(0, 0, 1511, 982), monitor));
        Assert.False(ScreenGeometry.Covers(new ScreenRect(0, 0, 0, 0), new ScreenRect(0, 0, 0, 0)));
        Assert.Equal(new ScreenRect(10, 20, 111, 221), ScreenGeometry.Round(new CGRect(10.4, 19.6, 100.6, 201)));
    }

    [Fact]
    public void A_point_belongs_to_the_screen_that_holds_it()
    {
        var main = new ScreenInfo(new ScreenRect(0, 0, 1512, 982), new ScreenRect(0, 33, 1512, 912), 96);
        var left = new ScreenInfo(new ScreenRect(-1920, 0, 0, 1200), new ScreenRect(-1920, 25, 0, 1200), 96);
        ScreenInfo[] screens = [main, left];

        Assert.Equal(main, ScreenGeometry.At((0, 0), screens));
        Assert.Equal(main, ScreenGeometry.At((1511, 981), screens));
        Assert.Equal(left, ScreenGeometry.At((-1, 1100), screens));
        Assert.Null(ScreenGeometry.At((1512, 0), screens));
        Assert.Null(ScreenGeometry.At((100, 1100), screens));
        Assert.Null(ScreenGeometry.At((0, 0), []));
    }

    [Fact]
    public void A_refusal_of_the_first_file_leaves_every_file_and_a_later_one_asks_to_look_again()
    {
        Assert.Equal(RecycleOutcome.CannotRecycle, MacTrash.OutcomeOfFailure(0));
        Assert.Equal(RecycleOutcome.Failed, MacTrash.OutcomeOfFailure(1));
        Assert.Equal(RecycleOutcome.Failed, MacTrash.OutcomeOfFailure(4));
    }

    [Fact]
    public void The_memory_line_says_what_macOS_gives_and_what_it_does_not()
    {
        Assert.Equal("working set 64.0 MB, private not measured", MacProcessMeter.MemoryLine(64L << 20, 0));
        Assert.Equal("working set 64.0 MB, private 32.5 MB", MacProcessMeter.MemoryLine(64L << 20, (32L << 20) + (1L << 19)));
        Assert.Equal("memory not measured (no resident size)", MacProcessMeter.MemoryLine(0, 0));
        Assert.Matches("^(working set|memory not measured)", new MacProcessMeter().Memory());
        Assert.True(new MacProcessMeter().CpuMilliseconds() > 0);
    }

    [Fact]
    public void Focus_has_no_public_interface_so_notifications_are_always_welcome() =>
        Assert.Equal(UserNotificationState.AcceptsNotifications, new MacQuietState().Read());

    [Fact]
    public void Each_signal_asks_its_own_step()
    {
        Assert.Equal(MacSignalStep.EndSession, MacSessionEnd.StepFor(PosixSignal.SIGTERM, 0));
        Assert.Equal(MacSignalStep.AskToQuit, MacSessionEnd.StepFor(PosixSignal.SIGINT, 1));
        Assert.Equal(MacSignalStep.Default, MacSessionEnd.StepFor(PosixSignal.SIGINT, 2));
        Assert.Equal(MacSignalStep.Default, MacSessionEnd.StepFor(PosixSignal.SIGHUP, 0));
    }

    [Fact]
    public void Sigterm_ends_the_session_and_a_first_ctrl_c_asks_to_quit_on_the_loop_and_nothing_asks_the_session_to_wait()
    {
        using var loop = new UnixLoop();
        var steps = new List<string>();
        var end = new MacSessionEnd(loop, _ => { }, register: false) { Ending = () => steps.Add("ending") };
        loop.CloseAsked = () => steps.Add("close asked");

        Assert.True(end.Handle(PosixSignal.SIGINT));
        Assert.False(end.Handle(PosixSignal.SIGINT));
        Assert.True(end.Handle(PosixSignal.SIGTERM));
        Assert.Empty(steps);
        loop.Post(loop.Quit);
        loop.Run();

        Assert.Equal(["close asked", "ending"], steps);
        Assert.False(end.Block("clips"));
        Assert.False(end.Unblock());
    }

    [Fact]
    public void Without_TMPDIR_the_single_instance_meets_in_the_temporary_folder_the_system_gives()
    {
        const string Tmp = "/var/folders/zz/abcdefghijklmnopqrstuvwxyz0000gn/T/";
        Func<string, string?> none = _ => null;
        Func<string, string?> withTmp = name => name == InstancePlace.MacTemporaryVariable ? Tmp : null;

        var with = InstancePlace.ForMac(withTmp, "/Users/you", "k", () => Tmp);
        var without = InstancePlace.ForMac(none, "/Users/you", "k", () => Tmp);

        Assert.NotNull(with);
        Assert.Equal(with, without);
    }

    [Fact]
    public void A_temporary_folder_too_long_for_a_socket_gives_way_to_Library_Caches_with_or_without_TMPDIR()
    {
        var tooLong = "/var/folders/" + new string('a', 120);
        Func<string, string?> withTmp = name => name == InstancePlace.MacTemporaryVariable ? tooLong : null;

        var with = InstancePlace.ForMac(withTmp, "/Users/you", "k");
        var without = InstancePlace.ForMac(_ => null, "/Users/you", "k", () => tooLong);

        Assert.NotNull(with);
        Assert.Equal("/Users/you/Library/Caches/Hindsound", with.Folder);
        Assert.True(with.OwnFolder);
        Assert.Equal(with, without);
        Assert.Null(InstancePlace.ForMac(withTmp, "/" + new string('h', 120), "k"));
    }
}
