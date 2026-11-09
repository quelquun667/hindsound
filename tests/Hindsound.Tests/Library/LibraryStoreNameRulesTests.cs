using Hindsound.Library;

namespace Hindsound.Tests.Library;

public sealed class LibraryStoreNameRulesTests : IDisposable
{
    private const string Canary = "Collection-Canary-7F3";

    private static readonly DateTime Now = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _told = [];

    public LibraryStoreNameRulesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private LibraryStore Store() => new(Path.Combine(_folder, "settings.json"), _told.Add);

    private static LibraryCollection[] Pets() => [new LibraryCollection { Id = "c1", Name = "pets" }];

    [Theory]
    [InlineData("", LibraryOutcome.NameEmpty)]
    [InlineData("   ", LibraryOutcome.NameEmpty)]
    [InlineData("pets", LibraryOutcome.NameTaken)]
    [InlineData("PETS", LibraryOutcome.NameTaken)]
    [InlineData(" pets ", LibraryOutcome.NameTaken)]
    [InlineData("All clips", LibraryOutcome.NameTaken)]
    [InlineData("all clips", LibraryOutcome.NameTaken)]
    [InlineData("ALL CLIPS", LibraryOutcome.NameTaken)]
    [InlineData("  all CLIPS  ", LibraryOutcome.NameTaken)]
    [InlineData("Favorites", LibraryOutcome.NameTaken)]
    [InlineData("favorites", LibraryOutcome.NameTaken)]
    [InlineData("FAVORITES", LibraryOutcome.NameTaken)]
    public void A_name_is_refused_for_the_reason_it_deserves(string name, LibraryOutcome expected)
    {
        Assert.Equal(expected, LibraryStore.NameRefusal(Pets(), name));
    }

    [Theory]
    [InlineData("dogs")]
    [InlineData("All clips of Sam")]
    [InlineData("Favorites 2")]
    [InlineData("Sam")]
    public void A_free_name_is_given(string name)
    {
        Assert.Null(LibraryStore.NameRefusal(Pets(), name));
    }

    [Fact]
    public void Forty_characters_are_allowed_and_forty_one_are_not()
    {
        Assert.Null(LibraryStore.NameRefusal([], new string('a', LibraryStore.MaxNameLength)));
        Assert.Equal(40, LibraryStore.MaxNameLength);
        Assert.Equal(LibraryOutcome.NameTooLong, LibraryStore.NameRefusal([], new string('a', 41)));
    }

    [Fact]
    public void The_length_is_that_of_the_trimmed_name()
    {
        Assert.Null(LibraryStore.NameRefusal([], "  " + new string('a', 40) + "  "));
    }

    [Fact]
    public void A_collection_may_keep_its_name_or_change_its_case()
    {
        Assert.Null(LibraryStore.NameRefusal(Pets(), "pets", exceptId: "c1"));
        Assert.Null(LibraryStore.NameRefusal(Pets(), "Pets", exceptId: "c1"));
        Assert.Equal(LibraryOutcome.NameTaken, LibraryStore.NameRefusal(Pets(), "pets", exceptId: "c2"));
    }

    [Theory]
    [InlineData("All clips")]
    [InlineData("favorites")]
    public void Creating_with_a_name_of_the_menu_writes_nothing(string name)
    {
        var store = Store();

        var change = store.CreateCollection(name, ClipScan.Nothing, Now);

        Assert.Equal(LibraryOutcome.NameTaken, change.Outcome);
        Assert.Null(change.CollectionId);
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public void Creating_with_too_long_a_name_writes_nothing()
    {
        var store = Store();

        Assert.Equal(LibraryOutcome.NameTooLong, store.CreateCollection(new string('a', 41), ClipScan.Nothing, Now).Outcome);

        Assert.False(File.Exists(store.Path));
    }

    [Theory]
    [InlineData("FAVORITES", LibraryOutcome.NameTaken)]
    [InlineData("all clips", LibraryOutcome.NameTaken)]
    [InlineData("dogs", LibraryOutcome.NameTaken)]
    [InlineData("  ", LibraryOutcome.NameEmpty)]
    public void A_refused_rename_keeps_the_old_name(string name, LibraryOutcome expected)
    {
        var store = Store();
        var id = store.CreateCollection("pets", ClipScan.Nothing, Now).CollectionId!;
        store.CreateCollection("dogs", ClipScan.Nothing, Now);
        var before = File.ReadAllBytes(store.Path);

        var change = store.RenameCollection(id, name, ClipScan.Nothing, Now);

        Assert.Equal(expected, change.Outcome);
        Assert.Equal("pets", change.Library.Data.Collections.Find(c => c.Id == id)!.Name);
        Assert.Equal(before, File.ReadAllBytes(store.Path));
    }

    [Fact]
    public void A_rename_past_the_limit_keeps_the_old_name()
    {
        var store = Store();
        var id = store.CreateCollection("pets", ClipScan.Nothing, Now).CollectionId!;

        Assert.Equal(LibraryOutcome.NameTooLong, store.RenameCollection(id, new string('a', 41), ClipScan.Nothing, Now).Outcome);

        Assert.Equal("pets", store.Read().Data.Collections[0].Name);
    }

    [Fact]
    public void A_name_is_written_trimmed_up_to_the_limit()
    {
        var store = Store();
        var name = new string('a', 40);

        var id = store.CreateCollection("  " + name + "  ", ClipScan.Nothing, Now).CollectionId!;
        Assert.Equal(name, store.Read().Data.Collections[0].Name);

        Assert.Equal(LibraryOutcome.Done, store.RenameCollection(id, " Pets ", ClipScan.Nothing, Now).Outcome);
        Assert.Equal("Pets", store.Read().Data.Collections[0].Name);
    }

    [Fact]
    public void A_collection_may_change_only_the_case_of_its_name()
    {
        var store = Store();
        var id = store.CreateCollection("pets", ClipScan.Nothing, Now).CollectionId!;

        Assert.Equal(LibraryOutcome.Done, store.RenameCollection(id, "PETS", ClipScan.Nothing, Now).Outcome);

        Assert.Equal("PETS", store.Read().Data.Collections[0].Name);
    }

    [Fact]
    public void A_file_written_by_hand_with_such_names_is_still_sound_and_keeps_them()
    {
        var store = Store();
        var longName = new string('a', 60);
        File.WriteAllText(store.Path, $$"""
            { "version": 1, "collections": [ { "id": "c1", "name": "{{longName}}" }, { "id": "c2", "name": "Favorites" } ] }
            """);

        var read = store.Read();

        Assert.Equal(LibraryStatus.Loaded, read.Status);
        Assert.Equal([longName, "Favorites"], read.Data.Collections.Select(c => c.Name));
    }

    [Fact]
    public void A_refused_name_never_reaches_the_log()
    {
        var store = Store();
        var id = store.CreateCollection(Canary, ClipScan.Nothing, Now).CollectionId!;
        var tooLong = string.Concat(Enumerable.Repeat(Canary, 3));

        Assert.Equal(LibraryOutcome.NameTooLong, store.CreateCollection(tooLong, ClipScan.Nothing, Now).Outcome);
        Assert.Equal(LibraryOutcome.NameTaken, store.CreateCollection(Canary, ClipScan.Nothing, Now).Outcome);
        Assert.Equal(LibraryOutcome.NameTooLong, store.RenameCollection(id, tooLong, ClipScan.Nothing, Now).Outcome);
        Assert.Equal(LibraryOutcome.NameTaken, store.RenameCollection(id, "Favorites", ClipScan.Nothing, Now).Outcome);

        // The same names with the file in a state that is told to the log.
        var fine = File.ReadAllText(store.Path);
        using (new FileStream(store.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(LibraryOutcome.NotWritten, store.CreateCollection(Canary + "-2", ClipScan.Nothing, Now).Outcome);
        }

        using (new FileStream(store.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(LibraryOutcome.ReadOnly, store.CreateCollection(tooLong, ClipScan.Nothing, Now).Outcome);
        }

        Assert.Equal(fine, File.ReadAllText(store.Path));
        Assert.NotEmpty(_told);
        Assert.DoesNotContain(_told, line => line.Contains(Canary, StringComparison.OrdinalIgnoreCase));
    }
}
