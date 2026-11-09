using System.IO;
using Hindsound.Library;
using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>The gestures of a line that leave the loop: where each step runs, and what comes of a thread that fails or can't start.</summary>
public sealed class ShellFilesOffLoopTests
{
    private const string Canary = "Collection-Canary-7F3";
    private static readonly string CanaryPath = $@"C:\{Canary}\{Canary}.mp3";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static ClipGroup Line(params string[] paths) =>
        new(paths.Select(path => new ClipFile(path, 10, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc))).ToList());

    private sealed class Shell : IFolderShell
    {
        public int ShowSelected(string folder, IReadOnlyList<string> files) => 0;
    }

    private static Thread NoThread(Action work, Action<Exception> failed) =>
        throw new OutOfMemoryException($"{CanaryPath} no thread") { HResult = unchecked((int)0x8007000E) };

    [Fact]
    public void Copy_looks_at_the_files_off_the_loop_and_touches_the_clipboard_only_on_the_loop()
    {
        var loop = Environment.CurrentManagedThreadId;
        var lookedOn = 0;
        var clipboardOn = 0;
        var posted = new List<Action>();
        var outcomes = new List<ShellOutcome>();

        var thread = ShellFiles.CopyOffLoop(
            Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"),
            path =>
            {
                lookedOn = Environment.CurrentManagedThreadId;
                return true;
            },
            posted.Add,
            _ => true,
            (attempt, done) =>
            {
                clipboardOn = Environment.CurrentManagedThreadId;
                done(attempt());
            },
            outcomes.Add);

        Assert.NotNull(thread);
        Assert.True(thread.Join(Patience));
        Assert.NotEqual(loop, lookedOn);
        Assert.Equal(0, clipboardOn);
        Assert.Empty(outcomes);

        Assert.Single(posted)();
        Assert.Equal(loop, clipboardOn);
        Assert.NotEqual(loop, lookedOn);
        Assert.Equal(ShellOutcome.Done, Assert.Single(outcomes));
    }

    [Fact]
    public void Copy_off_the_loop_hands_over_what_is_left_of_the_line()
    {
        IReadOnlyList<string>? set = null;
        var posted = new List<Action>();

        var thread = ShellFiles.CopyOffLoop(
            Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"),
            path => path.EndsWith("_mic.mp3", StringComparison.Ordinal),
            posted.Add,
            paths =>
            {
                set = paths;
                return true;
            },
            (attempt, done) => done(attempt()),
            _ => { });

        Assert.True(thread!.Join(Patience));
        Assert.Single(posted)();
        Assert.Equal([@"C:\clips\a_mic.mp3"], set);
    }

    [Fact]
    public void Copy_off_the_loop_with_nothing_left_says_gone_and_never_touches_the_clipboard()
    {
        var reached = false;
        var posted = new List<Action>();
        var outcomes = new List<ShellOutcome>();

        var thread = ShellFiles.CopyOffLoop(Line(@"C:\clips\a.mp3"), _ => false, posted.Add, _ => true, (_, _) => reached = true, outcomes.Add);

        Assert.True(thread!.Join(Patience));
        Assert.Single(posted)();
        Assert.False(reached);
        Assert.Equal(ShellOutcome.Gone, Assert.Single(outcomes));
    }

    [Fact]
    public void A_thread_that_can_not_start_answers_failed_through_the_post_for_show_in_folder_and_keeps_the_path_out_of_the_log()
    {
        var journal = new List<string>();
        var posted = new List<Action>();
        var outcomes = new List<ShellOutcome>();

        var thread = ShellFiles.ShowInFolderOffLoop(Line(CanaryPath), new Shell(), posted.Add, outcomes.Add, journal.Add, NoThread);

        Assert.Null(thread);
        Assert.Empty(outcomes);
        Assert.Single(posted)();
        Assert.Equal(ShellOutcome.Failed, Assert.Single(outcomes));
        var line = Assert.Single(journal);
        Assert.Contains("OutOfMemoryException", line);
        Assert.Contains("0x8007000E", line);
        Assert.DoesNotContain("Canary", line);
    }

    [Fact]
    public void A_thread_that_can_not_start_answers_failed_through_the_post_for_copy_and_touches_nothing()
    {
        var journal = new List<string>();
        var posted = new List<Action>();
        var outcomes = new List<ShellOutcome>();
        var looked = false;

        var thread = ShellFiles.CopyOffLoop(
            Line(CanaryPath),
            _ =>
            {
                looked = true;
                return true;
            },
            posted.Add,
            _ => true,
            (_, _) => { },
            outcomes.Add,
            journal.Add,
            NoThread);

        Assert.Null(thread);
        Assert.Single(posted)();
        Assert.Equal(ShellOutcome.Failed, Assert.Single(outcomes));
        Assert.False(looked);
        Assert.DoesNotContain("Canary", string.Join("\n", journal));
        Assert.Contains("OutOfMemoryException", Assert.Single(journal));
    }

    [Fact]
    public void A_post_that_throws_after_a_thread_that_can_not_start_is_logged_by_type_alone()
    {
        var journal = new List<string>();

        var thread = ShellFiles.ShowInFolderOffLoop(
            Line(CanaryPath),
            new Shell(),
            _ => throw new IOException(CanaryPath) { HResult = unchecked((int)0x80070020) },
            _ => { },
            journal.Add,
            NoThread);

        Assert.Null(thread);
        Assert.Equal(2, journal.Count);
        Assert.Contains("IOException", journal[1]);
        Assert.DoesNotContain("Canary", string.Join("\n", journal));
    }

    [Fact]
    public void A_post_that_throws_on_the_thread_is_logged_by_type_alone_and_ends_nothing()
    {
        var journal = new List<string>();

        var thread = ShellFiles.ShowInFolderOffLoop(
            Line(typeof(ShellFilesOffLoopTests).Assembly.Location),
            new Shell(),
            _ => throw new IOException($"{CanaryPath} is held") { HResult = unchecked((int)0x80070020) },
            _ => { },
            journal.Add);

        Assert.True(thread!.Join(Patience));
        var line = Assert.Single(journal);
        Assert.Contains("IOException", line);
        Assert.Contains("0x80070020", line);
        Assert.DoesNotContain("Canary", line);
    }

    [Fact]
    public void A_post_that_throws_on_the_thread_of_a_copy_is_logged_by_type_alone()
    {
        var journal = new List<string>();

        var thread = ShellFiles.CopyOffLoop(
            Line(@"C:\clips\a.mp3"),
            _ => true,
            _ => throw new IOException($"{CanaryPath} is held"),
            _ => true,
            (_, _) => { },
            _ => { },
            journal.Add);

        Assert.True(thread!.Join(Patience));
        var line = Assert.Single(journal);
        Assert.StartsWith("copy: failed (IOException", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Canary", line);
    }
}
