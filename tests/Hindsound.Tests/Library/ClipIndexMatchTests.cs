using Hindsound.Library;

namespace Hindsound.Tests.Library;

public sealed class ClipIndexMatchTests
{
    private const string Clips = @"D:\Clips";
    private const string Other = @"D:\Other";
    private const string Unplugged = @"E:\Clips";

    private static readonly DateTime T = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);

    private static ClipFile File(string folder, string name, long size = 1000, DateTime? written = null) =>
        new(Path.Combine(folder, name), size, written ?? T);

    private static ClipGroup Group(params ClipFile[] files) => new(files);

    private static ClipScan Scan(ClipGroup[] groups, params string[] folders) => new(groups, folders);

    private static LibraryFile Known(ClipFile file) => new() { Path = file.Path, Size = file.Size, Written = file.WrittenUtc };

    private static LibraryClip Entry(params ClipFile[] files) => new()
    {
        Files = [.. files.Select(Known)],
        Favorite = true,
        Collections = ["c1"],
    };

    private static void AssertLabeled(IndexMatch match, ClipGroup group)
    {
        var labels = match.LabelsOf(group);
        Assert.True(labels.Favorite);
        Assert.Equal(["c1"], labels.CollectionIds);
        Assert.True(labels.Any);
    }

    private static void AssertBare(IndexMatch match, ClipGroup group)
    {
        var labels = match.LabelsOf(group);
        Assert.False(labels.Favorite);
        Assert.Empty(labels.CollectionIds);
        Assert.False(labels.Any);
    }

    [Fact]
    public void SameFolderAndName_IsTheClip()
    {
        var file = File(Clips, "a.mp3");
        var group = Group(file);

        AssertLabeled(ClipIndexMatch.Match([Entry(file)], Scan([group], Clips)), group);
    }

    [Fact]
    public void AClipWithNoEntry_HasNoLabels()
    {
        var group = Group(File(Clips, "b.mp3", size: 7));

        AssertBare(ClipIndexMatch.Match([Entry(File(Clips, "a.mp3"))], Scan([group], Clips)), group);
    }

    [Fact]
    public void ThePathIsComparedAsWindowsDoes()
    {
        var group = Group(File(@"d:\CLIPS", "A.MP3", size: 5));

        AssertLabeled(ClipIndexMatch.Match([Entry(File(Clips, "a.mp3", size: 99))], Scan([group], @"d:\CLIPS")), group);
    }

    [Fact]
    public void Renamed_SameSizeAndDateWithOneCandidate_KeepsItsLabels()
    {
        var group = Group(File(Clips, "renamed.mp3"));

        AssertLabeled(ClipIndexMatch.Match([Entry(File(Clips, "a.mp3"))], Scan([group], Clips)), group);
    }

    [Fact]
    public void Moved_ToAnotherKnownFolder_KeepsItsLabels()
    {
        var group = Group(File(Other, "a.mp3"));

        AssertLabeled(ClipIndexMatch.Match([Entry(File(Clips, "a.mp3"))], Scan([group], Clips, Other)), group);
    }

    [Fact]
    public void Moved_OutOfTheFoldersRead_IsNoLink()
    {
        var group = Group(File(Other, "a.mp3"));

        AssertBare(ClipIndexMatch.Match([Entry(File(Clips, "a.mp3"))], Scan([group], Clips)), group);
    }

    [Fact]
    public void OtherSize_IsNoCandidate()
    {
        var group = Group(File(Clips, "renamed.mp3", size: 1001));

        AssertBare(ClipIndexMatch.Match([Entry(File(Clips, "a.mp3"))], Scan([group], Clips)), group);
    }

    [Fact]
    public void OtherDate_IsNoCandidate()
    {
        var group = Group(File(Clips, "renamed.mp3", written: T.AddTicks(1)));

        AssertBare(ClipIndexMatch.Match([Entry(File(Clips, "a.mp3"))], Scan([group], Clips)), group);
    }

    [Fact]
    public void TwoCandidates_IsNoLink()
    {
        var one = Group(File(Clips, "one.mp3"));
        var two = Group(File(Other, "two.mp3"));

        var match = ClipIndexMatch.Match([Entry(File(Clips, "a.mp3"))], Scan([one, two], Clips, Other));

        AssertBare(match, one);
        AssertBare(match, two);
    }

    [Fact]
    public void TwoEntriesWantingOneCandidate_IsNoLink()
    {
        var group = Group(File(Clips, "renamed.mp3"));

        var match = ClipIndexMatch.Match([Entry(File(Clips, "a.mp3")), Entry(File(Clips, "b.mp3"))], Scan([group], Clips));

        AssertBare(match, group);
    }

    [Fact]
    public void AFileAnotherEntryNames_IsNoCandidate()
    {
        var named = File(Clips, "named.mp3");
        var lost = new LibraryClip { Files = [Known(File(Clips, "gone.mp3"))], Collections = ["c2"] };
        var group = Group(named);

        var labels = ClipIndexMatch.Match([Entry(named), lost], Scan([group], Clips)).LabelsOf(group);

        Assert.Equal(["c1"], labels.CollectionIds);
    }

    [Fact]
    public void Match_ChangesNothingInTheEntries()
    {
        var entry = Entry(File(Clips, "a.mp3"));
        var gone = Entry(File(Clips, "gone.mp3", size: 42));
        var group = Group(File(Other, "moved.mp3"));

        ClipIndexMatch.Match([entry, gone], Scan([group], Clips, Other));

        Assert.Equal(Path.Combine(Clips, "a.mp3"), entry.Files[0].Path);
        Assert.Null(entry.MissingSince);
        Assert.Null(gone.MissingSince);
    }

    [Fact]
    public void Deleted_TheFirstActionNotesTheDate_AndTheEntryStays()
    {
        var entry = Entry(File(Clips, "a.mp3"));

        var kept = ClipIndexMatch.Settled([entry], Scan([], Clips), Now);

        Assert.Equal([entry], kept);
        Assert.Equal(Now, entry.MissingSince);
    }

    [Fact]
    public void Deleted_ALaterActionKeepsTheFirstDate()
    {
        var entry = Entry(File(Clips, "a.mp3"));
        entry.MissingSince = Now;

        ClipIndexMatch.Settled([entry], Scan([], Clips), Now.AddDays(10));

        Assert.Equal(Now, entry.MissingSince);
    }

    [Fact]
    public void Deleted_StaysUpToThirtyDays_ThenGoes()
    {
        var entry = Entry(File(Clips, "a.mp3"));
        entry.MissingSince = Now;

        Assert.Equal([entry], ClipIndexMatch.Settled([entry], Scan([], Clips), Now.AddDays(30).AddTicks(-1)));
        Assert.Empty(ClipIndexMatch.Settled([entry], Scan([], Clips), Now.AddDays(30)));
    }

    [Fact]
    public void AnOverdueEntry_StillGetsItsFullWaitFromTheFirstAction()
    {
        var entry = Entry(File(Clips, "a.mp3"));

        Assert.Equal([entry], ClipIndexMatch.Settled([entry], Scan([], Clips), Now.AddYears(1)));
        Assert.Equal(Now.AddYears(1), entry.MissingSince);
    }

    [Fact]
    public void BackBeforeThirtyDays_KeepsItsLabels_AndTheCountStops()
    {
        var file = File(Clips, "a.mp3");
        var entry = Entry(file);
        entry.MissingSince = Now.AddDays(-29);
        var group = Group(file);

        var kept = ClipIndexMatch.Settled([entry], Scan([group], Clips), Now);

        Assert.Equal([entry], kept);
        Assert.Null(entry.MissingSince);
        AssertLabeled(ClipIndexMatch.Match(kept, Scan([group], Clips)), group);
    }

    [Fact]
    public void AbsentFolder_CountsNothing()
    {
        var entry = Entry(File(Unplugged, "a.mp3"));

        Assert.Equal([entry], ClipIndexMatch.Settled([entry], Scan([], Clips), Now));
        Assert.Null(entry.MissingSince);
    }

    [Fact]
    public void AbsentFolder_NeitherEndsACountNorRestartsIt()
    {
        var entry = Entry(File(Unplugged, "a.mp3"));
        var noted = Now.AddDays(-40);
        entry.MissingSince = noted;

        Assert.Equal([entry], ClipIndexMatch.Settled([entry], Scan([], Clips), Now));
        Assert.Equal(noted, entry.MissingSince);
    }

    [Fact]
    public void NothingScanned_ChangesNothing()
    {
        var entry = Entry(File(Clips, "a.mp3"));

        Assert.Equal([entry], ClipIndexMatch.Settled([entry], ClipScan.Nothing, Now));
        Assert.Null(entry.MissingSince);
    }

    [Fact]
    public void AFolderMovedAsAWhole_IsFollowed_ItsOldPlaceBeingAbsent()
    {
        var entry = Entry(File(Unplugged, "a.mp3"));
        var group = Group(File(Clips, "a.mp3"));
        var scan = Scan([group], Clips);

        AssertLabeled(ClipIndexMatch.Match([entry], scan), group);
        ClipIndexMatch.Settled([entry], scan, Now);
        Assert.Equal(Path.Combine(Clips, "a.mp3"), entry.Files[0].Path);
    }

    [Theory]
    [InlineData(@"D:\Clips\")]
    [InlineData(@"d:\clips")]
    [InlineData("D:/Clips")]
    public void AFolderRead_IsTheSameFolderHoweverItIsWritten(string written)
    {
        var entry = Entry(File(Clips, "a.mp3"));

        ClipIndexMatch.Settled([entry], Scan([], written), Now);

        Assert.Equal(Now, entry.MissingSince);
    }

    [Fact]
    public void EditedFile_IsStillTheClip_AndTheEntryTakesItsNewSizeAndDate()
    {
        var entry = Entry(File(Clips, "a.mp3"));
        var edited = File(Clips, "a.mp3", size: 2000, written: T.AddHours(3));
        var group = Group(edited);

        AssertLabeled(ClipIndexMatch.Match([entry], Scan([group], Clips)), group);
        ClipIndexMatch.Settled([entry], Scan([group], Clips), Now);

        Assert.Equal(2000, entry.Files[0].Size);
        Assert.Equal(T.AddHours(3), entry.Files[0].Written);
    }

    [Fact]
    public void Renamed_TheNextActionWritesWhereItIsNow()
    {
        var entry = Entry(File(Clips, "a.mp3"));
        var scan = Scan([Group(File(Clips, "renamed.mp3"))], Clips);

        ClipIndexMatch.Settled([entry], scan, Now);

        Assert.Equal(Path.Combine(Clips, "renamed.mp3"), entry.Files[0].Path);
    }

    [Fact]
    public void Tracks_SystemRenamedAndMicDeleted_TheSurvivorKeepsTheLabels()
    {
        var entry = Entry(File(Clips, "a_system.mp3", size: 10), File(Clips, "a_mic.mp3", size: 20));
        var group = Group(File(Clips, "renamed.mp3", size: 10));
        var scan = Scan([group], Clips);

        AssertLabeled(ClipIndexMatch.Match([entry], scan), group);

        Assert.Equal([entry], ClipIndexMatch.Settled([entry], scan, Now));
        Assert.Null(entry.MissingSince);
    }

    [Fact]
    public void Tracks_MicDeleted_ALineWithOnlySystemKeepsTheLabels()
    {
        var system = File(Clips, "a_system.mp3", size: 10);
        var entry = Entry(system, File(Clips, "a_mic.mp3", size: 20));
        var group = Group(system);

        AssertLabeled(ClipIndexMatch.Match([entry], Scan([group], Clips)), group);
    }

    [Fact]
    public void Tracks_ThePairMoved_StaysOneLineWithItsLabels()
    {
        var entry = Entry(File(Clips, "a_system.mp3", size: 10), File(Clips, "a_mic.mp3", size: 20));
        var group = Group(File(Other, "a_system.mp3", size: 10), File(Other, "a_mic.mp3", size: 20));
        var scan = Scan([group], Clips, Other);

        AssertLabeled(ClipIndexMatch.Match([entry], scan), group);
        ClipIndexMatch.Settled([entry], scan, Now);

        Assert.Equal(Path.Combine(Other, "a_system.mp3"), entry.Files[0].Path);
        Assert.Equal(Path.Combine(Other, "a_mic.mp3"), entry.Files[1].Path);
    }

    [Fact]
    public void Tracks_PulledApart_BothLinesKeepTheLabels()
    {
        var mic = File(Clips, "a_mic.mp3", size: 20);
        var entry = Entry(File(Clips, "a_system.mp3", size: 10), mic);
        var renamed = Group(File(Clips, "renamed.mp3", size: 10));
        var kept = Group(mic);

        var match = ClipIndexMatch.Match([entry], Scan([renamed, kept], Clips));

        AssertLabeled(match, renamed);
        AssertLabeled(match, kept);
    }

    [Fact]
    public void Tracks_ADeletedOneThatComesBackLater_IsStillThisClip()
    {
        var system = File(Clips, "a_system.mp3", size: 10);
        var mic = File(Clips, "a_mic.mp3", size: 20);
        var entry = Entry(system, mic);

        ClipIndexMatch.Settled([entry], Scan([Group(system)], Clips), Now);
        var back = Group(mic);

        AssertLabeled(ClipIndexMatch.Match([entry], Scan([Group(system), back], Clips)), back);
    }

    [Fact]
    public void Tracks_BothDeleted_TheCountStarts()
    {
        var entry = Entry(File(Clips, "a_system.mp3", size: 10), File(Clips, "a_mic.mp3", size: 20));

        ClipIndexMatch.Settled([entry], Scan([], Clips), Now);

        Assert.Equal(Now, entry.MissingSince);
    }

    [Fact]
    public void Tracks_NoneFound_ButOneInAnAbsentFolder_CountsNothing()
    {
        var entry = Entry(File(Clips, "a_system.mp3", size: 10), File(Unplugged, "a_mic.mp3", size: 20));

        ClipIndexMatch.Settled([entry], Scan([], Clips), Now);

        Assert.Null(entry.MissingSince);
    }

    [Fact]
    public void Tracks_LabeledApart_ALineMakesOneOfBothEntries()
    {
        var system = File(Clips, "a_system.mp3", size: 10);
        var mic = File(Clips, "a_mic.mp3", size: 20);
        var first = new LibraryClip { Files = [Known(system)], Collections = ["c1"] };
        var second = new LibraryClip { Files = [Known(mic)], Favorite = true, Collections = ["c1", "c2"] };
        var group = Group(system, mic);

        var labels = ClipIndexMatch.Match([first, second], Scan([group], Clips)).LabelsOf(group);

        Assert.True(labels.Favorite);
        Assert.Equal(["c1", "c2"], labels.CollectionIds);
    }

    [Fact]
    public void FoldersCited_AreEveryFolderOnce()
    {
        var one = Entry(File(Clips, "a.mp3"), File(Other, "b.mp3"));
        var two = Entry(File(@"d:\clips", "c.mp3"));

        var folders = ClipIndexMatch.FoldersCited([one, two]);

        Assert.Equal(2, folders.Count);
        Assert.Contains(Clips, folders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Other, folders);
    }
}
