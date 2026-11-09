using System.IO;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>
/// The attributes of a file decide whether it is a clip. A clip that is hidden is not one: the index takes it for gone,
/// and keeps its labels for a while in case it comes back.
/// </summary>
public sealed class ClipScannerAttributesTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _root = new();

    private string Clips => _root.Combine("clips");

    public ClipScannerAttributesTests()
    {
        Directory.CreateDirectory(Clips);
    }

    public void Dispose()
    {
        // The default of a listing skips the hidden files, and they would stay read-only or hidden for the delete.
        foreach (var file in Directory.EnumerateFiles(Clips, "*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        _root.Dispose();
    }

    private void Clip(string name, FileAttributes attributes)
    {
        var path = Path.Combine(Clips, name);
        File.WriteAllBytes(path, new byte[10]);
        File.SetLastWriteTimeUtc(path, Noon);
        File.SetAttributes(path, attributes);
    }

    private string[] Names() =>
        ClipScanner.Scan(new ClipScanRequest { OutputFolder = Clips, RescueFolder = _root.Combine("no-rescue-folder") })
            .Groups.SelectMany(g => g.Files).Select(f => Path.GetFileName(f.Path)).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    [Fact]
    public void A_hidden_file_and_a_system_file_are_not_clips()
    {
        Clip("h.mp3", FileAttributes.Hidden);
        Clip("s.mp3", FileAttributes.System);
        Clip("hs.wav", FileAttributes.Hidden | FileAttributes.System);

        Assert.Empty(Names());
    }

    [Fact]
    public void A_read_only_file_and_a_file_kept_in_the_cloud_are_clips()
    {
        Clip("r.mp3", FileAttributes.ReadOnly);
        Clip("o.mp3", FileAttributes.Offline);
        Clip("n.mp3", FileAttributes.Normal);
        Clip("a.mp3", FileAttributes.Archive);

        Assert.Equal(["a.mp3", "n.mp3", "o.mp3", "r.mp3"], Names());
    }
}
