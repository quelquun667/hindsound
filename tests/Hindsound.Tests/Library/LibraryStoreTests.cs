using Hindsound.App;
using Hindsound.Library;

namespace Hindsound.Tests.Library;

public sealed class LibraryStoreTests : IDisposable
{
    private const string Broken = "{ this is not json";

    private static readonly DateTime Now = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));

    public LibraryStoreTests() => Directory.CreateDirectory(_folder);

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    private string LibraryPath => Path.Combine(_folder, "library.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static ClipGroup Clip(string name, long size = 1000) =>
        new([new ClipFile(Path.Combine(@"D:\Clips", name), size, new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc))]);

    private LibraryStore Store() => new(SettingsPath);

    private string[] Files() => [.. Directory.GetFileSystemEntries(_folder).Select(entry => Path.GetFileName(entry)).Order()];

    private string Create(LibraryStore store, string name)
    {
        var change = store.CreateCollection(name, ClipScan.Nothing, Now);
        Assert.Equal(LibraryOutcome.Done, change.Outcome);
        return change.CollectionId!;
    }

    private void AssertEveryActionRefused(LibraryStore store, LibraryOutcome expected = LibraryOutcome.ReadOnly)
    {
        var clip = Clip("a.mp3");
        Assert.Equal(expected, store.CreateCollection("pets", ClipScan.Nothing, Now).Outcome);
        Assert.Equal(expected, store.RenameCollection("c1", "dogs", ClipScan.Nothing, Now).Outcome);
        Assert.Equal(expected, store.DeleteCollection("c1", ClipScan.Nothing, Now).Outcome);
        Assert.Equal(expected, store.SetInCollection(clip, "c1", true, ClipScan.Nothing, Now).Outcome);
        Assert.Equal(expected, store.SetFavorite(clip, true, ClipScan.Nothing, Now).Outcome);
    }

    [Fact]
    public void TheFile_IsNextToTheSettings_SoATestRunHasItsOwn()
    {
        Assert.Equal(LibraryPath, Store().Path);
    }

    [Fact]
    public void Absent_IsEmpty_AndReadingCreatesNothing()
    {
        var missingFolder = Path.Combine(_folder, "not-yet");

        var read = new LibraryStore(Path.Combine(missingFolder, "settings.json")).Read();

        Assert.Equal(LibraryStatus.Missing, read.Status);
        Assert.True(read.CanWrite);
        Assert.Empty(read.Data.Collections);
        Assert.Empty(read.Data.Clips);
        Assert.False(Directory.Exists(missingFolder));
        Assert.Empty(Files());
    }

    [Fact]
    public void Absent_TheFirstActionCreatesTheFolderAndTheFile()
    {
        var store = new LibraryStore(Path.Combine(_folder, "not-yet", "settings.json"));

        Create(store, "pets");

        Assert.Equal(LibraryStatus.Loaded, store.Read().Status);
    }

    [Fact]
    public void ACollectionAndItsClip_ComeBackFromTheFile()
    {
        var store = Store();
        var id = Create(store, "pets");
        var clip = Clip("a.mp3");
        var written = clip.Main.WrittenUtc.AddTicks(1234567);
        clip = new ClipGroup([clip.Main with { WrittenUtc = written }]);

        store.SetInCollection(clip, id, true, ClipScan.Nothing, Now);
        var read = store.Read();

        Assert.Equal(LibraryStatus.Loaded, read.Status);
        Assert.Equal(LibraryStore.CurrentVersion, read.Data.Version);
        Assert.Equal("pets", Assert.Single(read.Data.Collections).Name);
        var entry = Assert.Single(read.Data.Clips);
        Assert.Equal([id], entry.Collections);
        Assert.False(entry.Favorite);
        var file = Assert.Single(entry.Files);
        Assert.Equal(clip.Main.Path, file.Path);
        Assert.Equal(clip.Main.Size, file.Size);
        Assert.Equal(written, file.Written);
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreRead_AsInTheSettings()
    {
        File.WriteAllText(LibraryPath, """
            // Edited by hand.
            { "version": 1, "collections": [{ "id": "c1", "name": "pets" },], }
            """);

        var read = Store().Read();

        Assert.Equal(LibraryStatus.Loaded, read.Status);
        Assert.Single(read.Data.Collections);
    }

    [Fact]
    public void Reading_NeverWrites_NorNotesAnAbsence()
    {
        var store = Store();
        var id = Create(store, "pets");
        store.SetInCollection(Clip("gone.mp3"), id, true, ClipScan.Nothing, Now);
        var before = File.ReadAllBytes(LibraryPath);
        var date = File.GetLastWriteTimeUtc(LibraryPath);
        var files = Files();

        var read = store.Read();
        var scan = new ClipScan([], [@"D:\Clips"]);
        ClipIndexMatch.Match(read.Data.Clips, scan);
        store.Read();

        Assert.Equal(before, File.ReadAllBytes(LibraryPath));
        Assert.Equal(date, File.GetLastWriteTimeUtc(LibraryPath));
        Assert.Equal(files, Files());
        Assert.Null(store.Read().Data.Clips[0].MissingSince);
    }

    [Fact]
    public void Absence_IsWrittenByTheNextAction_AndPurgedOnlyAfterThirtyDays()
    {
        var store = Store();
        var id = Create(store, "pets");
        store.SetInCollection(Clip("gone.mp3"), id, true, ClipScan.Nothing, Now);
        var scan = new ClipScan([], [@"D:\Clips"]);

        store.RenameCollection(id, "dogs", scan, Now);
        Assert.Equal(Now, Assert.Single(store.Read().Data.Clips).MissingSince);

        store.RenameCollection(id, "cats", scan, Now.AddDays(29));
        Assert.Equal(Now, Assert.Single(store.Read().Data.Clips).MissingSince);

        store.RenameCollection(id, "birds", scan, Now.AddDays(30));
        Assert.Empty(store.Read().Data.Clips);
        Assert.Single(store.Read().Data.Collections);
    }

    [Fact]
    public void Absence_AnActionThatGetsRefused_WritesNothing()
    {
        var store = Store();
        var id = Create(store, "pets");
        store.SetInCollection(Clip("gone.mp3"), id, true, ClipScan.Nothing, Now);
        var before = File.ReadAllBytes(LibraryPath);

        var change = store.CreateCollection("PETS", new ClipScan([], [@"D:\Clips"]), Now);

        Assert.Equal(LibraryOutcome.NameTaken, change.Outcome);
        Assert.Equal(before, File.ReadAllBytes(LibraryPath));
    }

    [Fact]
    public void ARenamedClip_TheActionFindsItsEntry_AndWritesWhereItIsNow()
    {
        var store = Store();
        var id = Create(store, "pets");
        store.SetFavorite(Clip("a.mp3"), true, ClipScan.Nothing, Now);
        var renamed = Clip("renamed.mp3");

        store.SetInCollection(renamed, id, true, new ClipScan([renamed], [@"D:\Clips"]), Now);

        var entry = Assert.Single(store.Read().Data.Clips);
        Assert.Equal(renamed.Main.Path, Assert.Single(entry.Files).Path);
        Assert.True(entry.Favorite);
        Assert.Equal([id], entry.Collections);
    }

    [Fact]
    public void EveryAction_ReadsTheFileAgain_SoAHandEditIsNotLost()
    {
        var store = Store();
        Create(store, "pets");
        File.WriteAllText(LibraryPath, """{ "version": 1, "collections": [{ "id": "c1", "name": "pets" }, { "id": "c9", "name": "added by hand" }] }""");

        Create(store, "dogs");

        Assert.Equal(["pets", "added by hand", "dogs"], store.Read().Data.Collections.Select(c => c.Name));
    }

    [Theory]
    [InlineData(Broken)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "version": "1" }""")]
    [InlineData("""{ "version": 1.5 }""")]
    [InlineData("""{ "version": 0 }""")]
    [InlineData("""{ "version": 1, "collections": [null] }""")]
    [InlineData("""{ "version": 1, "collections": [{ "id": "", "name": "pets" }] }""")]
    [InlineData("""{ "version": 1, "collections": [{ "id": "c1", "name": " " }] }""")]
    [InlineData("""{ "version": 1, "collections": [{ "id": "c1", "name": "a" }, { "id": "c1", "name": "b" }] }""")]
    [InlineData("""{ "version": 1, "clips": [null] }""")]
    [InlineData("""{ "version": 1, "clips": [{ "favorite": true }] }""")]
    [InlineData("""{ "version": 1, "clips": [{ "favorite": true, "files": [{ "path": "" }] }] }""")]
    [InlineData("""{ "version": 1, "clips": [{ "favorite": true, "files": [{ "path": "x", "written": "not a date" }] }] }""")]
    public void Damaged_IsReadOnly_AndTheFileStaysAsItIs(string content)
    {
        File.WriteAllText(LibraryPath, content);
        var store = Store();

        var read = store.Read();
        AssertEveryActionRefused(store);

        Assert.Equal(LibraryStatus.Corrupt, read.Status);
        Assert.False(read.CanWrite);
        Assert.Empty(read.Data.Collections);
        Assert.Equal(content, File.ReadAllText(LibraryPath));
        Assert.Equal(["library.json"], Files());
    }

    [Fact]
    public void Damaged_StartOver_KeepsACopyFirst_ThenWorks()
    {
        File.WriteAllText(LibraryPath, Broken);
        var store = Store();

        var change = store.StartOver();

        Assert.Equal(LibraryOutcome.Done, change.Outcome);
        Assert.Equal(Broken, File.ReadAllText(LibraryPath + ".bak"));
        Assert.Equal(LibraryStatus.Loaded, store.Read().Status);
        Create(store, "pets");
    }

    [Fact]
    public void Damaged_StartOver_NeverWritesOverAnEarlierCopy()
    {
        File.WriteAllText(LibraryPath + ".bak", "earlier");
        File.WriteAllText(LibraryPath, Broken);

        Store().StartOver();

        Assert.Equal("earlier", File.ReadAllText(LibraryPath + ".bak"));
        Assert.Equal(Broken, File.ReadAllText(Path.Combine(_folder, "library.json.1.bak")));
    }

    [Fact]
    public void Damaged_StartOver_WithoutACopy_ReplacesNothing()
    {
        File.WriteAllText(LibraryPath, Broken);

        // A folder in its place: not an existing file, and nothing can be copied there.
        Directory.CreateDirectory(LibraryPath + ".bak");
        var change = Store().StartOver();

        Assert.Equal(LibraryOutcome.NotWritten, change.Outcome);
        Assert.Equal(Broken, File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void StartOver_OnAHealthyFile_ChangesNothing()
    {
        var store = Store();
        Create(store, "pets");
        var before = File.ReadAllBytes(LibraryPath);

        var change = store.StartOver();

        Assert.Equal(LibraryOutcome.ReadOnly, change.Outcome);
        Assert.Equal(before, File.ReadAllBytes(LibraryPath));
    }

    [Fact]
    public void Locked_IsUnreadable_AndNothingIsWritten()
    {
        var store = Store();
        Create(store, "pets");
        var before = File.ReadAllBytes(LibraryPath);
        var files = Files();

        using (new FileStream(LibraryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var read = store.Read();

            Assert.Equal(LibraryStatus.Unreadable, read.Status);
            Assert.False(read.CanWrite);
            AssertEveryActionRefused(store);
            Assert.Equal(LibraryOutcome.ReadOnly, store.StartOver().Outcome);
        }

        Assert.Equal(before, File.ReadAllBytes(LibraryPath));
        Assert.Equal(files, Files());
        Assert.Equal(LibraryStatus.Loaded, store.Read().Status);
    }

    [Fact]
    public void TheWriteFails_TheTemporaryFileIsCleanedUp_AndTheFileStaysAsItWas()
    {
        var store = Store();
        Create(store, "pets");
        File.Copy(LibraryPath, LibraryPath + ".bak");
        var before = File.ReadAllBytes(LibraryPath);
        var files = Files();

        // Readable, so the action goes as far as File.Replace, which needs the file to itself.
        LibraryChange change;
        using (new FileStream(LibraryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            change = store.CreateCollection("dogs", ClipScan.Nothing, Now);
        }

        Assert.Equal(LibraryOutcome.NotWritten, change.Outcome);
        Assert.Equal(before, File.ReadAllBytes(LibraryPath));
        Assert.Equal(files, Files());
    }

    [Fact]
    public void Newer_IsShownAsFarAsPossible_AndNeverWritten()
    {
        const string content = """{ "version": 2, "collections": [{ "id": "c1", "name": "pets" }], "somethingNew": [1, 2] }""";
        File.WriteAllText(LibraryPath, content);
        var store = Store();

        var read = store.Read();

        Assert.Equal(LibraryStatus.TooNew, read.Status);
        Assert.False(read.CanWrite);
        Assert.Equal("pets", Assert.Single(read.Data.Collections).Name);
        AssertEveryActionRefused(store);
        Assert.Equal(LibraryOutcome.ReadOnly, store.StartOver().Outcome);
        Assert.Equal(content, File.ReadAllText(LibraryPath));
        Assert.Equal(["library.json"], Files());
    }

    [Fact]
    public void Newer_WithAShapeThisVersionCannotRead_IsNoDamage()
    {
        const string content = """{ "version": 2, "collections": "changed" }""";
        File.WriteAllText(LibraryPath, content);
        var store = Store();

        var read = store.Read();

        Assert.Equal(LibraryStatus.TooNew, read.Status);
        Assert.Empty(read.Data.Collections);
        Assert.Equal(LibraryOutcome.ReadOnly, store.StartOver().Outcome);
        Assert.Equal(content, File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void WritesOverASoundFile_LeaveNoCopyOfIt()
    {
        var store = Store();
        Create(store, "pets");
        Create(store, "dogs");
        Create(store, "cats");

        Assert.Equal(["library.json"], Files());
    }

    [Fact]
    public void ASoundFile_NeedsNoRoomForACopy()
    {
        var store = Store();
        Create(store, "pets");

        // A folder where a copy would go: with no copy to make, it is no obstacle.
        Directory.CreateDirectory(LibraryPath + ".bak");
        var change = store.CreateCollection("dogs", ClipScan.Nothing, Now);

        Assert.Equal(LibraryOutcome.Done, change.Outcome);
        Assert.Equal(["pets", "dogs"], store.Read().Data.Collections.Select(c => c.Name));
    }

    [Fact]
    public void ADeletedCollection_LeavesItsNameInNoFileOfTheFolder()
    {
        var store = Store();
        var id = Create(store, "Collection-Canary-7F3");
        Create(store, "dogs");

        Assert.Equal(LibraryOutcome.Done, store.DeleteCollection(id, ClipScan.Nothing, Now).Outcome);

        var files = Directory.GetFiles(_folder, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.DoesNotContain("Collection-Canary-7F3", File.ReadAllText(file)));
    }

    [Fact]
    public void Create_TrimsTheName_AndGivesAnIdNotInUse()
    {
        var store = Store();
        var first = Create(store, "  pets ");
        var second = Create(store, "dogs");
        store.DeleteCollection(first, ClipScan.Nothing, Now);
        var third = Create(store, "cats");

        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);
        Assert.Equal(["dogs", "cats"], store.Read().Data.Collections.Select(c => c.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NoName_IsRefused(string name)
    {
        var store = Store();

        var change = store.CreateCollection(name, ClipScan.Nothing, Now);

        Assert.Equal(LibraryOutcome.NameEmpty, change.Outcome);
        Assert.Null(change.CollectionId);
        Assert.False(File.Exists(LibraryPath));
    }

    [Theory]
    [InlineData("pets")]
    [InlineData("PETS")]
    [InlineData(" pets ")]
    public void Create_ANameInUse_IsRefused(string name)
    {
        var store = Store();
        Create(store, "pets");

        var change = store.CreateCollection(name, ClipScan.Nothing, Now);

        Assert.Equal(LibraryOutcome.NameTaken, change.Outcome);
        Assert.Null(change.CollectionId);
        Assert.Single(store.Read().Data.Collections);
    }

    [Fact]
    public void Rename_ChangesTheNameAndNoLabel()
    {
        var store = Store();
        var id = Create(store, "pets");
        store.SetInCollection(Clip("a.mp3"), id, true, ClipScan.Nothing, Now);

        Assert.Equal(LibraryOutcome.Done, store.RenameCollection(id, " dogs ", ClipScan.Nothing, Now).Outcome);

        var read = store.Read();
        Assert.Equal("dogs", Assert.Single(read.Data.Collections).Name);
        Assert.Equal([id], Assert.Single(read.Data.Clips).Collections);
    }

    [Fact]
    public void Rename_ToANameInUse_IsRefused_ButItsOwnNameInAnotherCaseIsNot()
    {
        var store = Store();
        var id = Create(store, "pets");
        Create(store, "dogs");

        Assert.Equal(LibraryOutcome.NameTaken, store.RenameCollection(id, "Dogs", ClipScan.Nothing, Now).Outcome);
        Assert.Equal(LibraryOutcome.NameEmpty, store.RenameCollection(id, " ", ClipScan.Nothing, Now).Outcome);
        Assert.Equal("pets", store.Read().Data.Collections[0].Name);

        Assert.Equal(LibraryOutcome.Done, store.RenameCollection(id, "Pets", ClipScan.Nothing, Now).Outcome);
        Assert.Equal("Pets", store.Read().Data.Collections[0].Name);
    }

    [Fact]
    public void AnIdThatIsGone_IsRefused_AndNothingIsWritten()
    {
        var store = Store();
        var clip = Clip("a.mp3");
        Create(store, "pets");
        var before = File.ReadAllBytes(LibraryPath);

        Assert.Equal(LibraryOutcome.UnknownCollection, store.RenameCollection("c99", "dogs", ClipScan.Nothing, Now).Outcome);
        Assert.Equal(LibraryOutcome.UnknownCollection, store.DeleteCollection("c99", ClipScan.Nothing, Now).Outcome);
        Assert.Equal(LibraryOutcome.UnknownCollection, store.SetInCollection(clip, "c99", true, ClipScan.Nothing, Now).Outcome);

        Assert.Equal(before, File.ReadAllBytes(LibraryPath));
    }

    [Fact]
    public void AClip_CanBeInTwoCollections_AndDeletingOneKeepsTheOtherAndTheStar()
    {
        var store = Store();
        var pets = Create(store, "pets");
        var dogs = Create(store, "dogs");
        var clip = Clip("a.mp3");

        store.SetInCollection(clip, pets, true, ClipScan.Nothing, Now);
        store.SetInCollection(clip, dogs, true, ClipScan.Nothing, Now);
        store.SetFavorite(clip, true, ClipScan.Nothing, Now);
        var both = ClipIndexMatch.Match(store.Read().Data.Clips, new ClipScan([clip], [@"D:\Clips"])).LabelsOf(clip);
        Assert.Equal([pets, dogs], both.CollectionIds);

        Assert.Equal(LibraryOutcome.Done, store.DeleteCollection(pets, ClipScan.Nothing, Now).Outcome);

        var read = store.Read();
        Assert.Equal("dogs", Assert.Single(read.Data.Collections).Name);
        var entry = Assert.Single(read.Data.Clips);
        Assert.Equal([dogs], entry.Collections);
        Assert.True(entry.Favorite);
    }

    [Fact]
    public void Delete_LeavesTheIdNowhere_NeitherInTheFileNorInWhatItReturns()
    {
        var store = Store();
        var kept = Create(store, "pets");
        const string gone = "c2";
        Assert.Equal(gone, Create(store, "dogs"));
        var clip = Clip("a.mp3");
        store.SetInCollection(clip, kept, true, ClipScan.Nothing, Now);
        store.SetInCollection(clip, gone, true, ClipScan.Nothing, Now);

        var change = store.DeleteCollection(gone, ClipScan.Nothing, Now);

        Assert.Equal([kept], Assert.Single(change.Library.Data.Clips).Collections);
        Assert.DoesNotContain($"\"{gone}\"", File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void Delete_TheLastLabelOfAClip_LeavesNoEntry_ButAStarredOneStays()
    {
        var store = Store();
        var id = Create(store, "pets");
        store.SetInCollection(Clip("a.mp3"), id, true, ClipScan.Nothing, Now);
        store.SetInCollection(Clip("b.mp3", size: 2), id, true, ClipScan.Nothing, Now);
        store.SetFavorite(Clip("b.mp3", size: 2), true, ClipScan.Nothing, Now);

        store.DeleteCollection(id, ClipScan.Nothing, Now);

        var entry = Assert.Single(store.Read().Data.Clips);
        Assert.Equal(Clip("b.mp3").Main.Path, entry.Files[0].Path);
    }

    [Fact]
    public void Collections_DeletingOrRenaming_TouchesNoAudioFile()
    {
        var audioPath = Path.Combine(_folder, "a.mp3");
        byte[] audio = [1, 2, 3, 4];
        File.WriteAllBytes(audioPath, audio);
        var date = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(audioPath, date);
        var clip = new ClipGroup([new ClipFile(audioPath, audio.Length, date)]);
        var scan = new ClipScan([clip], [_folder]);
        var store = Store();

        var id = store.CreateCollection("pets", scan, Now).CollectionId!;
        store.SetInCollection(clip, id, true, scan, Now);
        store.SetFavorite(clip, true, scan, Now);
        store.RenameCollection(id, "dogs", scan, Now);
        store.SetInCollection(clip, id, false, scan, Now);
        store.DeleteCollection(id, scan, Now);

        Assert.Equal(audio, File.ReadAllBytes(audioPath));
        Assert.Equal(date, File.GetLastWriteTimeUtc(audioPath));
        Assert.True(Assert.Single(store.Read().Data.Clips).Favorite);
    }

    [Fact]
    public void Untick_TheLastLabel_LeavesNoEntry()
    {
        var store = Store();
        var id = Create(store, "pets");
        var clip = Clip("a.mp3");

        store.SetInCollection(clip, id, true, ClipScan.Nothing, Now);
        store.SetInCollection(clip, id, true, ClipScan.Nothing, Now);
        Assert.Equal([id], Assert.Single(store.Read().Data.Clips).Collections);

        store.SetInCollection(clip, id, false, ClipScan.Nothing, Now);
        Assert.Empty(store.Read().Data.Clips);
    }

    [Fact]
    public void Star_OnAndOff()
    {
        var store = Store();
        var clip = Clip("a.mp3");

        store.SetFavorite(clip, true, ClipScan.Nothing, Now);
        Assert.True(Assert.Single(store.Read().Data.Clips).Favorite);

        store.SetFavorite(clip, false, ClipScan.Nothing, Now);
        Assert.Empty(store.Read().Data.Clips);
    }

    [Fact]
    public void Star_OnATwoTrackClip_KeepsBothFiles()
    {
        var store = Store();
        var when = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        var clip = new ClipGroup([new ClipFile(@"D:\Clips\a_system.mp3", 10, when), new ClipFile(@"D:\Clips\a_mic.mp3", 20, when)]);

        store.SetFavorite(clip, true, ClipScan.Nothing, Now);

        Assert.Equal(2, Assert.Single(store.Read().Data.Clips).Files.Count);
    }

    [Fact]
    public void Star_OnALineMadeOfTwoEntries_ComesOffBoth()
    {
        var when = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        var system = new ClipFile(@"D:\Clips\a_system.mp3", 10, when);
        var mic = new ClipFile(@"D:\Clips\a_mic.mp3", 20, when);
        var store = Store();
        store.SetFavorite(new ClipGroup([system]), true, ClipScan.Nothing, Now);
        store.SetFavorite(new ClipGroup([mic]), true, ClipScan.Nothing, Now);
        Assert.Equal(2, store.Read().Data.Clips.Count);

        store.SetFavorite(new ClipGroup([system, mic]), false, ClipScan.Nothing, Now);

        Assert.Empty(store.Read().Data.Clips);
    }

    [Fact]
    public void AnIdOfACollectionThatIsGone_MeansNothing_AndAnEntryWithNoLabelIsDropped()
    {
        File.WriteAllText(LibraryPath, """
            { "version": 1,
              "collections": [{ "id": "c1", "name": "pets" }],
              "clips": [
                { "files": [{ "path": "a" }], "collections": ["c1", "c9", "c1"] },
                { "files": [{ "path": "b" }], "collections": ["c9"] },
                { "files": [{ "path": "c" }] }
              ] }
            """);

        var read = Store().Read();

        Assert.Equal(LibraryStatus.Loaded, read.Status);
        Assert.Equal(["c1"], Assert.Single(read.Data.Clips).Collections);
    }

    [Fact]
    public void TheCanary_NeverReachesTheLog()
    {
        const string canary = "Collection-Canary-7F3";

        // The folder too: the message of a file error holds the full path.
        var folder = Path.Combine(_folder, canary);
        Directory.CreateDirectory(folder);
        var logPath = Path.Combine(_folder, "journal.log");
        var journal = new Journal(logPath);
        var store = new LibraryStore(Path.Combine(folder, "settings.json"), journal.Write);
        var library = Path.Combine(folder, "library.json");
        var clip = new ClipGroup([new ClipFile(Path.Combine(folder, canary + ".mp3"), 10, Now)]);
        var scan = new ClipScan([clip], [folder]);

        // Every path through the store: the plain ones.
        var id = store.CreateCollection(canary, scan, Now).CollectionId!;
        store.SetInCollection(clip, id, true, scan, Now);
        store.SetFavorite(clip, true, scan, Now);
        store.RenameCollection(id, canary + "-2", scan, Now);
        store.CreateCollection(canary + "-2", scan, Now);
        store.Read();

        // A lock on reading, then one on writing only.
        using (new FileStream(library, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store.Read();
            store.CreateCollection(canary + "-3", scan, Now);
        }

        using (new FileStream(library, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.CreateCollection(canary + "-4", scan, Now);
        }

        store.DeleteCollection(id, scan, Now);

        // A later version, then a broken file that quotes the name, which a parser's message could repeat.
        File.WriteAllText(library, $$"""{ "version": 2, "collections": [{ "id": "c1", "name": "{{canary}}" }] }""");
        store.Read();
        store.CreateCollection(canary + "-5", scan, Now);
        File.WriteAllText(library, $$"""{ "version": 1, "collections": [{ "id": "{{canary}}", "name": {{canary}} }] }""");
        store.Read();
        store.CreateCollection(canary + "-6", scan, Now);

        // A broken file whose copy cannot be made.
        var free = Path.Combine(folder, "library.json.bak");
        Assert.True(File.Exists(free));
        Directory.CreateDirectory(Path.Combine(folder, "library.json.1.bak"));
        Assert.Equal(LibraryOutcome.NotWritten, store.StartOver().Outcome);

        var log = File.ReadAllText(logPath);
        Assert.Contains("library:", log);
        Assert.DoesNotContain(canary, log, StringComparison.OrdinalIgnoreCase);
    }
}
