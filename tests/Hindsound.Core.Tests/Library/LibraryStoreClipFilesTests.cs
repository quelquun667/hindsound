using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>The labels of the index as the files of a clip change name or are copied, on made-up clips in a folder of the test.</summary>
public sealed class LibraryStoreClipFilesTests : IDisposable
{
    private static readonly DateTime LongAgo = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));
    private readonly LibraryStore _store;

    public LibraryStoreClipFilesTests()
    {
        Directory.CreateDirectory(Clips);
        _store = new LibraryStore(Path.Combine(_root, "settings.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Clips => Path.Combine(_root, "clips");

    private string At(string name) => Path.Combine(Clips, name);

    private ClipFile Made(string name, int size = 5)
    {
        var path = At(name);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, LongAgo);
        return new ClipFile(path, size, LongAgo);
    }

    private ClipScan Scan(params ClipGroup[] groups) => new(groups, [Clips]);

    private string Collection(string name, ClipGroup line)
    {
        var id = _store.CreateCollection(name, Scan(line), Now).CollectionId!;
        Assert.Equal(LibraryOutcome.Done, _store.SetInCollection(line, id, true, Scan(line), Now).Outcome);
        return id;
    }

    /// <summary>Moves the files on the disk, as the renaming does before it moves the labels.</summary>
    private static void Move(ClipGroup line, IReadOnlyList<string> to)
    {
        for (var i = 0; i < to.Count; i++)
        {
            File.Move(line.Files[i].Path, to[i]);
        }
    }

    private LibraryClip EntryOf(string path) =>
        Assert.Single(_store.Read().Data.Clips, entry => entry.Files.Exists(file => string.Equals(file.Path, path, StringComparison.Ordinal)));

    [Fact]
    public void The_star_and_the_collections_follow_a_clip_in_one_file()
    {
        var line = new ClipGroup([Made("old.mp3")]);
        Assert.Equal(LibraryOutcome.Done, _store.SetFavorite(line, true, Scan(line), Now).Outcome);
        var id = Collection("Raids", line);
        string[] to = [At("Boss fight.mp3")];
        Move(line, to);

        Assert.Equal(LibraryOutcome.Done, _store.MoveLabels(line, to, Scan(line), Now));

        var entry = EntryOf(to[0]);
        Assert.True(entry.Favorite);
        Assert.Equal([id], entry.Collections);
        Assert.Equal(5, entry.Files[0].Size);
        Assert.Equal(LongAgo, entry.Files[0].Written);
        Assert.DoesNotContain(_store.Read().Data.Clips, clip => clip.Files.Exists(file => file.Path == line.Main.Path));
    }

    [Fact]
    public void Both_files_of_a_clip_in_two_files_follow()
    {
        var line = new ClipGroup([Made("old_system.mp3"), Made("old_mic.mp3", size: 7)]);
        Assert.Equal(LibraryOutcome.Done, _store.SetFavorite(line, true, Scan(line), Now).Outcome);
        string[] to = [At("Raid_system.mp3"), At("Raid_mic.mp3")];
        Move(line, to);

        Assert.Equal(LibraryOutcome.Done, _store.MoveLabels(line, to, Scan(line), Now));

        var entry = Assert.Single(_store.Read().Data.Clips);
        Assert.Equal(to, entry.Files.Select(file => file.Path));
        Assert.Equal([5L, 7L], entry.Files.Select(file => file.Size));
    }

    [Fact]
    public void An_entry_that_knows_the_clip_by_its_size_and_date_follows_too()
    {
        // Starred under a name it had before the user renamed it by hand: the look found it by its size and date.
        var before = new ClipGroup([Made("by hand.mp3")]);
        Assert.Equal(LibraryOutcome.Done, _store.SetFavorite(before, true, Scan(before), Now).Outcome);
        File.Move(before.Main.Path, At("now.mp3"));
        var line = new ClipGroup([new ClipFile(At("now.mp3"), 5, LongAgo)]);
        string[] to = [At("Boss.mp3")];
        Move(line, to);

        Assert.Equal(LibraryOutcome.Done, _store.MoveLabels(line, to, Scan(line), Now));

        Assert.True(EntryOf(to[0]).Favorite);
    }

    [Fact]
    public void A_clip_with_no_label_writes_nothing()
    {
        var line = new ClipGroup([Made("old.mp3")]);
        string[] to = [At("new.mp3")];
        Move(line, to);

        Assert.Equal(LibraryOutcome.Done, _store.MoveLabels(line, to));
        Assert.Equal(LibraryOutcome.Done, _store.CopyCollections(line, to));
        Assert.False(File.Exists(_store.Path));
    }

    [Fact]
    public void A_damaged_index_is_left_as_it_is()
    {
        var line = new ClipGroup([Made("old.mp3")]);
        File.WriteAllText(_store.Path, "{ not json");

        Assert.Equal(LibraryOutcome.ReadOnly, _store.MoveLabels(line, [At("new.mp3")]));
        Assert.Equal(LibraryOutcome.ReadOnly, _store.CopyCollections(line, [At("copy.mp3")]));
        Assert.Equal("{ not json", File.ReadAllText(_store.Path));
    }

    [Fact]
    public void A_count_of_paths_that_does_not_fit_the_clip_changes_nothing()
    {
        var line = new ClipGroup([Made("old.mp3")]);
        Assert.Equal(LibraryOutcome.Done, _store.SetFavorite(line, true, Scan(line), Now).Outcome);
        var written = File.ReadAllText(_store.Path);

        Assert.Equal(LibraryOutcome.NoFile, _store.MoveLabels(line, [At("a.mp3"), At("b.mp3")]));
        Assert.Equal(written, File.ReadAllText(_store.Path));
    }

    [Fact]
    public void The_interface_alone_moves_the_labels_of_a_clip_known_by_its_name()
    {
        IClipIndex index = _store;
        var line = new ClipGroup([Made("old.mp3")]);
        Assert.Equal(LibraryOutcome.Done, _store.SetFavorite(line, true, Scan(line), Now).Outcome);
        string[] to = [At("new.mp3")];
        Move(line, to);

        Assert.Equal(LibraryOutcome.Done, index.MoveLabels(line, to));
        Assert.True(EntryOf(to[0]).Favorite);
    }

    /// <summary>A starred clip in a collection, then sent away: its entry outlives its file, as it does for a clip in the Recycle Bin.</summary>
    private string Gone(string name)
    {
        var gone = new ClipGroup([Made(name, size: 7)]);
        Assert.Equal(LibraryOutcome.Done, _store.SetFavorite(gone, true, Scan(gone), Now).Outcome);
        var id = Collection("Old", gone);
        File.Delete(gone.Main.Path);
        return id;
    }

    [Fact]
    public void A_clip_renamed_to_the_name_of_a_clip_gone_never_takes_its_labels()
    {
        Gone("funny.mp3");
        var line = new ClipGroup([Made("plain.mp3")]);
        var to = At("funny.mp3");
        Move(line, [to]);

        Assert.Equal(LibraryOutcome.Done, _store.MoveLabels(line, [to], Scan(line), Now));

        Assert.DoesNotContain(_store.Read().Data.Clips, entry => entry.Files.Exists(file => string.Equals(file.Path, to, StringComparison.Ordinal)));
    }

    [Fact]
    public void A_copy_named_as_a_clip_gone_gets_the_collections_of_its_clip_alone()
    {
        var old = Gone("funny.mp3");
        var line = new ClipGroup([Made("boss.mp3")]);
        var raids = Collection("Raids", line);
        var copy = Made("funny.mp3", size: 9);

        Assert.Equal(LibraryOutcome.Done, _store.CopyCollections(line, [copy.Path], Scan(line), Now));

        var copied = EntryOf(copy.Path);
        Assert.False(copied.Favorite);
        Assert.Equal([raids], copied.Collections);
        Assert.DoesNotContain(_store.Read().Data.Clips, entry => entry.Collections.Contains(old));
    }

    [Fact]
    public void A_copy_joins_the_collections_and_never_gets_the_star()
    {
        var line = new ClipGroup([Made("boss.mp3")]);
        Assert.Equal(LibraryOutcome.Done, _store.SetFavorite(line, true, Scan(line), Now).Outcome);
        var raids = Collection("Raids", line);
        var best = Collection("Best", line);
        var copy = Made("boss (edit).mp3", size: 9);

        Assert.Equal(LibraryOutcome.Done, _store.CopyCollections(line, [copy.Path], Scan(line), Now));

        var copied = EntryOf(copy.Path);
        Assert.False(copied.Favorite);
        Assert.Equal([raids, best], copied.Collections);
        Assert.Equal(9, copied.Files[0].Size);

        var original = EntryOf(line.Main.Path);
        Assert.True(original.Favorite);
        Assert.Equal([raids, best], original.Collections);
    }

    [Fact]
    public void A_starred_clip_in_no_collection_gives_its_copy_nothing()
    {
        var line = new ClipGroup([Made("boss.mp3")]);
        Assert.Equal(LibraryOutcome.Done, _store.SetFavorite(line, true, Scan(line), Now).Outcome);
        var copy = Made("boss (edit).mp3");

        Assert.Equal(LibraryOutcome.Done, _store.CopyCollections(line, [copy.Path], Scan(line), Now));

        Assert.Single(_store.Read().Data.Clips);
    }
}
