using System.IO;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>A clip held back for a save in progress is in its folder: that folder has not been read to its end.</summary>
public sealed class ClipScannerHeldBackTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _root = new();

    public ClipScannerHeldBackTests()
    {
        Directory.CreateDirectory(Clips);
    }

    private string Clips => _root.Combine("clips");

    public void Dispose() => _root.Dispose();

    private string Write(string name, DateTime written, int bytes = 10)
    {
        var path = Path.Combine(Clips, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    private ClipListing Scan(DateTime? started) =>
        ClipScanner.Scan(new ClipScanRequest { OutputFolder = Clips, RescueFolder = _root.Combine("no-rescue-folder"), ClipStartedUtc = started });

    [Fact]
    public void AFolderThatHoldsAFileBackForASave_IsNotRead()
    {
        Write("old.mp3", Noon);
        Write("new.mp3", Noon.AddMinutes(1));

        var listing = Scan(Noon.AddMinutes(1));

        Assert.Equal(["old.mp3"], listing.Groups.Select(g => Path.GetFileName(g.Main.Path)));
        Assert.Empty(listing.FoldersRead);
    }

    [Fact]
    public void AReservedEmptyFile_HoldsItsFolderBackToo()
    {
        Write("old.mp3", Noon);
        Write("reserved.mp3", Noon.AddMinutes(1), bytes: 0);

        Assert.Empty(Scan(Noon.AddMinutes(1)).FoldersRead);
    }

    [Fact]
    public void AFileOfTheMarginBeforeTheSave_HoldsItsFolderBack()
    {
        Write("a.mp3", Noon.AddSeconds(-1));

        Assert.Empty(Scan(Noon).FoldersRead);
    }

    [Fact]
    public void AFolderWithNoFileAtOrAfterTheStart_IsRead()
    {
        Write("a.mp3", Noon.AddSeconds(-3));

        Assert.Equal([Clips], Scan(Noon).FoldersRead);
    }

    [Fact]
    public void AFileThatIsNoClipOfOurs_HoldsNothingBack()
    {
        Write("notes.txt", Noon.AddMinutes(1));
        Directory.CreateDirectory(Path.Combine(Clips, "folder.mp3"));
        Write("a.mp3", Noon);

        Assert.Equal([Clips], Scan(Noon.AddMinutes(1)).FoldersRead);
    }

    [Fact]
    public void WithNoSaveInProgress_NothingIsHeldBack()
    {
        Write("a.mp3", Noon.AddMinutes(1));

        Assert.Equal([Clips], Scan(null).FoldersRead);
    }
}
