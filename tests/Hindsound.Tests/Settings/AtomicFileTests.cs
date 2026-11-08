using System.Security.AccessControl;
using System.Security.Principal;
using Hindsound.Settings;

namespace Hindsound.Tests.Settings;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_folder);

    private string Target => Path.Combine(_folder, "file.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Write_MakesTheFolderAndTheFile()
    {
        var target = Path.Combine(_folder, "new", "file.json");

        AtomicFile.Write(target, "one");

        Assert.Equal("one", File.ReadAllText(target));
    }

    [Fact]
    public void Write_ReplacesTheFile_AndLeavesNoTemporaryFile()
    {
        AtomicFile.Write(Target, "one");

        AtomicFile.Write(Target, "two");

        Assert.Equal("two", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_folder));
    }

    [Fact]
    public void Write_AFailureThrows_WithTheFileAsItWasAndNoTemporaryFile()
    {
        AtomicFile.Write(Target, "one");

        using (new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => AtomicFile.Write(Target, "two"));
        }

        Assert.Equal("one", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_folder));
    }

    [Fact]
    public void Write_ReplacesTheFile_InsteadOfWritingInPlace()
    {
        AtomicFile.Write(Target, "one");

        // A reader that lets the file be replaced holds the old file: a replacement leaves it with the old text, while a
        // write in place would show it the new text (and a cut during that write would leave half a file).
        using var reader = new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        AtomicFile.Write(Target, "two");

        Assert.Equal("two", File.ReadAllText(Target));
        Assert.Equal("one", new StreamReader(reader).ReadToEnd());
    }

    [Fact]
    public void Write_BuildsTheTemporaryFileInTheFolderOfTheTarget()
    {
        AtomicFile.Write(Target, "one");

        // The folder refuses new files: the write must fail at the temporary file, so it is made in that folder (a
        // temporary file elsewhere could sit on another volume, where the replacement is not atomic).
        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var info = new DirectoryInfo(_folder);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(rule);
        info.SetAccessControl(acl);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => AtomicFile.Write(Target, "two"));
            Assert.Equal("one", File.ReadAllText(Target));
        }
        finally
        {
            acl.RemoveAccessRule(rule);
            info.SetAccessControl(acl);
        }
    }

    [Fact]
    public void FreeBackupPath_NeverNamesAnEarlierCopy()
    {
        Assert.Equal(Target + ".bak", AtomicFile.FreeBackupPath(Target));

        File.WriteAllText(Target + ".bak", "kept");
        Assert.Equal(Target + ".1.bak", AtomicFile.FreeBackupPath(Target));

        File.WriteAllText(Target + ".1.bak", "kept");
        Assert.Equal(Target + ".2.bak", AtomicFile.FreeBackupPath(Target));
    }
}
