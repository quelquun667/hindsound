using System.IO;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>The scan and the index call a path by the same name, whatever the way it was written.</summary>
public sealed class ClipPathSpellingTests
{
    private const string Clips = @"D:\Clips";

    private static readonly DateTime T = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);

    private static ClipFile File(string path, long size = 1000) => new(path, size, T);

    private static LibraryFile Known(ClipFile file) => new() { Path = file.Path, Size = file.Size, Written = file.WrittenUtc };

    private static LibraryClip Entry(string path, string collection = "c1") => new() { Files = [Known(File(path))], Collections = [collection] };

    [Theory]
    [InlineData(@"D:\Clips", @"D:/Clips")]
    [InlineData(@"D:\Clips", @"D:\Clips\")]
    [InlineData(@"D:\Clips", @"\\?\D:\Clips")]
    [InlineData(@"D:\Clips", @"\\.\D:\Clips")]
    [InlineData(@"D:\Clips", @"\\.\D:/Clips/")]
    [InlineData(@"\\server\share\Clips", @"\\?\UNC\server\share\Clips")]
    [InlineData(@"\\server\share\Clips", @"\\.\UNC\server\share\Clips")]
    [InlineData(@"\\server\share\Clips", @"//server/share/Clips")]
    [InlineData(@"D:\Clips\a.mp3", @"\\?\D:\Clips\a.mp3")]
    [InlineData(@"D:\Clips\a.mp3", @"D:/Clips/a.mp3")]
    public void TwoSpellings_ShareOneKey(string plain, string other)
    {
        Assert.Equal(ClipFolders.Key(plain), ClipFolders.Key(other));
    }

    [Theory]
    [InlineData(@"D:\Clips", @"D:\Other")]
    [InlineData(@"D:\Clips", @"E:\Clips")]
    [InlineData(@"\\server\share\Clips", @"\\?\D:\server\share\Clips")]
    [InlineData(@"\\server\share\Clips", @"\\server\share\Other")]
    public void TwoFolders_DoNotShareOne(string first, string second)
    {
        Assert.NotEqual(ClipFolders.Key(first), ClipFolders.Key(second));
    }

    [Theory]
    [InlineData(@"\\?\D:\Clips\a.mp3", @"D:\Clips\a.mp3")]
    [InlineData(@"D:\Clips\a.mp3", @"\\?\D:\Clips\a.mp3")]
    [InlineData(@"D:/Clips/a.mp3", @"D:\Clips\a.mp3")]
    [InlineData(@"D:\Clips\a.mp3", @"\\.\D:\Clips\a.mp3")]
    public void AnEntryUnderAnotherSpellingOfItsPath_IsKnownByItsName(string entryPath, string onDisk)
    {
        var copy = new ClipGroup([File(Path.Combine(Clips, "copy.mp3"))]);
        var file = new ClipGroup([File(onDisk)]);
        var scan = new ClipScan([file, copy], [Clips]);

        var match = ClipIndexMatch.Match([Entry(entryPath)], scan);

        Assert.Equal(["c1"], match.LabelsOf(file).CollectionIds);
        Assert.False(match.LabelsOf(copy).Any);
    }

    [Theory]
    [InlineData(@"\\?\D:\Clips\a.mp3", @"D:\Clips")]
    [InlineData(@"D:\Clips\a.mp3", @"\\?\D:\Clips")]
    [InlineData(@"D:/Clips/a.mp3", @"D:\Clips")]
    [InlineData(@"D:\Clips\a.mp3", @"\\.\D:\Clips\")]
    public void AClipGone_IsCountedWhicheverWayItsFolderWasSpelled(string entryPath, string folderRead)
    {
        var entry = Entry(entryPath);

        var kept = ClipIndexMatch.Settled([entry], new ClipScan([], [folderRead]), Now);

        Assert.Equal(Now, Assert.Single(kept).MissingSince);
    }

    [Fact]
    public void AnEditedFileOfAnEntryWithOtherSeparators_StaysItsEntrys()
    {
        var edited = new ClipGroup([File(Path.Combine(Clips, "a.mp3"), size: 2000)]);
        var entry = Entry("D:/Clips/a.mp3");

        var kept = ClipIndexMatch.Settled([entry], new ClipScan([edited], [Clips]), Now);

        Assert.Null(Assert.Single(kept).MissingSince);
        Assert.Equal(Path.Combine(Clips, "a.mp3"), entry.Files[0].Path);
    }

    [Fact]
    public void AFolderWrittenWithTheDevicePrefix_IsReadOnce()
    {
        using var root = new TempFolder();
        var plain = root.Combine("clips");
        Directory.CreateDirectory(plain);

        var folders = ClipScanner.FoldersToRead(new ClipScanRequest { OutputFolder = plain, KnownFolders = [@"\\.\" + plain, @"\\?\" + plain], RescueFolder = root.Combine("none") });

        Assert.Equal(2, folders.Count);
        Assert.Single(folders, folder => string.Equals(folder, plain, StringComparison.OrdinalIgnoreCase));
    }
}
