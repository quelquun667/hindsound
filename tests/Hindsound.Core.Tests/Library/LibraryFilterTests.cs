using Hindsound.Library;

namespace Hindsound.Tests.Library;

public sealed class LibraryFilterTests
{
    private const string Folder = @"Z:\no-such-folder";

    private static readonly DateTime Written = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static ClipGroup Clip(string name) => new([new ClipFile(Path.Combine(Folder, name), 1000, Written)]);

    private static LibraryClip Entry(string name, bool favorite, params string[] collections) => new()
    {
        Files = [new LibraryFile { Path = Path.Combine(Folder, name), Size = 1000, Written = Written }],
        Favorite = favorite,
        Collections = [.. collections],
    };

    private static LibraryData Data() => new()
    {
        Collections =
        [
            new LibraryCollection { Id = "c1", Name = "pets" },
            new LibraryCollection { Id = "c2", Name = "Sam" },
            new LibraryCollection { Id = "c3", Name = "Alex" },
        ],
        Clips =
        [
            Entry("both.mp3", false, "c1", "c2"),
            Entry("star.mp3", true),
            Entry("star-and-pets.mp3", true, "c1"),
        ],
    };

    private static readonly ClipGroup[] Lines = [Clip("both.mp3"), Clip("star.mp3"), Clip("plain.mp3"), Clip("star-and-pets.mp3")];

    private static IndexMatch Match(LibraryData data) => ClipIndexMatch.Match(data.Clips, new ClipScan(Lines, [Folder]));

    private static string[] Names(IEnumerable<ClipGroup> rows) => [.. rows.Select(row => Path.GetFileName(row.Main.Path))];

    [Fact]
    public void The_menu_lists_every_clip_the_favorites_then_the_collections_in_the_order_of_the_file()
    {
        Assert.Equal(["All clips", "Favorites", "pets", "Sam", "Alex"], LibraryFilter.Menu(Data()).Select(item => item.Label));
    }

    [Fact]
    public void An_empty_library_still_has_the_two_first_lines()
    {
        Assert.Equal(["All clips", "Favorites"], LibraryFilter.Menu(new LibraryData()).Select(item => item.Label));
    }

    [Fact]
    public void Every_line_of_the_menu_leads_back_to_its_own_choice()
    {
        var chosen = LibraryFilter.Menu(Data()).Select(item => LibraryChoice.FromValue(item.Value)).ToArray();

        Assert.Equal([LibraryChoice.All, LibraryChoice.Favorites, LibraryChoice.Of("c1"), LibraryChoice.Of("c2"), LibraryChoice.Of("c3")], chosen);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("favorites")]
    public void A_collection_whose_id_is_the_word_of_another_choice_stays_a_collection(string id)
    {
        Assert.Equal(LibraryChoice.Of(id), LibraryChoice.FromValue(LibraryChoice.Of(id).Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("collection:")]
    [InlineData("something else")]
    public void A_value_that_means_nothing_shows_every_clip(string? value)
    {
        Assert.Equal(LibraryChoice.All, LibraryChoice.FromValue(value));
    }

    [Fact]
    public void All_clips_keeps_every_line_and_makes_no_copy()
    {
        var data = Data();

        Assert.Same(Lines, LibraryFilter.Rows(Lines, Match(data), LibraryChoice.All));
    }

    [Fact]
    public void Favorites_keeps_the_stars_in_their_order()
    {
        var data = Data();

        Assert.Equal(["star.mp3", "star-and-pets.mp3"], Names(LibraryFilter.Rows(Lines, Match(data), LibraryChoice.Favorites)));
    }

    [Fact]
    public void A_clip_in_two_collections_shows_in_both_and_in_no_other()
    {
        var data = Data();
        var match = Match(data);

        Assert.Equal(["both.mp3", "star-and-pets.mp3"], Names(LibraryFilter.Rows(Lines, match, LibraryChoice.Of("c1"))));
        Assert.Equal(["both.mp3"], Names(LibraryFilter.Rows(Lines, match, LibraryChoice.Of("c2"))));
        Assert.Empty(LibraryFilter.Rows(Lines, match, LibraryChoice.Of("c3")));
    }

    [Fact]
    public void A_collection_nobody_knows_keeps_no_line()
    {
        var data = Data();

        Assert.Empty(LibraryFilter.Rows(Lines, Match(data), LibraryChoice.Of("gone")));
    }

    [Fact]
    public void Each_view_says_what_its_emptiness_means()
    {
        Assert.Equal(new LibraryEmptyText("No clips yet", "Save a clip with your shortcut, and it shows up here."), LibraryFilter.EmptyText(LibraryChoice.All));
        Assert.Equal(new LibraryEmptyText("No clips in this collection"), LibraryFilter.EmptyText(LibraryChoice.Of("c3")));
        Assert.Null(LibraryFilter.EmptyText(LibraryChoice.Favorites).Body);
        Assert.NotEqual(LibraryFilter.EmptyText(LibraryChoice.Of("c3")).Title, LibraryFilter.EmptyText(LibraryChoice.Favorites).Title);
    }

    [Fact]
    public void A_collection_that_has_gone_leaves_every_clip()
    {
        var data = Data();

        Assert.Equal(LibraryChoice.Of("c1"), LibraryFilter.StillThere(LibraryChoice.Of("c1"), data));
        Assert.Equal(LibraryChoice.All, LibraryFilter.StillThere(LibraryChoice.Of("gone"), data));
        Assert.Equal(LibraryChoice.Favorites, LibraryFilter.StillThere(LibraryChoice.Favorites, data));
    }

    [Fact]
    public void The_name_of_a_choice_is_only_that_of_a_collection()
    {
        var data = Data();

        Assert.Equal("Sam", LibraryFilter.NameOf(LibraryChoice.Of("c2"), data));
        Assert.Null(LibraryFilter.NameOf(LibraryChoice.Favorites, data));
        Assert.Null(LibraryFilter.NameOf(LibraryChoice.All, data));
        Assert.Null(LibraryFilter.NameOf(LibraryChoice.Of("gone"), data));
    }

    [Fact]
    public void Filtering_touches_no_file_and_no_entry()
    {
        var folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new LibraryStore(Path.Combine(folder, "settings.json"));
            var clipPath = Path.Combine(folder, "pets-bark.mp3");
            File.WriteAllBytes(clipPath, [1, 2, 3, 4]);
            var clipDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(clipPath, clipDate);
            var group = new ClipGroup([new ClipFile(clipPath, 4, clipDate)]);
            var scan = new ClipScan([group], [folder]);
            var id = store.CreateCollection("pets", scan, Written).CollectionId!;
            Assert.Equal(LibraryOutcome.Done, store.SetInCollection(group, id, true, scan, Written).Outcome);
            var libraryBefore = File.ReadAllBytes(store.Path);
            var libraryDate = File.GetLastWriteTimeUtc(store.Path);

            var data = store.Read().Data;
            var match = ClipIndexMatch.Match(data.Clips, scan);
            foreach (var choice in new[] { LibraryChoice.All, LibraryChoice.Favorites, LibraryChoice.Of(id) })
            {
                LibraryFilter.Rows([group], match, choice);
            }

            Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(clipPath));
            Assert.Equal(clipDate, File.GetLastWriteTimeUtc(clipPath));
            Assert.Equal(libraryBefore, File.ReadAllBytes(store.Path));
            Assert.Equal(libraryDate, File.GetLastWriteTimeUtc(store.Path));
            Assert.Equal(2, Directory.GetFiles(folder).Length);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
