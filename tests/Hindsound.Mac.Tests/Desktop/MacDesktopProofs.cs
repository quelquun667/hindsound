using System.Diagnostics;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Mac.Desktop;
using Hindsound.Mac.Interop;
using Hindsound.Mac.Window;
using Hindsound.Settings;
using Hindsound.Unix.App;
using Xunit.Abstractions;

namespace Hindsound.Mac.Tests.Desktop;

/// <summary>
/// The desktop parts on a real macOS session: the job mac of the CI runs them, and prints their lines. The pasteboard, the
/// Trash and the login entry of the runner are changed and put back; on a Mac of someone's own, outside the CI, the login
/// entry is tried in a folder of the test instead of ~/Library/LaunchAgents.
/// </summary>
[Trait("Category", "Mac")]
[SupportedOSPlatform("macos")]
public sealed class MacDesktopProofs(ITestOutputHelper output) : IDisposable
{
    private const string Program = "/Applications/Hindsound.app/Contents/MacOS/hindsound";

    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "hindsound-desktop-proof-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void A_clip_put_on_the_pasteboard_reads_back_as_its_file_URL()
    {
        var clip = NewFile("Hindsound clip é.mp3");
        var log = new List<string>();

        Assert.True(new MacPasteboard(log.Add).TrySet([clip]), string.Join(" | ", log));
        var read = MacPasteboard.FileUrlOnIt();
        var url = ObjC.FileUrl(clip);
        var expected = ObjC.UrlText(url);
        ObjC.Release(url);

        output.WriteLine($"PROOF pasteboard written={clip} read={read}");
        Assert.Equal(expected, read);
        Assert.False(new MacPasteboard(log.Add).TrySet(["relative.mp3"]));
    }

    [Fact]
    public void A_clip_sent_to_the_Trash_is_found_in_the_Trash_of_the_user()
    {
        var clip = NewFile("Hindsound trash proof.mp3");
        var journal = new List<string>();

        var error = MacTrash.TrashOne(clip, out var inTrash);
        output.WriteLine($"PROOF trash error={error ?? "none"} from={clip} to={inTrash}");
        try
        {
            Assert.Null(error);
            Assert.False(File.Exists(clip));
            var trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
            Assert.NotNull(inTrash);
            Assert.StartsWith(trash + "/", inTrash, StringComparison.Ordinal);
            Assert.True(File.Exists(inTrash), inTrash);
            output.WriteLine($"PROOF trash found={File.Exists(inTrash)} size={new FileInfo(inTrash).Length}");
        }
        finally
        {
            if (inTrash is not null && File.Exists(inTrash))
            {
                File.Delete(inTrash);
            }
        }

        var second = NewFile("second.mp3");
        Assert.Equal(RecycleOutcome.Gone, new MacTrash(journal.Add).Recycle([second, Path.Combine(_folder, "gone.mp3")]));
        Assert.True(File.Exists(second));
        Assert.Equal(RecycleOutcome.Recycled, new MacTrash(journal.Add).Recycle([second]));
        Assert.False(File.Exists(second));
        Assert.Empty(journal);
    }

    [Fact]
    public void The_login_entry_is_enabled_read_back_checked_by_plutil_and_disabled()
    {
        var inCi = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        var entry = inCi ? new LaunchAgentEntry() : new LaunchAgentEntry(Path.Combine(_folder, "LaunchAgents"));
        Assert.Equal(RunEntry.None, entry.State(Program));

        entry.Enable(Program);
        try
        {
            var state = entry.State(Program);
            var lint = Run("/usr/bin/plutil", "-lint", entry.FilePath);
            var first = Run("/usr/bin/plutil", "-extract", "ProgramArguments.0", "raw", "-o", "-", entry.FilePath);
            output.WriteLine($"PROOF login file={entry.FilePath} state={state} plutil-lint={lint.Code}:{lint.Text} program={first.Text}");
            Assert.Equal(RunEntry.ThisProgram, state);
            Assert.Equal(0, lint.Code);
            Assert.Equal(Program, first.Text);
        }
        finally
        {
            entry.Disable();
        }

        output.WriteLine($"PROOF login disabled state={entry.State(Program)} file-left={File.Exists(entry.FilePath)}");
        Assert.Equal(RunEntry.None, entry.State(Program));
    }

    [Fact]
    public void The_Finder_is_asked_to_reveal_a_clip_and_open_answers_without_an_error()
    {
        var clip = NewFile("reveal.mp3");
        var log = new List<string>();

        Assert.Equal(MacOpen.Ok, new MacOpen(log.Add).ShowSelected(_folder, [clip]));
        var direct = Run(MacOpen.Program, "-R", clip);

        // open answers in well under a second; its exit is watched off the caller's thread, and a code other than 0 is logged.
        Thread.Sleep(3000);
        output.WriteLine($"PROOF open-R code-direct={direct.Code} log={(log.Count == 0 ? "empty" : string.Join(" | ", log))}");
        Assert.Equal(0, direct.Code);
        Assert.Empty(log);
    }

    [Fact]
    public void The_screens_are_read_with_their_visible_part_inside_and_the_front_application_is_named()
    {
        var screens = MacScreens.All();
        var parts = new MacScreens();
        foreach (var screen in screens)
        {
            output.WriteLine($"PROOF screen monitor={screen.Monitor} work={screen.Work} dpi={screen.Dpi}");
        }

        var front = MacScreens.Front();
        output.WriteLine($"PROOF screen cursor={parts.CursorScreen()?.Monitor} active={parts.ActiveScreen()?.Monitor} front-app={front.Name} front-bounds={front.Bounds} covers={parts.ForegroundCovers()}");

        Assert.NotEmpty(screens);
        var main = screens[0];
        Assert.Equal(main, parts.PrimaryScreen());
        Assert.Equal(0, main.Monitor.Left);
        Assert.Equal(0, main.Monitor.Top);
        Assert.True(main.Monitor.Width > 0 && main.Monitor.Height > 0);
        Assert.True(main.Work.Top >= main.Monitor.Top && main.Work.Bottom <= main.Monitor.Bottom && main.Work.Height < main.Monitor.Height, main.Work.ToString());
        Assert.Equal(UserNotificationState.AcceptsNotifications, MacDesktop.Parts(_ => { }).Quiet.Read());
    }

    [Fact]
    public void Once_installed_the_Mac_app_has_a_login_entry_and_its_desktop_parts()
    {
        MacPlatform.Install(_ => { });

        Assert.IsType<LaunchAgentEntry>(new AppOptions().RunKey());
        Assert.Null(new AppOptions(SettingsPath: "/tmp/settings.json").RunKey());
        Assert.NotNull(PlatformDefaults.Desktop?.Invoke());
        Assert.Equal("Open at login", PlatformDefaults.Features?.StartupLabel);
    }

    [Fact]
    public void A_character_no_font_has_gets_no_fallback_as_on_Linux_and_not_the_LastResort_sign()
    {
        using var fallback = new CoreTextFallback();
        fallback.CheckLoadable();

        var (postScript, _) = fallback.Describe("\U0010FFFD", bold: false);
        output.WriteLine($"PROOF coretext U+10FFFD picked={postScript}");

        Assert.Null(fallback.Find(0x10FFFD, 80));
        Assert.NotNull(fallback.Find(0x65E5, 80));
    }

    [Fact]
    public void Without_TMPDIR_the_single_instance_lives_in_the_same_folder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var with = InstancePlace.ForThisSystem(Environment.GetEnvironmentVariable, home, "k");
        var without = InstancePlace.ForThisSystem(_ => null, home, "k");
        output.WriteLine($"PROOF instance with-TMPDIR={with?.Folder} without={without?.Folder} confstr={MacUserTemporary.Read()}");

        Assert.NotNull(with);
        Assert.Equal(with, without);
    }

    private string NewFile(string name)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, [0x49, 0x44, 0x33, 4, 0, 0, 0, 0, 0, 0]);
        return path;
    }

    private static (int Code, string Text) Run(string program, params string[] arguments)
    {
        var info = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        return (process.HasExited ? process.ExitCode : -1, text.Trim());
    }
}
