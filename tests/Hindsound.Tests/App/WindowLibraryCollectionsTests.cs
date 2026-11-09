using System.Collections.Concurrent;
using System.IO;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// The star and the collections, from the real panel to a real index beside a made-up settings file, on made-up clips, with a
/// loop the test turns by hand. Every action looks first, off the loop, and its answer comes back on the loop.
/// </summary>
public sealed class WindowLibraryCollectionsTests : IDisposable
{
    private const string Canary = "Collection-Canary-7F3";
    private static readonly DateTime LongAgo = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly byte[] Sound = [1, 2, 3, 4, 5];

    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly ConcurrentQueue<string> _journal = new();
    private readonly WindowLibrary _panel;
    private readonly Dictionary<string, DateTime> _written = new(StringComparer.OrdinalIgnoreCase);
    private Func<ClipScanRequest>? _foldersInstead;

    public WindowLibraryCollectionsTests()
    {
        Directory.CreateDirectory(Clips);
        _panel = new WindowLibrary(
            _rig.Screen,
            new LibraryStore(_temp.Combine("settings.json"), _journal.Enqueue),
            () => _foldersInstead is { } instead ? instead() : new ClipScanRequest { OutputFolder = Clips, RescueFolder = _temp.Combine("rescued") },
            _loop.Enqueue,
            new NoShell(),
            File.Exists,
            _ => true,
            (attempt, done) => done(attempt()),
            _journal.Enqueue);
        Screen.SetLibrary(_panel);
    }

    public void Dispose()
    {
        _panel.Close();
        _rig.Dispose();
        _temp.Dispose();
    }

    private sealed class NoShell : IFolderShell
    {
        public int ShowSelected(string folder, IReadOnlyList<string> files) => 0;
    }

    private string Clips => _temp.Combine("clips");

    private string IndexFile => _temp.Combine("library.json");

    private SettingsScreen Screen => _rig.Screen;

    private string Clip(string name, DateTime? written = null)
    {
        var path = Path.Combine(Clips, name);
        File.WriteAllBytes(path, Sound);
        File.SetLastWriteTimeUtc(path, written ?? LongAgo);
        _written[name] = written ?? LongAgo;
        return path;
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
            else if (Environment.TickCount64 > end)
            {
                Assert.Fail("the loop never got there");
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private void Settle() => Turn(() => !_panel.IsWorking && _loop.IsEmpty);

    private void OpenPage()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.ShowSection(Section.Library);
        Settle();
    }

    private ClipGroup Line(string name) => _panel.Lines!.Lines.Single(line => Path.GetFileName(line.Main.Path) == name);

    private string Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return string.Join("\n", _rig.Canvas.Drawn);
    }

    private string CollectionId(string name) => _panel.Index!.Collections.Single(collection => collection.Name == name).Id;

    private void AssertClipsUntouched(params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(Clips, name);
            Assert.Equal(Sound, File.ReadAllBytes(path));
            Assert.Equal(_written[name], File.GetLastWriteTimeUtc(path));
        }
    }

    [Fact]
    public void A_star_is_written_beside_the_settings_and_the_reading_after_it_shows_it()
    {
        Clip("dogs.mp3");
        OpenPage();
        Assert.False(File.Exists(IndexFile));

        _panel.SetFavorite(Line("dogs.mp3"), true);
        Settle();

        Assert.True(File.Exists(IndexFile));
        Assert.True(_panel.LabelsOf(Line("dogs.mp3")).Favorite);
        Assert.Equal(LibraryStatus.Loaded, _panel.IndexStatus);
        AssertClipsUntouched("dogs.mp3");

        _panel.SetFavorite(Line("dogs.mp3"), false);
        Settle();

        Assert.False(_panel.LabelsOf(Line("dogs.mp3")).Favorite);
        AssertClipsUntouched("dogs.mp3");
    }

    [Fact]
    public void A_collection_is_made_ticked_untick_renamed_and_deleted_and_the_clips_never_move()
    {
        Clip("dogs.mp3");
        Clip("pets.wav", LongAgo.AddDays(-1));
        OpenPage();
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));

        _panel.CreateCollection("pets");
        Settle();
        var id = CollectionId("pets");
        Assert.Contains("pets", Painted(), StringComparison.Ordinal);

        _panel.SetIn(Line("dogs.mp3"), id, true);
        Settle();
        Assert.Equal([id], _panel.LabelsOf(Line("dogs.mp3")).CollectionIds);
        Assert.Empty(_panel.LabelsOf(Line("pets.wav")).CollectionIds);

        _panel.SetIn(Line("dogs.mp3"), id, false);
        Settle();
        Assert.Empty(_panel.LabelsOf(Line("dogs.mp3")).CollectionIds);

        _panel.SetIn(Line("dogs.mp3"), id, true);
        _panel.RenameCollection(id, "Sam");
        Settle();
        Assert.Equal("Sam", Assert.Single(_panel.Index!.Collections).Name);
        Assert.Equal([id], _panel.LabelsOf(Line("dogs.mp3")).CollectionIds);

        _panel.DeleteCollection(id);
        Settle();
        Assert.Empty(_panel.Index!.Collections);
        Assert.Empty(_panel.LabelsOf(Line("dogs.mp3")).CollectionIds);
        Assert.Equal(["dogs.mp3", "pets.wav"], _panel.Lines!.Lines.Select(line => Path.GetFileName(line.Main.Path)));
        AssertClipsUntouched("dogs.mp3", "pets.wav");
    }

    [Fact]
    public void A_refused_name_is_said_after_the_reading_it_brought_and_stays_on_screen()
    {
        Clip("dogs.mp3");
        OpenPage();
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        _panel.CreateCollection("pets");
        Settle();

        _panel.CreateCollection("PETS");
        Settle();

        // In the other order, the reading would have taken the sentence away.
        Assert.Contains(ChipLine.OutcomeMessage(LibraryOutcome.NameTaken)!, Painted(), StringComparison.Ordinal);
        Assert.Single(_panel.Index!.Collections);
    }

    [Fact]
    public void A_clip_renamed_between_the_reading_and_the_tick_is_not_written_and_the_screen_shows_the_fresh_look()
    {
        var path = Clip("dogs.mp3");
        OpenPage();
        Screen.SelectClip(path);
        _panel.CreateCollection("pets");
        Settle();
        var before = File.ReadAllText(IndexFile);
        var stale = Line("dogs.mp3");

        File.Move(path, Path.Combine(Clips, "Alex.mp3"));
        _panel.SetIn(stale, CollectionId("pets"), true);
        _panel.SetFavorite(stale, true);
        Settle();

        Assert.Equal(before, File.ReadAllText(IndexFile));
        Assert.Equal(["Alex.mp3"], _panel.Lines!.Lines.Select(line => Path.GetFileName(line.Main.Path)));
        Assert.False(_panel.LabelsOf(Line("Alex.mp3")).Any);
    }

    [Fact]
    public void A_locked_file_writes_nothing_says_so_in_amber_and_keeps_the_clips_listed()
    {
        Clip("dogs.mp3");
        File.WriteAllText(IndexFile, """{"version":1,"collections":[{"id":"c1","name":"pets"}]}""");
        OpenPage();
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        var line = Line("dogs.mp3");

        using (new FileStream(IndexFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _panel.SetFavorite(line, true);
            _panel.SetIn(line, "c1", true);
            Settle();

            Assert.Equal(LibraryStatus.Unreadable, _panel.IndexStatus);
            Assert.Contains(ChipLine.ReadOnlyMessage(LibraryStatus.Unreadable)!, Painted(), StringComparison.Ordinal);
            Assert.Equal(["dogs.mp3"], _panel.Lines!.Lines.Select(l => Path.GetFileName(l.Main.Path)));
        }

        Assert.Equal("""{"version":1,"collections":[{"id":"c1","name":"pets"}]}""", File.ReadAllText(IndexFile));
        AssertClipsUntouched("dogs.mp3");
    }

    [Fact]
    public void A_file_from_a_newer_version_is_left_as_it_is_and_says_why()
    {
        Clip("dogs.mp3");
        const string newer = """{"version":99,"collections":[]}""";
        File.WriteAllText(IndexFile, newer);
        OpenPage();
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));

        _panel.SetFavorite(Line("dogs.mp3"), true);
        _panel.CreateCollection("pets");
        Settle();

        Assert.Equal(newer, File.ReadAllText(IndexFile));
        Assert.Equal(LibraryStatus.TooNew, _panel.IndexStatus);
        Assert.Contains(ChipLine.ReadOnlyMessage(LibraryStatus.TooNew)!, Painted(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_damaged_file_shows_its_sentence_and_Start_over_puts_it_aside_and_starts_empty()
    {
        Clip("dogs.mp3");
        File.WriteAllText(IndexFile, "{ not json");
        OpenPage();

        // A damaged file still gives an index, empty: without one there would be no sentence and no Start over.
        Assert.NotNull(_panel.Index);
        Assert.Equal(LibraryStatus.Corrupt, _panel.IndexStatus);
        var painted = Painted();
        // Beside Start over, the sentence may take two lines.
        Assert.Contains("Your collections file has an error.", painted, StringComparison.Ordinal);
        Assert.Contains(SettingsScreen.StartOverLabel, painted, StringComparison.Ordinal);

        _panel.CreateCollection("pets");
        Settle();
        Assert.Equal("{ not json", File.ReadAllText(IndexFile));

        _panel.StartOver();
        Settle();

        Assert.Equal(LibraryStatus.Loaded, _panel.IndexStatus);
        Assert.Empty(_panel.Index!.Collections);
        Assert.Contains(Directory.GetFiles(_temp.Path, "library.json*"), file => file != IndexFile && File.ReadAllText(file) == "{ not json");
        AssertClipsUntouched("dogs.mp3");
    }

    [Fact]
    public void An_action_that_cannot_be_asked_still_gets_its_answer_on_the_loop()
    {
        Clip("dogs.mp3");
        OpenPage();
        Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        _foldersInstead = () =>
        {
            // Once: the rows ask where the rescue folder is at each paint.
            _foldersInstead = null;
            throw new IOException(Path.Combine(Clips, Canary));
        };

        _panel.CreateCollection("pets");
        Assert.DoesNotContain(ChipLine.OutcomeMessage(LibraryOutcome.NotWritten)!, Painted(), StringComparison.Ordinal);
        Settle();

        Assert.Contains(ChipLine.OutcomeMessage(LibraryOutcome.NotWritten)!, Painted(), StringComparison.Ordinal);
        Assert.False(File.Exists(IndexFile));
        Assert.DoesNotContain(_journal, line => line.Contains(Canary, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_journal, line => line.Contains(nameof(IOException), StringComparison.Ordinal));
    }

    [Fact]
    public void The_name_of_a_collection_never_reaches_the_log_whatever_becomes_of_it()
    {
        Clip(Canary + ".mp3");
        OpenPage();
        var line = Line(Canary + ".mp3");
        _panel.CreateCollection(Canary);
        Settle();
        var id = CollectionId(Canary);
        _panel.SetIn(line, id, true);
        _panel.RenameCollection(id, Canary + " again");
        _panel.CreateCollection(Canary + " again");
        Settle();

        using (new FileStream(IndexFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _panel.RenameCollection(id, Canary);
            _panel.SetFavorite(line, true);
            Settle();
        }

        File.WriteAllText(IndexFile, "{ \"collections\": [ \"" + Canary);
        _panel.DeleteCollection(id);
        _panel.StartOver();
        Settle();

        Assert.NotEmpty(_journal);
        Assert.DoesNotContain(_journal, logged => logged.Contains("Canary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_window_closed_during_an_action_still_has_it_written_and_hears_nothing_back()
    {
        Clip("dogs.mp3");
        OpenPage();
        var line = Line("dogs.mp3");
        var ended = false;

        _panel.SetFavorite(line, true);
        _panel.Close(() => ended = true);
        Turn(() => ended && _loop.IsEmpty);

        Assert.Contains("\"favorite\"", File.ReadAllText(IndexFile), StringComparison.OrdinalIgnoreCase);
        Assert.False(_panel.LabelsOf(line).Favorite);
        Assert.Equal(LibraryStatus.Missing, _panel.IndexStatus);

        // Asked after the close: nothing leaves.
        _panel.CreateCollection("pets");
        Assert.True(_loop.IsEmpty);
        Assert.False(_panel.IsWorking);
        Assert.DoesNotContain("pets", File.ReadAllText(IndexFile), StringComparison.Ordinal);
    }

    [Fact]
    public void A_starred_clip_older_than_the_cap_is_still_a_line_and_an_unstarred_one_is_not()
    {
        var newest = LongAgo.AddDays(1);
        for (var i = 0; i < ClipScanner.MaxLines; i++)
        {
            Clip($"pets-{i:D4}.wav", newest.AddMinutes(-i));
        }

        Clip("Sam-old.wav", LongAgo.AddDays(-2));
        Clip("Alex-older.wav", LongAgo.AddDays(-3));
        OpenPage();
        Assert.DoesNotContain(_panel.Lines!.Lines, l => Path.GetFileName(l.Main.Path) == "Sam-old.wav");

        _panel.SetFavorite(new ClipGroup([new ClipFile(Path.Combine(Clips, "Alex-older.wav"), Sound.Length, LongAgo.AddDays(-3))]), true);
        Settle();

        var names = _panel.Lines!.Lines.Select(l => Path.GetFileName(l.Main.Path)).ToList();
        Assert.Contains("Alex-older.wav", names);
        Assert.DoesNotContain("Sam-old.wav", names);
        Assert.Equal(ClipScanner.MaxLines + 1, names.Count);
    }
}
