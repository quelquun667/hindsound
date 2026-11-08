using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>
/// A clip copied to a FAT or exFAT drive keeps its size and its name but not its date to the tick: it is still found, as long as
/// that is the only way it can be, and the exact date has been tried first.
/// </summary>
public sealed class ClipIndexMatchCoarseDatesTests
{
    private const string Clips = @"D:\Clips";
    private const string Drive = @"E:\Clips";

    private static readonly DateTime T = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static ClipFile File(string folder, string name, DateTime written, long size = 1000) => new(Path.Combine(folder, name), size, written);

    private static ClipGroup Group(params ClipFile[] files) => new(files);

    private static ClipScan Scan(params ClipGroup[] groups) => new(groups, [Clips, Drive]);

    private static LibraryFile Known(ClipFile file) => new() { Path = file.Path, Size = file.Size, Written = file.WrittenUtc };

    private static LibraryClip Entry(string collection, params ClipFile[] files) => new() { Files = [.. files.Select(Known)], Collections = [collection] };

    private static string[] Labels(IndexMatch match, ClipGroup group) => [.. match.LabelsOf(group).CollectionIds];

    [Theory]
    [InlineData(1_234_567L, 20_000_000L)] // FAT: to the next even second
    [InlineData(1_234_567L, 1_200_000L)] // exFAT: cut to 10 ms
    [InlineData(0L, 20_000_000L)] // exactly the margin
    public void ACopyWithACoarserDate_IsTheClip(long before, long onTheDrive)
    {
        var copy = Group(File(Drive, "a.mp3", T.AddTicks(onTheDrive)));

        var match = ClipIndexMatch.Match([Entry("c1", File(Clips, "a.mp3", T.AddTicks(before)))], Scan(copy));

        Assert.Equal(["c1"], Labels(match, copy));
    }

    [Fact]
    public void ACopyWithACoarserDate_IsFollowedByTheEntryOnceAnActionHasWrittenIt()
    {
        var entry = Entry("c1", File(Clips, "a.mp3", T.AddTicks(1_234_567)));
        var copy = File(Drive, "a.mp3", T.AddSeconds(2));

        var settled = ClipIndexMatch.Settled([entry], Scan(Group(copy)), T.AddDays(1));

        Assert.Equal(copy.Path, Assert.Single(Assert.Single(settled).Files).Path);
        Assert.Equal(copy.WrittenUtc, entry.Files[0].Written);
        Assert.Null(entry.MissingSince);
    }

    [Theory]
    [InlineData(20_100_000L)] // a hundredth of a second past the margin
    [InlineData(-20_100_000L)]
    public void ADateTooFarAway_IsNoCandidate(long onTheDrive)
    {
        var far = Group(File(Drive, "a.mp3", T.AddTicks(onTheDrive)));

        Assert.Empty(Labels(ClipIndexMatch.Match([Entry("c1", File(Clips, "a.mp3", T))], Scan(far)), far));
    }

    [Fact]
    public void ADateOffTheGridOfSuchADrive_IsNoCandidate()
    {
        var off = Group(File(Drive, "a.mp3", T.AddSeconds(1).AddTicks(1)));

        Assert.Empty(Labels(ClipIndexMatch.Match([Entry("c1", File(Clips, "a.mp3", T))], Scan(off)), off));
    }

    [Fact]
    public void AnotherSize_IsNoCandidate()
    {
        var other = Group(File(Drive, "a.mp3", T.AddSeconds(1), size: 1001));

        Assert.Empty(Labels(ClipIndexMatch.Match([Entry("c1", File(Clips, "a.mp3", T))], Scan(other)), other));
    }

    [Fact]
    public void TwoCandidatesWithinTheMargin_AreNoLink()
    {
        var one = Group(File(Drive, "one.mp3", T.AddSeconds(1)));
        var two = Group(File(Drive, "two.mp3", T.AddSeconds(2)));

        var match = ClipIndexMatch.Match([Entry("c1", File(Clips, "a.mp3", T))], Scan(one, two));

        Assert.Empty(Labels(match, one));
        Assert.Empty(Labels(match, two));
    }

    [Fact]
    public void TwoLostEntriesWithinTheMarginOfOneFile_AreNoLink()
    {
        var copy = Group(File(Drive, "copy.mp3", T.AddSeconds(2)));

        var match = ClipIndexMatch.Match(
            [Entry("c1", File(Clips, "a.mp3", T.AddSeconds(1))), Entry("c2", File(Clips, "b.mp3", T.AddSeconds(3)))],
            Scan(copy));

        Assert.Empty(Labels(match, copy));
    }

    [Fact]
    public void AFileAnotherEntryNames_IsNotTakenForTheLostOne()
    {
        var named = File(Drive, "kept.mp3", T.AddSeconds(2));
        var kept = Entry("c2", named);
        var lost = Entry("c1", File(Clips, "a.mp3", T));
        var group = Group(named);

        var match = ClipIndexMatch.Match([lost, kept], Scan(group));

        Assert.Equal(["c2"], Labels(match, group));
    }

    [Fact]
    public void TheExactDateComesFirst_SoTwoTracksOfOneSizeASecondApartAreNotADoubt()
    {
        var first = Group(File(Drive, "x.mp3", T));
        var second = Group(File(Drive, "y.mp3", T.AddSeconds(1)));

        var match = ClipIndexMatch.Match(
            [Entry("c1", File(Clips, "a.mp3", T)), Entry("c2", File(Clips, "b.mp3", T.AddSeconds(1)))],
            Scan(first, second));

        Assert.Equal(["c1"], Labels(match, first));
        Assert.Equal(["c2"], Labels(match, second));
    }

    [Fact]
    public void AFileLinkedByItsExactDate_IsNotAlsoTakenByACoarseOne()
    {
        var exact = Group(File(Drive, "x.mp3", T));
        var coarse = Entry("c2", File(Clips, "b.mp3", T.AddSeconds(1).AddTicks(5)));

        var match = ClipIndexMatch.Match([Entry("c1", File(Clips, "a.mp3", T)), coarse], Scan(exact));

        Assert.Equal(["c1"], Labels(match, exact));
    }

    [Fact]
    public void ARenamedPairOfTracksOfOneSizeAndDate_IsToldApartBySuffix()
    {
        var system = File(Drive, "y_system.mp3", T);
        var mic = File(Drive, "y_mic.mp3", T);
        var entries = new[]
        {
            Entry("c1", File(Clips, "x_system.mp3", T)),
            Entry("c2", File(Clips, "x_mic.mp3", T)),
        };

        // Listed in the opposite order of the entries, so that the order tells nothing.
        var match = ClipIndexMatch.Match(entries, Scan(Group(mic), Group(system)));

        Assert.Equal(["c1"], Labels(match, Group(system)));
        Assert.Equal(["c2"], Labels(match, Group(mic)));
    }

    [Fact]
    public void ARenamedPairOfTracksWithACoarserDate_IsToldApartBySuffix()
    {
        var system = File(Drive, "y_system.mp3", T.AddSeconds(2));
        var mic = File(Drive, "y_mic.mp3", T.AddSeconds(2));
        var entries = new[]
        {
            Entry("c1", File(Clips, "x_system.mp3", T.AddTicks(1_234_567))),
            Entry("c2", File(Clips, "x_mic.mp3", T.AddTicks(1_534_567))),
        };

        var match = ClipIndexMatch.Match(entries, Scan(Group(mic), Group(system)));

        Assert.Equal(["c1"], Labels(match, Group(system)));
        Assert.Equal(["c2"], Labels(match, Group(mic)));
    }

    [Fact]
    public void TwoLostFilesOfOneSizeAndDate_WithoutTheSuffixes_StayADoubt()
    {
        var one = Group(File(Drive, "one.mp3", T));
        var two = Group(File(Drive, "two.mp3", T));

        var match = ClipIndexMatch.Match([Entry("c1", File(Clips, "a.mp3", T)), Entry("c2", File(Clips, "b.mp3", T))], Scan(one, two));

        Assert.Empty(Labels(match, one));
        Assert.Empty(Labels(match, two));
    }

    [Fact]
    public void TwoLostTracksOfOneKind_StayADoubt_WhateverTheSuffixOfTheFilesThere()
    {
        var system = Group(File(Drive, "y_system.mp3", T));
        var mic = Group(File(Drive, "y_mic.mp3", T));

        var match = ClipIndexMatch.Match(
            [Entry("c1", File(Clips, "x_system.mp3", T)), Entry("c2", File(Clips, "z_system.mp3", T))],
            Scan(system, mic));

        Assert.Empty(Labels(match, system));
        Assert.Empty(Labels(match, mic));
    }

    [Fact]
    public void TwoFilesOfOneKindThere_AreADoubtForALostPair()
    {
        var one = Group(File(Drive, "y_system.mp3", T));
        var two = Group(File(Drive, "z_system.mp3", T));

        var match = ClipIndexMatch.Match(
            [Entry("c1", File(Clips, "x_system.mp3", T)), Entry("c2", File(Clips, "x_mic.mp3", T))],
            Scan(one, two));

        Assert.Empty(Labels(match, one));
        Assert.Empty(Labels(match, two));
    }

    [Fact]
    public void ThreeLostFilesOfOneSizeAndDate_StayADoubt_EvenWhenTwoOfThemCouldBePaired()
    {
        var there = new[] { "y_system.mp3", "y_mic.mp3", "v_system.mp3" }.Select(name => Group(File(Drive, name, T))).ToArray();

        var match = ClipIndexMatch.Match(
            [
                Entry("c1", File(Clips, "x_system.mp3", T)),
                Entry("c2", File(Clips, "x_mic.mp3", T)),
                Entry("c3", File(Clips, "w_system.mp3", T)),
            ],
            Scan(there));

        // The two system tracks are a doubt; the mic track is the only one of its kind, and is found all the same.
        Assert.Empty(Labels(match, there[0]));
        Assert.Empty(Labels(match, there[2]));
        Assert.Equal(["c2"], Labels(match, there[1]));
    }

    [Fact]
    public void AMicTrack_IsNeverTheSystemTrackOfAClipWrittenASecondApart()
    {
        var system = Group(File(Drive, "new_system.mp3", T.AddSeconds(1)));

        var match = ClipIndexMatch.Match([Entry("c1", File(Clips, "old_mic.mp3", T))], Scan(system));

        Assert.Empty(Labels(match, system));
    }

    [Fact]
    public void ARenamedFileOfKnownSuffix_FollowsAFileWithNone()
    {
        var plain = Group(File(Drive, "plain.mp3", T.AddSeconds(1)));

        var match = ClipIndexMatch.Match([Entry("c1", File(Clips, "old_mic.mp3", T))], Scan(plain));

        Assert.Equal(["c1"], Labels(match, plain));
    }

    [Fact]
    public void AFileOfAFolderNotRead_IsNoCandidate()
    {
        var elsewhere = Group(File(@"F:\Away", "a.mp3", T.AddSeconds(1)));

        Assert.Empty(Labels(ClipIndexMatch.Match([Entry("c1", File(Clips, "a.mp3", T))], Scan(elsewhere)), elsewhere));
    }
}
