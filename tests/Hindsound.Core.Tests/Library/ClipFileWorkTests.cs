using System.Collections.Concurrent;
using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>Renaming and recycling the files of a clip, on made-up files in a folder of the test, with a fake Recycle Bin.</summary>
public sealed class ClipFileWorkTests : IDisposable
{
    private static readonly DateTime LongAgo = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> _journal = new();
    private readonly List<IReadOnlyList<string>> _labelsMoved = [];

    public ClipFileWorkTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string At(string name) => Path.Combine(_root, name);

    private ClipGroup Clip(params string[] names) =>
        new([.. names.Select(name =>
        {
            File.WriteAllBytes(At(name), [1, 2, 3]);
            File.SetLastWriteTimeUtc(At(name), LongAgo);
            return new ClipFile(At(name), 3, LongAgo);
        })]);

    private LibraryOutcome MoveLabels(IReadOnlyList<string> to)
    {
        _labelsMoved.Add(to);
        return LibraryOutcome.Done;
    }

    private ClipRenameOutcome Rename(ClipGroup clip, string name, Func<string, bool>? exists = null, Action<string, string>? move = null) =>
        ClipFileWork.Rename(clip, name, exists ?? File.Exists, move ?? ((from, to) => File.Move(from, to)), MoveLabels, _journal.Enqueue);

    private string[] Names() => [.. Directory.GetFiles(_root).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    [Fact]
    public void A_clip_in_one_file_takes_its_new_name_and_its_labels_follow()
    {
        var clip = Clip("old.mp3");

        Assert.Equal(ClipRenameOutcome.Done, Rename(clip, "Boss fight"));

        Assert.Equal(["Boss fight.mp3"], Names());
        Assert.Equal([At("Boss fight.mp3")], Assert.Single(_labelsMoved));
    }

    [Fact]
    public void Both_files_of_a_clip_in_two_files_take_the_new_name_with_their_suffix()
    {
        var clip = Clip("old_system.mp3", "old_mic.mp3");

        Assert.Equal(ClipRenameOutcome.Done, Rename(clip, "Raid"));

        Assert.Equal(["Raid_mic.mp3", "Raid_system.mp3"], Names());
        Assert.Equal([At("Raid_system.mp3"), At("Raid_mic.mp3")], Assert.Single(_labelsMoved));
    }

    [Fact]
    public void A_change_of_case_alone_renames_the_file()
    {
        var clip = Clip("boss.mp3");

        Assert.Equal(ClipRenameOutcome.Done, Rename(clip, "Boss"));

        Assert.Equal(["Boss.mp3"], Names());
    }

    [Fact]
    public void The_rule_is_asked_again_against_the_disk_and_nothing_moves()
    {
        var clip = Clip("old_system.mp3", "old_mic.mp3");
        Clip("Raid_mic.mp3");

        Assert.Equal(ClipRenameOutcome.Taken, Rename(clip, "Raid"));
        Assert.Equal(ClipRenameOutcome.Reserved, Rename(clip, "nul"));

        Assert.Equal(["Raid_mic.mp3", "old_mic.mp3", "old_system.mp3"], Names());
        Assert.Empty(_labelsMoved);
    }

    [Fact]
    public void When_the_second_file_can_not_move_the_first_takes_its_old_name_back()
    {
        var clip = Clip("old_system.mp3", "old_mic.mp3");

        // The mic name is free when the rule looks, and taken an instant later, before the files move.
        Func<string, bool> exists = path =>
        {
            var there = File.Exists(path);
            if (path == At("Raid_mic.mp3"))
            {
                File.WriteAllBytes(path, [9]);
            }

            return there;
        };

        Assert.Equal(ClipRenameOutcome.Failed, Rename(clip, "Raid", exists));

        Assert.Equal(["Raid_mic.mp3", "old_mic.mp3", "old_system.mp3"], Names());
        Assert.Equal([9], File.ReadAllBytes(At("Raid_mic.mp3")));
        Assert.Empty(_labelsMoved);
        Assert.Contains(_journal, line => line.StartsWith("clip rename: not renamed (IOException", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_that_can_not_go_back_is_said_in_the_log_without_its_name()
    {
        var clip = Clip("old_system.mp3", "old_mic.mp3");
        var moves = 0;

        // The first move goes through; then the system refuses everything.
        var outcome = Rename(clip, "Raid", move: (from, to) =>
        {
            if (++moves > 1)
            {
                throw new UnauthorizedAccessException("denied " + to);
            }

            File.Move(from, to);
        });

        Assert.Equal(ClipRenameOutcome.Failed, outcome);
        Assert.Equal(["Raid_system.mp3", "old_mic.mp3"], Names());
        Assert.Contains(_journal, line => line.StartsWith("clip rename: a file kept its new name", StringComparison.Ordinal));
        Assert.DoesNotContain(_journal, line => line.Contains("Raid", StringComparison.Ordinal) || line.Contains("old", StringComparison.Ordinal) || line.Contains(_root, StringComparison.Ordinal));
    }

    [Fact]
    public void Labels_that_can_not_follow_leave_the_files_renamed()
    {
        var clip = Clip("old.mp3");

        var outcome = ClipFileWork.Rename(clip, "new", File.Exists, (from, to) => File.Move(from, to), _ => LibraryOutcome.ReadOnly, _journal.Enqueue);

        Assert.Equal(ClipRenameOutcome.Done, outcome);
        Assert.Equal(["new.mp3"], Names());
        Assert.Contains("clip rename: the labels did not follow (ReadOnly)", _journal);
    }

    [Theory]
    [InlineData(RecycleOutcome.Recycled, ClipDeleteOutcome.Recycled)]
    [InlineData(RecycleOutcome.CannotRecycle, ClipDeleteOutcome.CannotRecycle)]
    [InlineData(RecycleOutcome.Failed, ClipDeleteOutcome.Failed)]
    public void Each_answer_of_the_bin_is_told(RecycleOutcome answer, ClipDeleteOutcome told)
    {
        var bin = new FakeBin(answer);

        Assert.Equal(told, ClipFileWork.Delete(Clip("a.mp3"), bin, _journal.Enqueue));
        Assert.Equal([At("a.mp3")], Assert.Single(bin.Calls));
    }

    [Fact]
    public void A_clip_the_bin_finds_already_gone_is_told_gone_and_logged_without_its_name()
    {
        var bin = new FakeBin(RecycleOutcome.Gone);

        Assert.Equal(ClipDeleteOutcome.Gone, ClipFileWork.Delete(Clip("secret.mp3"), bin, _journal.Enqueue));
        Assert.NotEmpty(_journal);
        Assert.DoesNotContain(_journal, line => line.Contains("secret", StringComparison.Ordinal) || line.Contains(At("secret.mp3"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Both_files_of_a_clip_go_to_the_bin_in_one_call()
    {
        var bin = new FakeBin(RecycleOutcome.Recycled);

        ClipFileWork.Delete(Clip("old_system.mp3", "old_mic.mp3"), bin, _journal.Enqueue);

        Assert.Equal([At("old_system.mp3"), At("old_mic.mp3")], Assert.Single(bin.Calls));
    }

    [Fact]
    public void A_bin_that_throws_is_a_failure_said_without_its_message()
    {
        var bin = new FakeBin(RecycleOutcome.Recycled) { Throws = true };

        Assert.Equal(ClipDeleteOutcome.Failed, ClipFileWork.Delete(Clip("secret.mp3"), bin, _journal.Enqueue));
        Assert.DoesNotContain(_journal, line => line.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void A_line_with_no_file_is_gone_and_asks_nothing()
    {
        var bin = new FakeBin(RecycleOutcome.Recycled);

        Assert.Equal(ClipDeleteOutcome.Gone, ClipFileWork.Delete(new ClipGroup([]), bin, _journal.Enqueue));
        Assert.Equal(ClipRenameOutcome.Gone, Rename(new ClipGroup([]), "new"));
        Assert.Empty(bin.Calls);
    }

    private sealed class FakeBin(RecycleOutcome answer) : IRecycleBin
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public bool Throws { get; init; }

        public RecycleOutcome Recycle(IReadOnlyList<string> paths)
        {
            Calls.Add([.. paths]);
            return Throws ? throw new IOException("secret.mp3 is locked") : answer;
        }
    }
}
