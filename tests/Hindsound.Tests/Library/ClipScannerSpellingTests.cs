using System.IO;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>The ways a folder can be written: one folder is read once, whatever its spelling.</summary>
public sealed class ClipScannerSpellingTests : IDisposable
{
    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    public void The_long_form_of_a_folder_is_the_same_folder()
    {
        var clips = _root.Combine("clips");
        Directory.CreateDirectory(clips);
        File.WriteAllBytes(Path.Combine(clips, "a.mp3"), new byte[10]);

        var listing = ClipScanner.Scan(new ClipScanRequest
        {
            OutputFolder = clips,
            RescueFolder = _root.Combine("no-rescue-folder"),
            KnownFolders = [@"\\?\" + clips, @"\\?\" + clips + @"\"],
        });

        Assert.Single(listing.Groups);
        Assert.Single(listing.FoldersRead);
    }

    [Fact]
    public void The_long_form_of_a_folder_has_the_key_of_its_short_form()
    {
        Assert.Equal(ClipFolders.Key(@"\\server\share\Clips"), ClipFolders.Key(@"\\?\UNC\server\share\Clips"));
        Assert.Equal(ClipFolders.Key(@"D:\Clips"), ClipFolders.Key(@"\\?\D:\Clips\"));
        Assert.NotEqual(ClipFolders.Key(@"D:\Clips"), ClipFolders.Key(@"D:\Other"));
        Assert.NotEqual(ClipFolders.Key(@"\\server\share\Clips"), ClipFolders.Key(@"\\?\D:\server\share\Clips"));
    }
}
