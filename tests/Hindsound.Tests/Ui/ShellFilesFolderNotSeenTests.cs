using System.IO;
using Hindsound.Library;
using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>A clip missing from a folder that can't be seen, a drive unplugged, is not said to be moved or deleted.</summary>
public sealed class ShellFilesFolderNotSeenTests
{
    private const string Canary = "Collection-Canary-7F3";
    private static readonly string CanaryPath = $@"C:\{Canary}\{Canary}.mp3";

    private static ClipGroup Line(string path) =>
        new([new ClipFile(path, 10, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc))]);

    private sealed class FakeShell : IFolderShell
    {
        public int Calls { get; private set; }

        public int ShowSelected(string folder, IReadOnlyList<string> files)
        {
            Calls++;
            return 0;
        }
    }

    private static Thread Started(Action work, Action<Exception> onError)
    {
        var thread = new Thread(() => work());
        thread.Start();
        return thread;
    }

    [Fact]
    public void Show_in_folder_with_the_folder_not_seen_fails_instead_of_saying_gone()
    {
        var shell = new FakeShell();

        var outcome = ShellFiles.ShowInFolder(Line(@"E:\clips\a.mp3"), shell, _ => false, folderExists: _ => false);

        Assert.Equal(ShellOutcome.Failed, outcome);
        Assert.Equal(0, shell.Calls);
    }

    [Fact]
    public void Show_in_folder_with_the_folder_seen_and_the_file_missing_says_gone()
    {
        var checkedFolders = new List<string>();

        var outcome = ShellFiles.ShowInFolder(Line(@"E:\clips\a.mp3"), new FakeShell(), _ => false, folderExists: folder =>
        {
            checkedFolders.Add(folder);
            return true;
        });

        Assert.Equal(ShellOutcome.Gone, outcome);
        Assert.Equal([@"E:\clips"], checkedFolders);
    }

    [Fact]
    public void A_folder_check_that_throws_fails_and_stays_out_of_the_log()
    {
        var journal = new List<string>();

        var outcome = ShellFiles.ShowInFolder(Line(CanaryPath), new FakeShell(), _ => false, journal.Add, _ => throw new IOException(CanaryPath));

        Assert.Equal(ShellOutcome.Failed, outcome);
        Assert.Contains("IOException", Assert.Single(journal));
        Assert.DoesNotContain("Canary", string.Join("\n", journal));
    }

    [Fact]
    public void The_folder_is_not_looked_at_while_a_file_is_found()
    {
        var shell = new FakeShell();

        var outcome = ShellFiles.ShowInFolder(Line(@"E:\clips\a.mp3"), shell, _ => true, folderExists: _ => throw new InvalidOperationException());

        Assert.Equal(ShellOutcome.Done, outcome);
        Assert.Equal(1, shell.Calls);
    }

    private static ShellOutcome ExpectedWhen(bool folderSeen) => folderSeen ? ShellOutcome.Gone : ShellOutcome.Failed;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copy_with_no_file_left_asks_whether_the_folder_is_seen(bool folderSeen)
    {
        var expected = ExpectedWhen(folderSeen);
        var answers = new List<ShellOutcome>();
        var clipboardTouched = false;

        ShellFiles.Copy(
            Line(@"E:\clips\a.mp3"),
            _ => false,
            _ => true,
            (attempt, _) => clipboardTouched = true,
            answers.Add,
            folderExists: _ => folderSeen);

        Assert.Equal(expected, Assert.Single(answers));
        Assert.False(clipboardTouched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copy_off_the_loop_hands_the_check_on(bool folderSeen)
    {
        var expected = ExpectedWhen(folderSeen);
        var answers = new List<ShellOutcome>();

        var thread = ShellFiles.CopyOffLoop(
            Line(@"E:\clips\a.mp3"),
            _ => false,
            work => work(),
            _ => true,
            (_, _) => { },
            answers.Add,
            start: Started,
            folderExists: _ => folderSeen);
        thread!.Join();

        Assert.Equal(expected, Assert.Single(answers));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Show_in_folder_off_the_loop_hands_the_check_on(bool folderSeen)
    {
        var expected = ExpectedWhen(folderSeen);
        var answers = new List<ShellOutcome>();

        var thread = ShellFiles.ShowInFolderOffLoop(
            Line(@"E:\clips\hindsound-no-such-file.mp3"),
            new FakeShell(),
            work => work(),
            answers.Add,
            start: Started,
            folderExists: _ => folderSeen);
        thread!.Join();

        Assert.Equal(expected, Assert.Single(answers));
    }
}
