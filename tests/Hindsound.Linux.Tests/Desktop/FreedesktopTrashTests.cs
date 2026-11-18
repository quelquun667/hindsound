using Hindsound.Library;
using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The rules of the trash of freedesktop.org against a file system in memory, so that they run on any system: which trash takes
/// a file, what is written, in which order, and what a failure leaves behind. What Linux itself answers is in
/// <see cref="FreedesktopTrashOnLinuxTests"/>.
/// </summary>
public sealed class FreedesktopTrashTests
{
    private const string DataHome = "/home/u/.local/share";
    private const string Trash = DataHome + "/Trash";
    private static readonly DateTime Now = new(2026, 11, 18, 9, 5, 7);

    private readonly FakeTrashFileSystem _fs = new();
    private readonly List<string> _journal = [];

    public FreedesktopTrashTests()
    {
        _fs.Folder(DataHome);
        _fs.Folder("/home/u/clips");
    }

    private FreedesktopTrash Bin(string? dataHome = DataHome) => new(_journal.Add, _fs, dataHome, () => Now);

    private RecycleOutcome Recycle(params string[] paths) => Bin().Recycle(paths);

    [Fact]
    public void AFileOfTheHomeFileSystemGoesToTheTrashOfTheHome()
    {
        _fs.File("/home/u/clips/a.wav");

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/home/u/clips/a.wav"));

        Assert.False(_fs.Exists("/home/u/clips/a.wav"));
        Assert.Equal("audio", _fs.TextOf(Trash + "/files/a.wav"));
        Assert.Equal("[Trash Info]\nPath=/home/u/clips/a.wav\nDeletionDate=2026-11-18T09:05:07\n", _fs.TextOf(Trash + "/info/a.wav.trashinfo"));
        Assert.Empty(_journal);
    }

    [Fact]
    public void TheTrashFoldersAreMadeReadableByTheUserAlone()
    {
        _fs.File("/home/u/clips/a.wav");

        Recycle("/home/u/clips/a.wav");

        Assert.Equal(0x1C0u, _fs.ModeOf(Trash));
        Assert.Equal(0x1C0u, _fs.ModeOf(Trash + "/info"));
        Assert.Equal(0x1C0u, _fs.ModeOf(Trash + "/files"));
        Assert.Equal(0x180u, _fs.ModeOf(Trash + "/info/a.wav.trashinfo"));
    }

    [Fact]
    public void ADataFolderThatDoesNotExistYetIsMade()
    {
        var fs = new FakeTrashFileSystem();
        fs.File("/home/u/clips/a.wav");

        var outcome = new FreedesktopTrash(_journal.Add, fs, "/home/u/.local/share", () => Now).Recycle(["/home/u/clips/a.wav"]);

        Assert.Equal(RecycleOutcome.Recycled, outcome);
        Assert.True(fs.Exists("/home/u/.local/share/Trash/files/a.wav"));
        Assert.Equal(0x1C0u, fs.ModeOf("/home/u/.local/share"));
    }

    [Fact]
    public void TheInfoIsMadeFirstThenTheFileIsRenamed()
    {
        _fs.File("/home/u/clips/a.wav");

        Recycle("/home/u/clips/a.wav");

        var steps = _fs.Steps.Where(step => step.StartsWith("create") || step.StartsWith("rename")).ToList();
        Assert.Equal(["create " + Trash + "/info/a.wav.trashinfo", "rename /home/u/clips/a.wav -> " + Trash + "/files/a.wav"], steps);
    }

    [Fact]
    public void ATrashInfoIsNeverRemovedOnSuccess()
    {
        _fs.File("/home/u/clips/a.wav");

        Recycle("/home/u/clips/a.wav");

        Assert.DoesNotContain(_fs.Steps, step => step.StartsWith("delete"));
    }

    [Fact]
    public void TheFilesOfAClipGoTogether()
    {
        _fs.File("/home/u/clips/a.wav");
        _fs.File("/home/u/clips/a.mic.wav", "mic");

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/home/u/clips/a.wav", "/home/u/clips/a.mic.wav"));

        Assert.Equal("mic", _fs.TextOf(Trash + "/files/a.mic.wav"));
        Assert.True(_fs.Exists(Trash + "/info/a.mic.wav.trashinfo"));
        Assert.True(_fs.Exists(Trash + "/files/a.wav"));
    }

    [Fact]
    public void NoFileIsAnEmptySuccessThatTouchesNothing()
    {
        Assert.Equal(RecycleOutcome.Recycled, Recycle());

        Assert.Empty(_fs.Steps);
    }

    [Fact]
    public void ANameInTheTrashIsNeverReused()
    {
        _fs.File("/home/u/clips/a.wav", "first");
        Recycle("/home/u/clips/a.wav");
        _fs.File("/home/u/clips/a.wav", "second");

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/home/u/clips/a.wav"));

        Assert.Equal("first", _fs.TextOf(Trash + "/files/a.wav"));
        Assert.Equal("second", _fs.TextOf(Trash + "/files/a.2.wav"));
        Assert.Contains("Path=/home/u/clips/a.wav\n", _fs.TextOf(Trash + "/info/a.2.wav.trashinfo"));
    }

    [Fact]
    public void ALeftoverInfoWithNoFileTakesTheNameToo()
    {
        _fs.Folder(Trash + "/info", mode: 0x1C0);
        _fs.Folder(Trash + "/files", mode: 0x1C0);
        _fs.File(Trash + "/info/a.wav.trashinfo", "[Trash Info]\n");
        _fs.File("/home/u/clips/a.wav");

        Recycle("/home/u/clips/a.wav");

        Assert.Equal("[Trash Info]\n", _fs.TextOf(Trash + "/info/a.wav.trashinfo"));
        Assert.True(_fs.Exists(Trash + "/files/a.2.wav"));
    }

    [Fact]
    public void ALeftoverFileWithNoInfoIsNotReplaced()
    {
        _fs.Folder(Trash + "/info", mode: 0x1C0);
        _fs.Folder(Trash + "/files", mode: 0x1C0);
        _fs.File(Trash + "/files/a.wav", "old");
        _fs.File("/home/u/clips/a.wav", "new");

        Recycle("/home/u/clips/a.wav");

        Assert.Equal("old", _fs.TextOf(Trash + "/files/a.wav"));
        Assert.Equal("new", _fs.TextOf(Trash + "/files/a.2.wav"));
    }

    [Fact]
    public void ABrokenLinkInTheTrashTakesTheNameToo()
    {
        _fs.Folder(Trash + "/info", mode: 0x1C0);
        _fs.Folder(Trash + "/files", mode: 0x1C0);
        _fs.Link(Trash + "/files/a.wav", "/nowhere");
        _fs.File("/home/u/clips/a.wav");

        Recycle("/home/u/clips/a.wav");

        Assert.True(_fs.Exists(Trash + "/files/a.2.wav"));
        Assert.Equal(TrashKind.Symlink, _fs.KindOf(Trash + "/files/a.wav"));
    }

    [Fact]
    public void ANameAnotherProgramTookBetweenTheLookAndTheCreationGivesTheNextName()
    {
        _fs.File("/home/u/clips/a.wav");
        var first = true;
        _fs.CreateFault = path =>
        {
            if (first && path.EndsWith("/a.wav.trashinfo"))
            {
                first = false;
                _fs.Folder(Trash + "/info", mode: 0x1C0);
                _fs.File(path, "theirs");
                return Errno.Exists;
            }

            return 0;
        };

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/home/u/clips/a.wav"));

        Assert.Equal("theirs", _fs.TextOf(Trash + "/info/a.wav.trashinfo"));
        Assert.True(_fs.Exists(Trash + "/files/a.2.wav"));
        Assert.True(_fs.Exists(Trash + "/info/a.2.wav.trashinfo"));
    }

    [Fact]
    public void AFailedRenameRemovesTheInfoAndLeavesTheFile()
    {
        _fs.File("/home/u/clips/a.wav");
        _fs.RenameFault = (_, _) => Errno.Io;

        var outcome = Recycle("/home/u/clips/a.wav");

        Assert.Equal(RecycleOutcome.CannotRecycle, outcome);
        Assert.True(_fs.Exists("/home/u/clips/a.wav"));
        Assert.False(_fs.Exists(Trash + "/info/a.wav.trashinfo"));
        Assert.False(_fs.Exists(Trash + "/files/a.wav"));
        Assert.Equal(["recycle bin: not done (move, errno 5)"], _journal);
    }

    [Fact]
    public void AFileOnAnotherFileSystemThanItsTrashIsLeftNotCopied()
    {
        // The trash of the home and the clips are on different devices, and the folder of the clips has no room for a trash.
        _fs.Folder("/mnt/usb", device: 2);
        _fs.Folder("/mnt/usb/.Trash-1000", device: 3);
        _fs.File("/mnt/usb/a.wav");

        var outcome = Recycle("/mnt/usb/a.wav");

        Assert.Equal(RecycleOutcome.CannotRecycle, outcome);
        Assert.True(_fs.Exists("/mnt/usb/a.wav"));
        Assert.DoesNotContain(_fs.Paths, path => path.EndsWith(".wav") && path.Contains("/files/"));
        Assert.DoesNotContain(_fs.Paths, path => path.EndsWith(".trashinfo"));
    }

    [Fact]
    public void WhenTheSecondFileFailsTheFirstIsPutBack()
    {
        _fs.File("/home/u/clips/a.wav");
        _fs.File("/home/u/clips/a.mic.wav");
        _fs.RenameFault = (from, _) => from.EndsWith("a.mic.wav") ? Errno.Io : 0;

        var outcome = Recycle("/home/u/clips/a.wav", "/home/u/clips/a.mic.wav");

        Assert.Equal(RecycleOutcome.CannotRecycle, outcome);
        Assert.True(_fs.Exists("/home/u/clips/a.wav"));
        Assert.True(_fs.Exists("/home/u/clips/a.mic.wav"));
        Assert.False(_fs.Exists(Trash + "/files/a.wav"));
        Assert.False(_fs.Exists(Trash + "/info/a.wav.trashinfo"));
        Assert.False(_fs.Exists(Trash + "/info/a.mic.wav.trashinfo"));
    }

    [Fact]
    public void WhenTheFirstCannotBePutBackItIsFailedAndTheJournalSaysSo()
    {
        _fs.File("/home/u/clips/a.wav");
        _fs.File("/home/u/clips/a.mic.wav");
        _fs.RenameFault = (from, to) => from.EndsWith("a.mic.wav") || to.EndsWith("/clips/a.wav") ? Errno.Io : 0;

        var outcome = Recycle("/home/u/clips/a.wav", "/home/u/clips/a.mic.wav");

        Assert.Equal(RecycleOutcome.Failed, outcome);
        Assert.True(_fs.Exists(Trash + "/files/a.wav"));
        Assert.True(_fs.Exists(Trash + "/info/a.wav.trashinfo"));
        Assert.Contains("recycle bin: put back failed (errno 5)", _journal);
    }

    [Fact]
    public void AFileAlreadyGoneIsGoneAndNothingMoves()
    {
        _fs.File("/home/u/clips/a.wav");

        var outcome = Recycle("/home/u/clips/a.wav", "/home/u/clips/b.wav");

        Assert.Equal(RecycleOutcome.Gone, outcome);
        Assert.True(_fs.Exists("/home/u/clips/a.wav"));
        Assert.Empty(_fs.Steps);
    }

    [Fact]
    public void AFolderIsNotTakenAndStaysWhereItIs()
    {
        _fs.Folder("/home/u/clips/album");

        Assert.Equal(RecycleOutcome.CannotRecycle, Recycle("/home/u/clips/album"));

        Assert.True(_fs.Exists("/home/u/clips/album"));
        Assert.Empty(_fs.Steps);
    }

    [Fact]
    public void ALinkIsSentAsALinkAndWhatItPointsToStays()
    {
        _fs.File("/home/u/clips/real.wav", "real");
        _fs.Link("/home/u/clips/link.wav", "/home/u/clips/real.wav");

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/home/u/clips/link.wav"));

        Assert.Equal(TrashKind.Symlink, _fs.KindOf(Trash + "/files/link.wav"));
        Assert.Equal("real", _fs.TextOf("/home/u/clips/real.wav"));
        Assert.False(_fs.Exists("/home/u/clips/link.wav"));
    }

    [Fact]
    public void ABrokenLinkIsSentToo()
    {
        _fs.Link("/home/u/clips/broken.wav", "/gone.wav");

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/home/u/clips/broken.wav"));

        Assert.True(_fs.Exists(Trash + "/files/broken.wav"));
    }

    [Fact]
    public void AFolderReachedByALinkIsWrittenWithItsRealPath()
    {
        _fs.Folder("/data/real");
        _fs.Link("/home/u/shortcut", "/data/real");
        _fs.File("/data/real/a.wav");

        Recycle("/home/u/shortcut/a.wav");

        Assert.Contains("Path=/data/real/a.wav\n", _fs.TextOf(Trash + "/info/a.wav.trashinfo"));
    }

    [Fact]
    public void ADataFolderThatIsALinkToAFolderIsUsed()
    {
        var fs = new FakeTrashFileSystem();
        fs.Folder("/dotfiles/share");
        fs.Link("/home/u/share", "/dotfiles/share");
        fs.File("/home/u/clips/a.wav");

        var outcome = new FreedesktopTrash(_journal.Add, fs, "/home/u/share", () => Now).Recycle(["/home/u/clips/a.wav"]);

        Assert.Equal(RecycleOutcome.Recycled, outcome);
        Assert.True(fs.Exists("/dotfiles/share/Trash/files/a.wav"));
    }

    [Fact]
    public void WithoutADataFolderNothingMovesAndTheFileStays()
    {
        _fs.File("/home/u/clips/a.wav");

        Assert.Equal(RecycleOutcome.CannotRecycle, Bin(null).Recycle(["/home/u/clips/a.wav"]));
        Assert.Equal(RecycleOutcome.CannotRecycle, Bin("relative/share").Recycle(["/home/u/clips/a.wav"]));

        Assert.True(_fs.Exists("/home/u/clips/a.wav"));
        Assert.Empty(_fs.Steps);
    }

    [Fact]
    public void ARelativePathIsRefused()
    {
        Assert.Equal(RecycleOutcome.CannotRecycle, Recycle("clips/a.wav"));

        Assert.Empty(_fs.Steps);
    }

    [Fact]
    public void AHomeTrashThatCannotBeMadeLeavesTheFile()
    {
        _fs.File("/home/u/clips/a.wav");
        _fs.MakeFault = _ => Errno.Access;

        Assert.Equal(RecycleOutcome.CannotRecycle, Recycle("/home/u/clips/a.wav"));

        Assert.True(_fs.Exists("/home/u/clips/a.wav"));
        Assert.Equal(["recycle bin: not done (folder home, errno 13)"], _journal);
    }

    [Fact]
    public void ATrashThatIsAFileIsNotUsed()
    {
        _fs.File(Trash);
        _fs.File("/home/u/clips/a.wav");

        Assert.Equal(RecycleOutcome.CannotRecycle, Recycle("/home/u/clips/a.wav"));

        Assert.True(_fs.Exists("/home/u/clips/a.wav"));
    }

    [Fact]
    public void AnExceptionOfTheFileSystemIsAFailureAndNotAThrow()
    {
        _fs.File("/home/u/clips/a.wav");
        _fs.StatThrows = path => path.EndsWith("a.wav");

        var outcome = Recycle("/home/u/clips/a.wav");

        Assert.Equal(RecycleOutcome.Failed, outcome);
        Assert.Single(_journal);
        Assert.StartsWith("recycle bin: failed (IOException, 0x", _journal[0]);
    }

    // The top directory trash of another file system.

    private void Usb()
    {
        _fs.Folder("/mnt/usb", device: 2);
        _fs.Folder("/mnt/usb/clips/live");
        _fs.File("/mnt/usb/clips/live/a b.wav");
    }

    [Fact]
    public void AFileOnAnotherFileSystemGoesToTheOwnTrashOfItsTopDirectory()
    {
        Usb();

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/mnt/usb/clips/live/a b.wav"));

        Assert.Equal("audio", _fs.TextOf("/mnt/usb/.Trash-1000/files/a b.wav"));
        Assert.Equal("[Trash Info]\nPath=clips/live/a%20b.wav\nDeletionDate=2026-11-18T09:05:07\n", _fs.TextOf("/mnt/usb/.Trash-1000/info/a b.wav.trashinfo"));
        Assert.Equal(0x1C0u, _fs.ModeOf("/mnt/usb/.Trash-1000"));
        Assert.Equal(0x1C0u, _fs.ModeOf("/mnt/usb/.Trash-1000/info"));
        Assert.Equal(0x1C0u, _fs.ModeOf("/mnt/usb/.Trash-1000/files"));
    }

    [Fact]
    public void TheTrashOfTheHomeIsNotMadeForAFileOnAnotherFileSystem()
    {
        Usb();

        Recycle("/mnt/usb/clips/live/a b.wav");

        Assert.False(_fs.Exists(Trash));
    }

    [Fact]
    public void ASharedTrashFolderWithTheStickyBitIsUsedForTheUser()
    {
        Usb();
        _fs.Folder("/mnt/usb/.Trash", mode: 0x3FF, owner: 0);

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/mnt/usb/clips/live/a b.wav"));

        Assert.True(_fs.Exists("/mnt/usb/.Trash/1000/files/a b.wav"));
        Assert.Contains("Path=clips/live/a%20b.wav\n", _fs.TextOf("/mnt/usb/.Trash/1000/info/a b.wav.trashinfo"));
        Assert.False(_fs.Exists("/mnt/usb/.Trash-1000"));
    }

    [Fact]
    public void ASharedTrashFolderWithoutTheStickyBitIsNotUsed()
    {
        Usb();
        _fs.Folder("/mnt/usb/.Trash", mode: 0x1FF, owner: 0);

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/mnt/usb/clips/live/a b.wav"));

        Assert.False(_fs.Exists("/mnt/usb/.Trash/1000"));
        Assert.True(_fs.Exists("/mnt/usb/.Trash-1000/files/a b.wav"));
    }

    [Fact]
    public void ASharedTrashFolderThatIsALinkIsNotUsed()
    {
        Usb();
        _fs.Folder("/elsewhere", mode: 0x3FF);
        _fs.Link("/mnt/usb/.Trash", "/elsewhere");

        Assert.Equal(RecycleOutcome.Recycled, Recycle("/mnt/usb/clips/live/a b.wav"));

        Assert.False(_fs.Exists("/elsewhere/1000"));
        Assert.True(_fs.Exists("/mnt/usb/.Trash-1000/files/a b.wav"));
    }

    [Fact]
    public void AnOwnTrashOfAnotherUserIsNotUsedAndTheFileStays()
    {
        Usb();
        _fs.Folder("/mnt/usb/.Trash-1000", owner: 1001);

        Assert.Equal(RecycleOutcome.CannotRecycle, Recycle("/mnt/usb/clips/live/a b.wav"));

        Assert.True(_fs.Exists("/mnt/usb/clips/live/a b.wav"));
        Assert.DoesNotContain(_fs.Steps, step => step.StartsWith("rename"));
    }

    [Fact]
    public void AnOwnTrashThatIsALinkIsNotUsedAndTheFileStays()
    {
        Usb();
        _fs.Folder("/elsewhere");
        _fs.Link("/mnt/usb/.Trash-1000", "/elsewhere");

        Assert.Equal(RecycleOutcome.CannotRecycle, Recycle("/mnt/usb/clips/live/a b.wav"));

        Assert.True(_fs.Exists("/mnt/usb/clips/live/a b.wav"));
    }

    [Fact]
    public void ATopDirectoryWhereNothingCanBeMadeLeavesTheFile()
    {
        Usb();
        _fs.MakeFault = path => path.StartsWith("/mnt/usb") ? Errno.Access : 0;

        Assert.Equal(RecycleOutcome.CannotRecycle, Recycle("/mnt/usb/clips/live/a b.wav"));

        Assert.True(_fs.Exists("/mnt/usb/clips/live/a b.wav"));
        Assert.Equal(["recycle bin: not done (folder own, errno 13)"], _journal);
    }

    [Fact]
    public void TheTopDirectoryIsTheHighestFolderOnTheSameDevice()
    {
        _fs.Folder("/mnt", device: 5);
        _fs.Folder("/mnt/disk", device: 6);
        _fs.File("/mnt/disk/deep/er/a.wav");

        Recycle("/mnt/disk/deep/er/a.wav");

        Assert.True(_fs.Exists("/mnt/disk/.Trash-1000/files/a.wav"));
        Assert.Contains("Path=deep/er/a.wav\n", _fs.TextOf("/mnt/disk/.Trash-1000/info/a.wav.trashinfo"));
    }

    [Fact]
    public void AFileAtTheTopOfItsFileSystemIsWrittenByItsNameAlone()
    {
        _fs.Folder("/mnt/usb", device: 2);
        _fs.File("/mnt/usb/a.wav");

        Recycle("/mnt/usb/a.wav");

        Assert.Contains("Path=a.wav\n", _fs.TextOf("/mnt/usb/.Trash-1000/info/a.wav.trashinfo"));
    }

    [Fact]
    public void AnotherUserGetsTheirOwnNumberedTrash()
    {
        Usb();
        _fs.UserId = 1234;

        Recycle("/mnt/usb/clips/live/a b.wav");

        Assert.True(_fs.Exists("/mnt/usb/.Trash-1234/files/a b.wav"));
    }

    // The journal: codes only.

    [Fact]
    public void TheJournalNeverHoldsAPathOrAName()
    {
        const string Secret = "Secret Clip Name";
        _fs.File($"/home/u/clips/{Secret}.wav");
        _fs.File($"/home/u/clips/{Secret}.mic.wav");
        var lines = new List<string>();

        foreach (var fault in new Action<FakeTrashFileSystem>[]
        {
            fs => fs.RenameFault = (_, _) => Errno.Io,
            fs => fs.CreateFault = _ => Errno.Access,
            fs => fs.MakeFault = _ => Errno.Access,
            fs => fs.StatThrows = path => path.Contains(Secret),
            fs => fs.RenameFault = (from, to) => from.Contains(".mic") || to.Contains("/clips/") ? Errno.Io : 0,
        })
        {
            var fs = new FakeTrashFileSystem();
            fs.Folder("/home/u/.local/share");
            fs.File($"/home/u/clips/{Secret}.wav");
            fs.File($"/home/u/clips/{Secret}.mic.wav");
            fault(fs);
            new FreedesktopTrash(lines.Add, fs, DataHome, () => Now).Recycle([$"/home/u/clips/{Secret}.wav", $"/home/u/clips/{Secret}.mic.wav"]);
        }

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain("Secret", line);
            Assert.DoesNotContain("/home", line);
            Assert.StartsWith("recycle bin: ", line);
        });
    }
}
