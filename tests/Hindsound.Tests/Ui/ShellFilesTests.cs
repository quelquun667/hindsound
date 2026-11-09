using System.IO;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>A line of the library, in front of Windows: no window opens and the clipboard stays as it is, the calls are fakes.</summary>
public sealed class ShellFilesTests
{
    private const string Canary = "Collection-Canary-7F3";
    private static readonly string CanaryPath = $@"C:\{Canary}\{Canary}.mp3";

    private static ClipGroup Line(params string[] paths) =>
        new(paths.Select(path => new ClipFile(path, 10, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc))).ToList());

    private static bool Everything(string path) => true;

    private sealed class FakeShell(int answer = 0, Exception? throws = null) : IFolderShell
    {
        public List<(string Folder, IReadOnlyList<string> Files)> Calls { get; } = [];

        public int ShowSelected(string folder, IReadOnlyList<string> files)
        {
            Calls.Add((folder, files));
            return throws is null ? answer : throw throws;
        }
    }

    [Fact]
    public void Show_in_folder_selects_the_two_tracks_of_a_line_in_one_call()
    {
        var shell = new FakeShell();

        var outcome = ShellFiles.ShowInFolder(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"), shell, Everything);

        Assert.Equal(ShellOutcome.Done, outcome);
        var call = Assert.Single(shell.Calls);
        Assert.Equal(@"C:\clips", call.Folder);
        Assert.Equal([@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"], call.Files);
    }

    [Fact]
    public void Show_in_folder_selects_the_track_that_is_left()
    {
        var shell = new FakeShell();

        var outcome = ShellFiles.ShowInFolder(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"), shell, path => path.EndsWith("_mic.mp3", StringComparison.Ordinal));

        Assert.Equal(ShellOutcome.Done, outcome);
        Assert.Equal([@"C:\clips\a_mic.mp3"], Assert.Single(shell.Calls).Files);
    }

    [Fact]
    public void Show_in_folder_with_nothing_left_says_gone_and_opens_nothing()
    {
        var shell = new FakeShell();

        var outcome = ShellFiles.ShowInFolder(Line(@"C:\clips\a.mp3"), shell, _ => false);

        Assert.Equal(ShellOutcome.Gone, outcome);
        Assert.Empty(shell.Calls);
    }

    [Fact]
    public void Show_in_folder_keeps_to_one_folder_whatever_the_case_of_its_name()
    {
        var shell = new FakeShell();

        var outcome = ShellFiles.ShowInFolder(Line(@"C:\clips\a.mp3", @"C:\CLIPS\b.mp3", @"D:\other\c.mp3"), shell, Everything);

        Assert.Equal(ShellOutcome.Done, outcome);
        var call = Assert.Single(shell.Calls);
        Assert.Equal(@"C:\clips", call.Folder);
        Assert.Equal([@"C:\clips\a.mp3", @"C:\CLIPS\b.mp3"], call.Files);
    }

    [Fact]
    public void A_long_path_prefix_never_reaches_the_shell()
    {
        var shell = new FakeShell();

        ShellFiles.ShowInFolder(Line(@"\\?\C:\clips\a.mp3"), shell, Everything);

        var call = Assert.Single(shell.Calls);
        Assert.Equal(@"C:\clips", call.Folder);
        Assert.Equal([@"C:\clips\a.mp3"], call.Files);
    }

    [Fact]
    public void A_path_without_a_folder_fails_without_calling_the_shell()
    {
        var shell = new FakeShell();

        Assert.Equal(ShellOutcome.Failed, ShellFiles.ShowInFolder(Line(@"C:\"), shell, Everything));
        Assert.Empty(shell.Calls);
    }

    [Fact]
    public void A_refusal_of_the_shell_fails_and_the_log_gets_its_code_alone()
    {
        var journal = new List<string>();
        var shell = new FakeShell(answer: unchecked((int)0x80070002));

        var outcome = ShellFiles.ShowInFolder(Line(CanaryPath), shell, Everything, journal.Add);

        Assert.Equal(ShellOutcome.Failed, outcome);
        Assert.Contains("0x80070002", Assert.Single(journal));
        Assert.DoesNotContain("Canary", string.Join("\n", journal));
    }

    [Fact]
    public void A_positive_answer_of_the_shell_is_a_success()
    {
        var journal = new List<string>();

        Assert.Equal(ShellOutcome.Done, ShellFiles.ShowInFolder(Line(@"C:\clips\a.mp3"), new FakeShell(answer: 1), Everything, journal.Add));
        Assert.Empty(journal);
    }

    [Fact]
    public void An_exception_of_the_shell_is_caught_and_its_message_stays_out_of_the_log()
    {
        var journal = new List<string>();
        var shell = new FakeShell(throws: new IOException($"{CanaryPath} is held") { HResult = unchecked((int)0x80070020) });

        var outcome = ShellFiles.ShowInFolder(Line(CanaryPath), shell, Everything, journal.Add);

        Assert.Equal(ShellOutcome.Failed, outcome);
        var line = Assert.Single(journal);
        Assert.Contains("IOException", line);
        Assert.Contains("0x80070020", line);
        Assert.DoesNotContain("Canary", line);
    }

    [Fact]
    public void A_check_that_throws_counts_as_gone_and_stays_out_of_the_log()
    {
        var journal = new List<string>();
        var shell = new FakeShell();

        var outcome = ShellFiles.ShowInFolder(Line(CanaryPath), shell, _ => throw new IOException(CanaryPath), journal.Add);

        Assert.Equal(ShellOutcome.Gone, outcome);
        Assert.Empty(shell.Calls);
        Assert.Contains("IOException", Assert.Single(journal));
        Assert.DoesNotContain("Canary", string.Join("\n", journal));
    }

    [Fact]
    public void Off_the_loop_it_runs_in_a_single_threaded_apartment_and_answers_through_the_post()
    {
        var caller = Environment.CurrentManagedThreadId;
        var shell = new ApartmentShell();
        var posted = new List<Action>();
        var outcomes = new List<ShellOutcome>();

        var thread = ShellFiles.ShowInFolderOffLoop(Line(typeof(ShellFilesTests).Assembly.Location), shell, posted.Add, outcomes.Add);

        Assert.True(thread!.Join(TimeSpan.FromSeconds(30)));
        Assert.Equal(ApartmentState.STA, shell.Apartment);
        Assert.NotEqual(caller, shell.Thread);
        Assert.Empty(outcomes);
        Assert.Single(posted)();
        Assert.Equal(ShellOutcome.Done, Assert.Single(outcomes));
    }

    [Fact]
    public void Off_screen_nothing_is_shown_and_no_path_is_written()
    {
        var journal = new List<string>();

        var answer = new OffscreenFolderShell(journal.Add).ShowSelected($@"C:\{Canary}", [CanaryPath]);

        Assert.Equal(0, answer);
        Assert.DoesNotContain("Canary", Assert.Single(journal));
    }

    [Fact]
    public void Copy_hands_the_two_tracks_to_the_way_of_the_app()
    {
        IReadOnlyList<string>? set = null;
        var outcomes = new List<ShellOutcome>();

        ShellFiles.Copy(
            Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"),
            Everything,
            paths =>
            {
                set = paths;
                return true;
            },
            (attempt, done) => done(attempt()),
            outcomes.Add);

        Assert.Equal([@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"], set);
        Assert.Equal(ShellOutcome.Done, Assert.Single(outcomes));
    }

    [Fact]
    public void Copy_of_a_line_that_lost_a_track_copies_the_other()
    {
        IReadOnlyList<string>? set = null;

        ShellFiles.Copy(
            Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"),
            path => path.EndsWith("_system.mp3", StringComparison.Ordinal),
            paths =>
            {
                set = paths;
                return true;
            },
            (attempt, done) => done(attempt()),
            _ => { });

        Assert.Equal([@"C:\clips\a_system.mp3"], set);
    }

    [Fact]
    public void Copy_with_nothing_left_says_gone_and_never_touches_the_clipboard()
    {
        var reached = false;
        var outcomes = new List<ShellOutcome>();

        ShellFiles.Copy(
            Line(@"C:\clips\a.mp3"),
            _ => false,
            _ => true,
            (_, _) => reached = true,
            outcomes.Add);

        Assert.False(reached);
        Assert.Equal(ShellOutcome.Gone, Assert.Single(outcomes));
    }

    [Fact]
    public void With_no_clipboard_the_attempt_never_runs_on_the_way_of_the_app()
    {
        var tried = false;
        var outcomes = new List<ShellOutcome>();
        var options = new AppOptions { NoClipboard = true };

        ShellFiles.Copy(
            Line(typeof(ShellFilesTests).Assembly.Location),
            File.Exists,
            _ => tried = true,
            (attempt, done) => SettingsMapping.CopyToClipboard(options, attempt, (_, _) => { }, done),
            outcomes.Add);

        Assert.False(tried);
        Assert.Equal(ShellOutcome.Failed, Assert.Single(outcomes));
    }

    [Fact]
    public void A_held_clipboard_is_retried_through_the_loop_as_a_clip_is()
    {
        var attempts = 0;
        var outcomes = new List<ShellOutcome>();
        var later = new Queue<Action>();

        ShellFiles.Copy(
            Line(@"C:\clips\a.mp3"),
            Everything,
            _ => ++attempts == 3,
            (attempt, done) => ClipboardFiles.CopyWithRetries(attempt, (_, step) => later.Enqueue(step), done),
            outcomes.Add);

        while (later.TryDequeue(out var step))
        {
            step();
        }

        Assert.Equal(3, attempts);
        Assert.Equal(ShellOutcome.Done, Assert.Single(outcomes));
    }

    [Fact]
    public void An_attempt_that_throws_in_a_retry_is_a_failed_attempt_and_stays_out_of_the_log()
    {
        var journal = new List<string>();
        var outcomes = new List<ShellOutcome>();
        var later = new Queue<Action>();

        ShellFiles.Copy(
            Line(CanaryPath),
            Everything,
            _ => throw new IOException(CanaryPath),
            (attempt, done) => ClipboardFiles.CopyWithRetries(attempt, (_, step) => later.Enqueue(step), done, attempts: 2),
            outcomes.Add,
            journal.Add);

        while (later.TryDequeue(out var step))
        {
            step();
        }

        Assert.Equal(ShellOutcome.Failed, Assert.Single(outcomes));
        Assert.Equal(2, journal.Count);
        Assert.DoesNotContain("Canary", string.Join("\n", journal));
    }

    [Fact]
    public void A_way_to_the_clipboard_that_throws_answers_once_and_stays_out_of_the_log()
    {
        var journal = new List<string>();
        var outcomes = new List<ShellOutcome>();

        ShellFiles.Copy(
            Line(CanaryPath),
            Everything,
            _ => true,
            (_, done) =>
            {
                done(true);
                throw new IOException(CanaryPath);
            },
            outcomes.Add,
            journal.Add);

        Assert.Equal(ShellOutcome.Done, Assert.Single(outcomes));
        Assert.Contains("IOException", Assert.Single(journal));
        Assert.DoesNotContain("Canary", string.Join("\n", journal));
    }

    [Fact]
    public void The_drop_list_of_a_line_holds_its_two_tracks_side_by_side()
    {
        var bytes = ClipboardFiles.BuildDropFiles(ShellFiles.StillThere(Line(@"C:\a_system.mp3", @"C:\a_mic.mp3"), Everything));

        var text = System.Text.Encoding.Unicode.GetString(bytes, 20, bytes.Length - 20);
        Assert.Equal("C:\\a_system.mp3\0C:\\a_mic.mp3\0\0", text);
    }

    private sealed class ApartmentShell : IFolderShell
    {
        public ApartmentState Apartment { get; private set; } = ApartmentState.Unknown;

        public int Thread { get; private set; }

        public int ShowSelected(string folder, IReadOnlyList<string> files)
        {
            Apartment = System.Threading.Thread.CurrentThread.GetApartmentState();
            Thread = Environment.CurrentManagedThreadId;
            return 0;
        }
    }
}
