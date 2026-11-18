using System.Diagnostics;
using System.Runtime.Versioning;
using Hindsound.Library;
using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The trash on a real Linux, with the file system of the CI: the answers of statx, the rename that never replaces and never
/// copies, the files and links a trash takes. Everything happens in a folder of its own under the temporary folder, with the
/// data folder given by the test: the real home of the user is never read, and its trash never touched.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class FreedesktopTrashOnLinuxTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 18, 9, 5, 7);

    private readonly LinuxTrashFileSystem _files = new();
    private readonly List<string> _journal = [];
    private readonly string _root;

    public FreedesktopTrashOnLinuxTests()
    {
        // The real path: a temporary folder reached through a link would be written by what it resolves to.
        _root = _files.RealPath(Directory.CreateTempSubdirectory("hindsound-trash-").FullName)!;
        Directory.CreateDirectory(Clips);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Clips => Path.Combine(_root, "clips");

    private string DataHome => Path.Combine(_root, "data");

    private string Trash => Path.Combine(DataHome, "Trash");

    private FreedesktopTrash Bin() => new(_journal.Add, _files, DataHome, () => Now);

    private string Clip(string name, string text = "audio")
    {
        var path = Path.Combine(Clips, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void AFileGoesToTheHomeTrashWithItsInfoAndLeavesNoCopy()
    {
        var clip = Clip("a.wav", "the sound");

        Assert.Equal(RecycleOutcome.Recycled, Bin().Recycle([clip]));

        Assert.False(File.Exists(clip));
        Assert.Equal("the sound", File.ReadAllText(Path.Combine(Trash, "files", "a.wav")));
        Assert.Equal(
            $"[Trash Info]\nPath={TrashInfo.EncodePath(clip)}\nDeletionDate=2026-11-18T09:05:07\n",
            File.ReadAllText(Path.Combine(Trash, "info", "a.wav.trashinfo")));
        Assert.Empty(_journal);
    }

    [Fact]
    public void TheTrashFoldersAndTheInfoAreReadableByTheUserAlone()
    {
        var clip = Clip("a.wav");

        Bin().Recycle([clip]);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(DataHome));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Trash));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(Trash, "info")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(Trash, "files")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(Trash, "info", "a.wav.trashinfo")));
    }

    [Fact]
    public void ANameWithASpaceAHashAnAccentAndALineBreakIsEncodedAndKept()
    {
        var clip = Clip("my clip #1 \u00e9\n.wav");

        Assert.Equal(RecycleOutcome.Recycled, Bin().Recycle([clip]));

        var info = File.ReadAllText(Path.Combine(Trash, "info", "my clip #1 \u00e9\n.wav.trashinfo"));
        Assert.Contains("Path=" + TrashInfo.EncodePath(Clips) + "/my%20clip%20%231%20%C3%A9%0A.wav\n", info);
        Assert.Equal(3, info.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void TwoFilesOfOneNameKeepBothAndTheFirstIsNotTouched()
    {
        var first = Clip("a.wav", "first");
        Bin().Recycle([first]);
        var second = Clip("a.wav", "second");

        Assert.Equal(RecycleOutcome.Recycled, Bin().Recycle([second]));

        Assert.Equal("first", File.ReadAllText(Path.Combine(Trash, "files", "a.wav")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(Trash, "files", "a.2.wav")));
        Assert.True(File.Exists(Path.Combine(Trash, "info", "a.2.wav.trashinfo")));
    }

    [Fact]
    public void ALinkGoesAsALinkAndItsTargetStays()
    {
        var target = Path.Combine(_root, "target.wav");
        File.WriteAllText(target, "target");
        var link = Path.Combine(Clips, "link.wav");
        File.CreateSymbolicLink(link, target);

        Assert.Equal(RecycleOutcome.Recycled, Bin().Recycle([link]));

        var inTrash = new FileInfo(Path.Combine(Trash, "files", "link.wav"));
        Assert.Equal(target, inTrash.LinkTarget);
        Assert.Equal("target", File.ReadAllText(target));
        Assert.Null(new FileInfo(link).LinkTarget);
        Assert.False(File.Exists(link));
    }

    [Fact]
    public void ABrokenLinkGoesToo()
    {
        var link = Path.Combine(Clips, "broken.wav");
        File.CreateSymbolicLink(link, Path.Combine(_root, "nowhere.wav"));

        Assert.Equal(RecycleOutcome.Recycled, Bin().Recycle([link]));

        Assert.NotNull(new FileInfo(Path.Combine(Trash, "files", "broken.wav")).LinkTarget);
    }

    [Fact]
    public void AFileReachedByALinkedFolderIsWrittenWithItsRealPath()
    {
        var shortcut = Path.Combine(_root, "shortcut");
        Directory.CreateSymbolicLink(shortcut, Clips);
        Clip("a.wav");

        Assert.Equal(RecycleOutcome.Recycled, Bin().Recycle([Path.Combine(shortcut, "a.wav")]));

        Assert.Contains("Path=" + TrashInfo.EncodePath(Path.Combine(Clips, "a.wav")) + "\n", File.ReadAllText(Path.Combine(Trash, "info", "a.wav.trashinfo")));
        Assert.False(File.Exists(Path.Combine(Clips, "a.wav")));
    }

    [Fact]
    public void ADataFolderThatIsALinkToAFolderIsUsed()
    {
        var real = Path.Combine(_root, "dotfiles");
        Directory.CreateDirectory(real);
        Directory.CreateSymbolicLink(DataHome, real);
        var clip = Clip("a.wav");

        Assert.Equal(RecycleOutcome.Recycled, Bin().Recycle([clip]));

        Assert.True(File.Exists(Path.Combine(real, "Trash", "files", "a.wav")));
    }

    [Fact]
    public void AMissingFileIsGoneAndAFolderIsLeftAlone()
    {
        var clip = Clip("a.wav");
        var folder = Path.Combine(Clips, "album");
        Directory.CreateDirectory(folder);

        Assert.Equal(RecycleOutcome.Gone, Bin().Recycle([clip, Path.Combine(Clips, "b.wav")]));
        Assert.Equal(RecycleOutcome.CannotRecycle, Bin().Recycle([folder]));

        Assert.True(File.Exists(clip));
        Assert.True(Directory.Exists(folder));
        Assert.False(Directory.Exists(Trash));
    }

    [Fact]
    public void AnInfoTakenByAnotherProgramGivesAnotherName()
    {
        Directory.CreateDirectory(Path.Combine(Trash, "info"));
        File.WriteAllText(Path.Combine(Trash, "info", "a.wav.trashinfo"), "theirs");
        var clip = Clip("a.wav");

        Assert.Equal(RecycleOutcome.Recycled, Bin().Recycle([clip]));

        Assert.Equal("theirs", File.ReadAllText(Path.Combine(Trash, "info", "a.wav.trashinfo")));
        Assert.True(File.Exists(Path.Combine(Trash, "files", "a.2.wav")));
    }

    [Fact]
    public void AFolderThatCannotBeWrittenLeavesTheFileAndSaysCodesOnly()
    {
        var clip = Clip("secret-name.wav");
        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            // A user that is root writes into any folder: nothing to look at then.
            if (Environment.UserName == "root")
            {
                return;
            }

            var outcome = new FreedesktopTrash(_journal.Add, _files, Path.Combine(locked, "data"), () => Now).Recycle([clip]);

            Assert.Equal(RecycleOutcome.CannotRecycle, outcome);
            Assert.True(File.Exists(clip));
            Assert.All(_journal, line => Assert.DoesNotContain("secret-name", line));
            Assert.NotEmpty(_journal);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void TheTrashOfTheUserIsTheOneUnderTheDataFolderTheSystemGives()
    {
        var clip = Clip("a.wav");
        var bin = FreedesktopTrash.ForUser(folder => folder == Environment.SpecialFolder.LocalApplicationData ? DataHome : null, _journal.Add);

        Assert.Equal(RecycleOutcome.Recycled, bin.Recycle([clip]));

        Assert.True(File.Exists(Path.Combine(Trash, "files", "a.wav")));
    }

    [Fact]
    public void WithoutADataFolderTheFileStays()
    {
        var clip = Clip("a.wav");
        var bin = FreedesktopTrash.ForUser(_ => "", _journal.Add);

        Assert.Equal(RecycleOutcome.CannotRecycle, bin.Recycle([clip]));

        Assert.True(File.Exists(clip));
    }

    // Another file system: /dev/shm is a tmpfs on a CI runner, where the temporary folder is on the disk. Where it is not
    // different, or not writable, there is nothing to look at.

    [Fact]
    public void AFileOnAnotherFileSystemGoesToTheTrashOfItsTopDirectoryAndNeverToTheHome()
    {
        const string Shm = "/dev/shm";
        if (!Directory.Exists(Shm) || _files.Stat(Shm, follow: true).Device == _files.Stat(_root, follow: true).Device)
        {
            return;
        }

        var own = $"{Shm}/.Trash-{_files.UserId}";
        var ownExisted = Directory.Exists(own);
        var folder = Path.Combine(Shm, "hindsound-trash-" + Guid.NewGuid().ToString("N"));
        var name = Path.GetFileName(folder) + ".wav";
        try
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            var clip = Path.Combine(folder, name);
            File.WriteAllText(clip, "on tmpfs");

            var outcome = Bin().Recycle([clip]);

            // When the top directory refuses a folder of ours (no write there), the file must be where it was.
            if (outcome == RecycleOutcome.CannotRecycle)
            {
                Assert.True(File.Exists(clip));
                return;
            }

            Assert.Equal(RecycleOutcome.Recycled, outcome);
            Assert.False(File.Exists(clip));
            Assert.Equal("on tmpfs", File.ReadAllText(Path.Combine(own, "files", name)));
            Assert.Contains($"Path={TrashInfo.EncodePath(Path.GetFileName(folder))}/{name}\n", File.ReadAllText(Path.Combine(own, "info", name + ".trashinfo")));
            Assert.False(Directory.Exists(Trash));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(own));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            if (!ownExisted)
            {
                if (Directory.Exists(own))
                {
                    Directory.Delete(own, recursive: true);
                }
            }
            else
            {
                File.Delete(Path.Combine(own, "files", name));
                File.Delete(Path.Combine(own, "info", name + ".trashinfo"));
            }
        }
    }

    // The file system itself.

    [Fact]
    public void StatTellsTheKindTheOwnerTheModeAndTheDeviceOfFilesLinksAndFolders()
    {
        var clip = Clip("a.wav");
        File.SetUnixFileMode(clip, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        var link = Path.Combine(Clips, "link");
        File.CreateSymbolicLink(link, Clips);

        var file = _files.Stat(clip, follow: false);
        var linkItself = _files.Stat(link, follow: false);
        var linkFollowed = _files.Stat(link, follow: true);

        Assert.Equal(TrashKind.File, file.Kind);
        Assert.Equal(_files.UserId, file.Owner);
        Assert.Equal(0x1A0u, file.Mode);
        Assert.Equal(TrashKind.Symlink, linkItself.Kind);
        Assert.Equal(TrashKind.Directory, linkFollowed.Kind);
        Assert.Equal(file.Device, _files.Stat(Clips, follow: true).Device);
        Assert.Equal(Errno.NoEntry, _files.Stat(Path.Combine(Clips, "none"), follow: false).Error);
        Assert.Equal(Errno.NotDirectory, _files.Stat(Path.Combine(clip, "deeper"), follow: false).Error);
    }

    [Fact]
    public void TheStickyBitIsSeen()
    {
        var folder = Path.Combine(_root, "sticky");
        Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.StickyBit);

        Assert.True(_files.Stat(folder, follow: false).Sticky);
        Assert.False(_files.Stat(Clips, follow: false).Sticky);
    }

    [Fact]
    public async Task TheDeviceIsTheOneTheStatCommandReports()
    {
        // The layout of struct statx is read by offsets: the device of a few folders must be the one stat(1) prints.
        foreach (var folder in new[] { _root, "/", "/proc", "/dev/shm" }.Where(Directory.Exists))
        {
            var ours = _files.Stat(folder, follow: true);
            var major = ours.Device >> 32;
            var minor = ours.Device & 0xFFFFFFFF;
            var expected = ((major & 0xfff) << 8) | (minor & 0xff) | ((minor & ~0xffUL) << 12) | ((major & ~0xfffUL) << 32);

            Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), await StatDevice(folder));
        }
    }

    private static async Task<string> StatDevice(string folder)
    {
        var start = new ProcessStartInfo("stat") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-L");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("%d");
        start.ArgumentList.Add(folder);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var text = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return text.Trim();
    }

    [Fact]
    public void RealPathResolvesLinksAndAnswersNullForAMissingPath()
    {
        var link = Path.Combine(_root, "shortcut");
        Directory.CreateSymbolicLink(link, Clips);

        Assert.Equal(Clips, _files.RealPath(link));
        Assert.Null(_files.RealPath(Path.Combine(_root, "none")));
    }

    [Fact]
    public void MakeDirectoryMakesOneFolderOfTheUserAloneAndSaysWhenItExists()
    {
        var folder = Path.Combine(_root, "made");

        Assert.Equal(0, _files.MakeDirectory(folder));
        Assert.Equal(Errno.Exists, _files.MakeDirectory(folder));
        Assert.Equal(Errno.NoEntry, _files.MakeDirectory(Path.Combine(_root, "none", "deeper")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(folder));
    }

    [Fact]
    public void CreateFileNeverReplacesAFileAndWritesUtf8WithoutAMark()
    {
        var path = Path.Combine(_root, "info.trashinfo");

        Assert.Equal(0, _files.CreateFile(path, "caf\u00e9\n"));
        Assert.Equal(Errno.Exists, _files.CreateFile(path, "other"));

        Assert.Equal([(byte)'c', (byte)'a', (byte)'f', 0xC3, 0xA9, (byte)'\n'], File.ReadAllBytes(path));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void CreateFileInAFolderThatCannotBeWrittenAnswersExistsForAFileThereAndKeepsIt()
    {
        var folder = Path.Combine(_root, "info");
        Directory.CreateDirectory(folder);
        var theirs = Path.Combine(folder, "a.wav.trashinfo");
        File.WriteAllText(theirs, "theirs");
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            // A user that is root writes into any folder: nothing to look at then.
            if (Environment.UserName == "root")
            {
                return;
            }

            // The file exists: the answer is Exists, not a refusal to write, and the file of another is not removed.
            Assert.Equal(Errno.Exists, _files.CreateFile(theirs, "ours"));
            Assert.Equal(Errno.Access, _files.CreateFile(Path.Combine(folder, "b.wav.trashinfo"), "ours"));

            Assert.Equal("theirs", File.ReadAllText(theirs));
            Assert.Equal(["a.wav.trashinfo"], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void RenameNeverReplacesWhatIsAtTheDestination()
    {
        var from = Clip("from.wav", "from");
        var to = Clip("to.wav", "to");

        Assert.Equal(Errno.Exists, _files.Rename(from, to));

        Assert.Equal("from", File.ReadAllText(from));
        Assert.Equal("to", File.ReadAllText(to));
        Assert.Equal(0, _files.Rename(from, Path.Combine(Clips, "moved.wav")));
        Assert.False(File.Exists(from));
    }

    [Fact]
    public void RenameToAnotherFileSystemFailsAndMovesNothing()
    {
        const string Shm = "/dev/shm";
        if (!Directory.Exists(Shm) || _files.Stat(Shm, follow: true).Device == _files.Stat(_root, follow: true).Device)
        {
            return;
        }

        var folder = Path.Combine(Shm, "hindsound-trash-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var from = Clip("from.wav", "from");
            var to = Path.Combine(folder, "to.wav");

            Assert.Equal(Errno.CrossDevice, _files.Rename(from, to));

            Assert.Equal("from", File.ReadAllText(from));
            Assert.False(File.Exists(to));
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
