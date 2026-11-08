using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>The edges of the matching: paths that differ only in case, and paths with no folder to speak of.</summary>
public sealed class ClipIndexMatchGuardsTests
{
    private static readonly DateTime T = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);

    private static LibraryClip Entry(string path, string collection) => new()
    {
        Files = [new LibraryFile { Path = path, Size = 1000, Written = T }],
        Collections = [collection],
    };

    [Fact]
    public void AFileNamedByAnEntry_IsNoCandidateForAnother_WhateverTheCaseOfItsPath()
    {
        // The entry wrote one case, the disk gives another: it is still the same file, and the entry's.
        var named = Entry(@"D:\Clips\a.mp3", "c1");
        var lost = Entry(@"D:\Clips\gone.mp3", "c2");
        var group = new ClipGroup([new ClipFile(@"d:\clips\A.MP3", 1000, T)]);

        var labels = ClipIndexMatch.Match([named, lost], new ClipScan([group], [@"d:\clips"])).LabelsOf(group);

        Assert.Equal(["c1"], labels.CollectionIds);
    }

    [Fact]
    public void APathWithNoFolder_IsNeitherReadNorGone_AndNothingThrows()
    {
        var root = Entry(@"D:\", "c1");
        var relative = Entry("a.mp3", "c2");
        var scan = new ClipScan([], [@"D:\Clips"]);

        var kept = ClipIndexMatch.Settled([root, relative], scan, Now.AddDays(90));

        Assert.Equal(2, kept.Count);
        Assert.Null(root.MissingSince);
        Assert.Null(relative.MissingSince);
    }

    [Fact]
    public void FoldersCited_LeavesOutAPathWithNoFolder()
    {
        var folders = ClipIndexMatch.FoldersCited([Entry(@"D:\", "c1"), Entry("a.mp3", "c2"), Entry(@"D:\Clips\a.mp3", "c3"), Entry(@"d:\clips\b.mp3", "c3")]);

        Assert.Equal([@"D:\Clips"], folders);
    }
}
