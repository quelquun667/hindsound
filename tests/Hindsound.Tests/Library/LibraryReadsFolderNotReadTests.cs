using System.IO;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>A clip is called gone only when its folder was read and the clip was not in it.</summary>
public sealed class LibraryReadsFolderNotReadTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private string Clips => _root.Combine("clips");

    private string Settings => _root.Combine("settings.json");

    private string LibraryFile => _root.Combine("library.json");

    private ClipScanRequest Folders => new() { OutputFolder = Clips, RescueFolder = _root.Combine("no-rescue-folder") };

    private static string Clip(string folder, string name, DateTime written)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[10]);
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    private static ClipGroup LineOf(string path) => new([new ClipFile(path, 10, Noon)]);

    private LibraryActed Act(ReadsRig rig, LibraryAction action, ClipScanRequest? folders = null)
    {
        rig.Reads.Act(action, folders ?? Folders);
        rig.Settle();
        return rig.Last.Acted!;
    }

    [Fact]
    public void A_clip_held_back_for_a_save_in_progress_is_not_called_gone_and_nothing_is_written()
    {
        // The clip before this one ended less than 2 s before the shortcut was pressed for the next.
        var started = DateTime.UtcNow;
        var ended = Clip(Clips, "ended.mp3", started.AddSeconds(-1));
        var rig = new ReadsRig(Settings, _ => null);

        var acted = Act(rig, LibraryAction.SetFavorite(LineOf(ended), true), Folders with { ClipStartedUtc = started });

        Assert.Equal(LibraryOutcome.FolderNotRead, acted.Outcome);
        Assert.Empty(rig.Last.FoldersRead);
        Assert.False(File.Exists(LibraryFile));
        Assert.True(File.Exists(ended));
    }

    [Fact]
    public void A_folder_that_is_absent_or_refused_is_not_called_a_clip_gone_either()
    {
        var elsewhere = Path.Combine(_root.Combine("not-a-folder-at-all"), "a.mp3");
        Clip(Clips, "other.mp3", Noon);
        var rig = new ReadsRig(Settings, _ => null);

        var acted = Act(rig, LibraryAction.SetInCollection(LineOf(elsewhere), "c1", true));

        Assert.Equal(LibraryOutcome.FolderNotRead, acted.Outcome);
        Assert.Equal([Clips], rig.Last.FoldersRead);
        Assert.False(File.Exists(LibraryFile));
    }

    [Fact]
    public void A_clip_missing_from_a_folder_that_was_read_is_still_gone()
    {
        var path = Clip(Clips, "a.mp3", Noon);
        var rig = new ReadsRig(Settings, _ => null);
        File.Delete(path);

        var acted = Act(rig, LibraryAction.SetFavorite(LineOf(path), true));

        Assert.Equal(LibraryOutcome.ClipGone, acted.Outcome);
        Assert.Equal([Clips], rig.Last.FoldersRead);
    }

    [Fact]
    public void A_line_with_no_file_is_still_gone()
    {
        Clip(Clips, "a.mp3", Noon);
        var rig = new ReadsRig(Settings, _ => null);

        var acted = Act(rig, LibraryAction.SetFavorite(new ClipGroup([]), true));

        Assert.Equal(LibraryOutcome.ClipGone, acted.Outcome);
    }
}
