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
    public void FreeBackupPath_NeverNamesAnEarlierCopy()
    {
        Assert.Equal(Target + ".bak", AtomicFile.FreeBackupPath(Target));

        File.WriteAllText(Target + ".bak", "kept");
        Assert.Equal(Target + ".1.bak", AtomicFile.FreeBackupPath(Target));

        File.WriteAllText(Target + ".1.bak", "kept");
        Assert.Equal(Target + ".2.bak", AtomicFile.FreeBackupPath(Target));
    }
}
