using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>Cases of the matching that a doubt or a coincidence of names could get wrong.</summary>
public sealed class ClipIndexMatchRulesTests
{
    private const string Clips = @"D:\Clips";

    private static readonly DateTime T = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);

    private static ClipFile File(string name, long size = 1000) => new(Path.Combine(Clips, name), size, T);

    private static ClipGroup Group(params ClipFile[] files) => new(files);

    private static ClipScan Scan(params ClipGroup[] groups) => new(groups, [Clips]);

    private static LibraryFile Known(ClipFile file) => new() { Path = file.Path, Size = file.Size, Written = file.WrittenUtc };

    private static LibraryClip Entry(ClipFile file, string? collection = null, bool favorite = false) => new()
    {
        Files = [Known(file)],
        Favorite = favorite,
        Collections = collection is null ? [] : [collection],
    };

    [Fact]
    public void TwoCopies_StayADoubt_WhateverIsLabeledAmongThem()
    {
        var copy1 = File("copy1.mp3");
        var copy2 = File("copy2.mp3");
        var dead = Entry(File("a.mp3"), "c1");
        var scan = Scan(Group(copy1), Group(copy2));
        Assert.False(ClipIndexMatch.Match([dead], scan).LabelsOf(Group(copy2)).Any);

        var starred = Entry(copy1, favorite: true);
        var match = ClipIndexMatch.Match([dead, starred], scan);

        Assert.False(match.LabelsOf(Group(copy2)).Any);
        Assert.True(match.LabelsOf(Group(copy1)).Favorite);
        Assert.Empty(match.LabelsOf(Group(copy1)).CollectionIds);
    }

    [Fact]
    public void ARenamedFile_TakesTheLabelsOfTheEntryWithItsSizeAndDate_NotThoseOfTheDeadOneWhoseNameItTook()
    {
        var pets = Entry(File("a.mp3", size: 1000), "c1");
        var dogs = Entry(File("b.mp3", size: 2000), "c2");
        var renamed = Group(File("b.mp3", size: 1000));

        var match = ClipIndexMatch.Match([pets, dogs], Scan(renamed));

        Assert.Equal(["c1"], match.LabelsOf(renamed).CollectionIds);
    }

    [Fact]
    public void ARenamedFile_KeepsThatChoice_OnceTheActionHasWrittenIt()
    {
        var pets = Entry(File("a.mp3", size: 1000), "c1");
        var dogs = Entry(File("b.mp3", size: 2000), "c2");
        var renamed = Group(File("b.mp3", size: 1000));
        var scan = Scan(renamed);

        var settled = ClipIndexMatch.Settled([pets, dogs], scan, Now);
        var labels = ClipIndexMatch.Match(settled, scan).LabelsOf(renamed);

        Assert.Equal(["c1"], labels.CollectionIds);
        Assert.Equal(Path.Combine(Clips, "b.mp3"), pets.Files[0].Path);
        Assert.Equal(Now, dogs.MissingSince);
    }

    [Fact]
    public void AFileEditedInPlace_StaysItsEntrys_WhenTwoLostEntriesHaveItsNewSizeAndDate()
    {
        var edited = Entry(File("b.mp3", size: 2000), "c2");
        var lost1 = Entry(File("a1.mp3", size: 1000), "c1");
        var lost2 = Entry(File("a2.mp3", size: 1000), "c3");
        var group = Group(File("b.mp3", size: 1000));

        var labels = ClipIndexMatch.Match([edited, lost1, lost2], Scan(group)).LabelsOf(group);

        Assert.Equal(["c2"], labels.CollectionIds);
    }

    [Fact]
    public void AFileEditedInPlace_StaysItsEntrys_WhenNoLostEntryHasItsNewSizeAndDate()
    {
        var edited = Entry(File("b.mp3", size: 2000), "c2");
        var lost = Entry(File("a.mp3", size: 500), "c1");
        var group = Group(File("b.mp3", size: 1000));

        var labels = ClipIndexMatch.Match([edited, lost], Scan(group)).LabelsOf(group);

        Assert.Equal(["c2"], labels.CollectionIds);
    }

    [Fact]
    public void ATakenOverName_IsNotGivenAway_WhenAnotherFileHasTheSameSizeAndDate()
    {
        var edited = Entry(File("b.mp3", size: 2000), "c2");
        var lost = Entry(File("a.mp3", size: 1000), "c1");
        var taken = Group(File("b.mp3", size: 1000));
        var other = Group(File("c.mp3", size: 1000));

        var match = ClipIndexMatch.Match([edited, lost], Scan(taken, other));

        Assert.Equal(["c2"], match.LabelsOf(taken).CollectionIds);
        Assert.False(match.LabelsOf(other).Any);
    }

    // END
}
