using System.Collections.Concurrent;
using System.IO;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// Renaming a clip and sending it to the Recycle Bin, from the real panel, on made-up clips beside a real index, with a fake bin,
/// a fake player and a loop the test turns by hand. Each answer comes on the loop, and a new reading follows it.
/// </summary>
public sealed class WindowLibraryClipFilesTests : IDisposable
{
    private static readonly DateTime LongAgo = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly ConcurrentQueue<string> _journal = new();
    private readonly FakeBin _bin = new();
    private readonly List<string> _told = [];
    private readonly List<NotingPlayer> _players = [];
    private readonly WindowLibrary _panel;

    public WindowLibraryClipFilesTests()
    {
        Directory.CreateDirectory(Clips);
        _panel = new WindowLibrary(
            _rig.Screen,
            new LibraryStore(Path.Combine(_root, "settings.json"), _journal.Enqueue),
            () => new ClipScanRequest { OutputFolder = Clips, RescueFolder = Path.Combine(_root, "rescued") },
            _loop.Enqueue,
            new NoShell(),
            File.Exists,
            _ => true,
            (attempt, done) => done(attempt()),
            _journal.Enqueue,
            player: () =>
            {
                var player = new NotingPlayer();
                _players.Add(player);
                return player;
            },
            recycleBin: _bin);
        _panel.RenameAnswered = (line, outcome) => _told.Add($"rename {Path.GetFileName(line.Main.Path)}: {outcome}");
        _panel.DeleteAnswered = (line, outcome) => _told.Add($"delete {Path.GetFileName(line.Main.Path)}: {outcome}");
        _rig.Screen.SetLibrary(_panel);
    }

    public void Dispose()
    {
        _panel.Close();
        _panel.Preview?.WaitForEnd(5000);
        _rig.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class NoShell : IFolderShell
    {
        public int ShowSelected(string folder, IReadOnlyList<string> files) => 0;
    }

    private string Clips => Path.Combine(_root, "clips");

    private string At(string name) => Path.Combine(Clips, name);

    private void Clip(string name)
    {
        File.WriteAllBytes(At(name), [1, 2, 3, 4, 5]);
        File.SetLastWriteTimeUtc(At(name), LongAgo);
    }

    private void Turn(Func<bool> until)
    {
        var end = Environment.TickCount64 + 20_000;
        while (!until())
        {
            if (_loop.TryDequeue(out var work))
            {
                work();
            }
            else
            {
                Assert.True(Environment.TickCount64 < end, "the loop never got there");
                Thread.Sleep(1);
            }
        }
    }

    private void Settle() => Turn(() => !_panel.IsWorking && _loop.IsEmpty);

    private void Look()
    {
        _panel.ReadAgain();
        Settle();
    }

    private ClipGroup Line(string name) => _panel.Lines!.Lines.Single(line => Path.GetFileName(line.Main.Path) == name);

    private string[] Shown() => [.. _panel.Lines!.Lines.Select(line => Path.GetFileName(line.Main.Path)).Order(StringComparer.Ordinal)];

    private string[] OnDisk() => [.. Directory.GetFiles(Clips).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    [Fact]
    public void A_renamed_clip_keeps_its_star_and_the_reading_after_the_answer_shows_its_new_name()
    {
        Clip("dogs.mp3");
        Look();
        _panel.SetFavorite(Line("dogs.mp3"), true);
        Settle();

        _panel.Rename(Line("dogs.mp3"), "Good dogs");
        Settle();

        Assert.Equal(["rename dogs.mp3: Done"], _told);
        Assert.Equal(["Good dogs.mp3"], OnDisk());
        Assert.Equal(["Good dogs.mp3"], Shown());
        Assert.True(_panel.LabelsOf(Line("Good dogs.mp3")).Favorite);
        Assert.Equal(0, _panel.ActionsWaiting);
    }

    [Fact]
    public void Both_files_of_a_clip_in_two_files_are_renamed()
    {
        Clip("raid_system.mp3");
        Clip("raid_mic.mp3");
        Look();
        var line = Assert.Single(_panel.Lines!.Lines);
        Assert.Equal(2, line.Files.Count);

        _panel.Rename(line, "Boss");
        Settle();

        Assert.Equal(["Boss_mic.mp3", "Boss_system.mp3"], OnDisk());
        Assert.Single(_told, told => told.EndsWith(": Done", StringComparison.Ordinal));
    }

    [Fact]
    public void A_name_taken_on_the_disk_since_the_listing_is_refused_and_nothing_moves()
    {
        Clip("dogs.mp3");
        Look();
        Clip("cats.mp3");

        _panel.Rename(Line("dogs.mp3"), "Cats");
        Settle();

        Assert.Equal(["rename dogs.mp3: Taken"], _told);
        Assert.Equal(["cats.mp3", "dogs.mp3"], OnDisk());
    }

    [Fact]
    public void A_clip_gone_since_the_listing_is_gone()
    {
        Clip("dogs.mp3");
        Look();
        var line = Line("dogs.mp3");
        File.Delete(At("dogs.mp3"));

        _panel.Rename(line, "new");
        Settle();
        _panel.Delete(line);
        Settle();

        Assert.Equal(["rename dogs.mp3: Gone", "delete dogs.mp3: Gone"], _told);
        Assert.Empty(_bin.Calls);
        Assert.Empty(_panel.Lines!.Lines);
    }

    [Fact]
    public void What_plays_of_the_clip_lets_go_of_its_file_before_the_renaming()
    {
        Clip("dogs.mp3");
        Look();
        _panel.Play(Line("dogs.mp3"));
        Turn(() => _rig.Screen.Preview.State == PreviewState.Playing);

        _panel.Rename(Line("dogs.mp3"), "Good dogs");
        Settle();

        Assert.Equal(["rename dogs.mp3: Done"], _told);
        var player = Assert.Single(_players);

        // The file was still under its old name when the player closed it: the close came first.
        Assert.Equal([true], player.ClosedWhileThere);
    }

    [Fact]
    public void What_plays_of_the_clip_lets_go_of_its_file_before_the_bin_takes_it()
    {
        Clip("dogs.mp3");
        Look();
        _panel.Play(Line("dogs.mp3"));
        Turn(() => _rig.Screen.Preview.State == PreviewState.Playing);
        _bin.Answer = RecycleOutcome.Recycled;
        _bin.BeforeAnswer = () => _bin.ClosedFirst = _players.Single().ClosedWhileThere.Count == 1;

        _panel.Delete(Line("dogs.mp3"));
        Settle();

        Assert.Equal(["delete dogs.mp3: Recycled"], _told);
        Assert.True(_bin.ClosedFirst);
    }

    [Fact]
    public void Another_clip_that_plays_goes_on()
    {
        Clip("dogs.mp3");
        Clip("cats.mp3");
        Look();
        _panel.Play(Line("cats.mp3"));
        Turn(() => _rig.Screen.Preview.State == PreviewState.Playing);

        _panel.Rename(Line("dogs.mp3"), "Good dogs");
        Settle();

        Assert.Equal(["rename dogs.mp3: Done"], _told);
        Assert.Empty(_players.Single().ClosedWhileThere);
        Assert.Equal(PreviewState.Playing, _rig.Screen.Preview.State);
    }

    [Theory]
    [InlineData(RecycleOutcome.Recycled, ClipDeleteOutcome.Recycled)]
    [InlineData(RecycleOutcome.CannotRecycle, ClipDeleteOutcome.CannotRecycle)]
    [InlineData(RecycleOutcome.Failed, ClipDeleteOutcome.Failed)]
    public void Each_answer_of_the_bin_reaches_the_screen_and_a_reading_follows(RecycleOutcome answer, ClipDeleteOutcome told)
    {
        Clip("dogs.mp3");
        Look();
        _bin.Answer = answer;
        _bin.TakesFiles = answer == RecycleOutcome.Recycled;

        _panel.Delete(Line("dogs.mp3"));
        Settle();

        Assert.Equal([$"delete dogs.mp3: {told}"], _told);
        Assert.Equal([At("dogs.mp3")], Assert.Single(_bin.Calls));
        string[] left = answer == RecycleOutcome.Recycled ? [] : ["dogs.mp3"];
        Assert.Equal(left, Shown());
    }

    [Fact]
    public void A_clip_the_bin_can_not_take_stays_with_its_index_untouched()
    {
        Clip("dogs.mp3");
        Look();
        _panel.SetFavorite(Line("dogs.mp3"), true);
        Settle();
        var index = File.ReadAllText(Path.Combine(_root, "library.json"));
        _bin.Answer = RecycleOutcome.CannotRecycle;

        _panel.Delete(Line("dogs.mp3"));
        Settle();

        Assert.Equal(["dogs.mp3"], OnDisk());
        Assert.Equal(index, File.ReadAllText(Path.Combine(_root, "library.json")));
        Assert.True(_panel.LabelsOf(Line("dogs.mp3")).Favorite);
    }

    [Fact]
    public void Both_files_of_a_clip_go_to_the_bin_in_one_call()
    {
        Clip("raid_system.mp3");
        Clip("raid_mic.mp3");
        Look();
        _bin.Answer = RecycleOutcome.Recycled;

        _panel.Delete(Assert.Single(_panel.Lines!.Lines));
        Settle();

        var call = Assert.Single(_bin.Calls);
        Assert.Equal([At("raid_mic.mp3"), At("raid_system.mp3")], call.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_answer_reaches_a_closed_window()
    {
        Clip("dogs.mp3");
        Look();
        _bin.Answer = RecycleOutcome.Recycled;

        _panel.Delete(Line("dogs.mp3"));
        _panel.Close();
        Turn(() => !_panel.IsWorking);
        while (_loop.TryDequeue(out var work))
        {
            work();
        }

        Assert.Empty(_told);
        Assert.DoesNotContain(_journal, line => line.Contains("dogs", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A bin that answers as told. It takes the files away only when asked to, so a reading after it sees them gone.</summary>
    private sealed class FakeBin : IRecycleBin
    {
        public ConcurrentQueue<IReadOnlyList<string>> Calls { get; } = new();

        public RecycleOutcome Answer { get; set; } = RecycleOutcome.CannotRecycle;

        public bool TakesFiles { get; set; } = true;

        public Action? BeforeAnswer { get; set; }

        public bool ClosedFirst { get; set; }

        public RecycleOutcome Recycle(IReadOnlyList<string> paths)
        {
            Calls.Enqueue([.. paths]);
            BeforeAnswer?.Invoke();
            if (Answer == RecycleOutcome.Recycled && TakesFiles)
            {
                // Out of the folder of the clips, as the bin would; the folder of the test goes with the rest.
                foreach (var path in paths)
                {
                    File.Move(path, path + ".recycled-" + Guid.NewGuid().ToString("N")[..6] + ".bin");
                }
            }

            return Answer;
        }
    }

    /// <summary>Plays nothing; notes, at each close, whether the file it held was still under its name.</summary>
    private sealed class NotingPlayer : IPreviewPlayer
    {
        private readonly ConcurrentDictionary<string, string> _paths = new();

        public ConcurrentQueue<bool> ClosedWhileThere { get; } = new();

        public int Open(string path, string track)
        {
            _paths[track] = path;
            return 0;
        }

        public int Play(string track) => 0;

        public int Stop(string track) => 0;

        public int Close(string track)
        {
            if (_paths.TryRemove(track, out var path))
            {
                ClosedWhileThere.Enqueue(File.Exists(path));
            }

            return 0;
        }

        public int StateOf(string track, out PreviewTrackState state)
        {
            state = PreviewTrackState.Playing;
            return 0;
        }

        public void Wait(WaitHandle wake, int milliseconds) => wake.WaitOne(Math.Min(milliseconds, 20));
    }
}
