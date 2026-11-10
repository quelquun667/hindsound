using System.IO;
using Hindsound.App;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.App;

/// <summary>The probe reads the log while the app may be writing it from another thread.</summary>
public sealed class JournalSharedReadTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_log_reads_while_a_write_holds_it()
    {
        var path = _temp.Combine("settings.log");
        File.WriteAllText(path, "first\nsecond\n");

        // As File.AppendAllText holds it during a write.
        using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            Assert.Equal(["first", "second"], Journal.ReadShared(path));
        }
    }

    [Fact]
    public void A_write_gets_through_while_the_log_is_being_read()
    {
        var path = _temp.Combine("settings.log");
        File.WriteAllText(path, "first\n");
        var journal = new Journal(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            journal.Write("second");
        }

        Assert.Contains("second", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
