using Hindsound.Export;
using Hindsound.Library;

namespace Hindsound.Tests.Library;

public sealed class ClipFoldersRescuedTests
{
    private const string Output = @"C:\Fake\Clips", Rescue = @"C:\Fake\Rescued";

    private static readonly ClipScanRequest Folders = new() { OutputFolder = Output, RescueFolder = Rescue };

    private static ClipGroup Line(params string[] paths) =>
        new(paths.Select(path => new ClipFile(path, 10, DateTime.UnixEpoch)).ToArray());

    [Fact]
    public void A_clip_in_the_rescue_folder_is_rescued()
    {
        Assert.True(ClipFolders.IsRescued(Line(@"C:\Fake\Rescued\a.mp3"), Folders));
        Assert.True(ClipFolders.IsRescued(Line(@"C:\Fake\Rescued\a_system.mp3", @"C:\Fake\Rescued\a_mic.mp3"), Folders));
    }

    [Fact]
    public void A_clip_anywhere_else_is_not()
    {
        Assert.False(ClipFolders.IsRescued(Line(@"C:\Fake\Clips\a.mp3"), Folders));
        Assert.False(ClipFolders.IsRescued(Line(@"C:\Fake\Rescued-not\a.mp3"), Folders));
        Assert.False(ClipFolders.IsRescued(Line(@"C:\Fake\Rescued\deeper\a.mp3"), Folders));
    }

    [Fact]
    public void The_folder_is_the_same_under_another_spelling()
    {
        Assert.True(ClipFolders.IsRescued(Line(@"c:\FAKE\rescued\a.mp3"), Folders));
        Assert.True(ClipFolders.IsRescued(Line("C:/Fake/Rescued/a.mp3"), Folders));
        Assert.True(ClipFolders.IsRescued(Line(@"\\?\C:\Fake\Rescued\a.mp3"), Folders));
        Assert.True(ClipFolders.IsRescued(Line(@"C:\Fake\Rescued\a.mp3"), Folders with { RescueFolder = @"c:\fake\rescued\" }));
    }

    [Fact]
    public void With_the_rescue_folder_as_the_output_folder_no_clip_is_rescued()
    {
        var same = Folders with { OutputFolder = @"c:\fake\RESCUED\" };

        Assert.False(ClipFolders.IsRescued(Line(@"C:\Fake\Rescued\a.mp3"), same));
    }

    [Fact]
    public void An_output_folder_no_clip_can_go_to_leaves_the_rescued_clips_rescued()
    {
        // A relative folder sends the clip to the rescue folder: that is the very case of the badge.
        var unusable = Folders with { OutputFolder = @"relative\folder" };

        Assert.True(ClipFolders.IsRescued(Line(@"C:\Fake\Rescued\a.mp3"), unusable));
    }

    [Fact]
    public void The_default_rescue_folder_counts()
    {
        var path = System.IO.Path.Combine(ClipSaver.DefaultRescueFolder, "a.mp3");

        Assert.True(ClipFolders.IsRescued(Line(path), new ClipScanRequest { OutputFolder = Output }));
    }

    [Fact]
    public void A_line_with_no_file_is_not_rescued()
    {
        Assert.False(ClipFolders.IsRescued(new ClipGroup([]), Folders));
    }

    [Fact]
    public void A_rescue_folder_that_cannot_be_used_rescues_nothing()
    {
        Assert.False(ClipFolders.IsRescued(Line(@"C:\Fake\Rescued\a.mp3"), Folders with { RescueFolder = @"relative\folder" }));
    }
}
