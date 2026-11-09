using System.Diagnostics;
using System.IO;
using Hindsound.Library;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.Library;

/// <summary>How often the screen is told, and what the read does once the view has closed.</summary>
public sealed class LibraryReadsPacingTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private string Settings => _root.Combine("settings.json");

    private ClipScanRequest Folders => new() { OutputFolder = _root.Combine("clips"), RescueFolder = _root.Combine("no-rescue-folder") };

    private static ClipListing Many(int count)
    {
        var groups = new List<ClipGroup>(count);
        for (var i = 0; i < count; i++)
        {
            groups.Add(new ClipGroup([new ClipFile(@"C:\fake-clips\clip-" + i + ".mp3", 10 + i, Noon.AddSeconds(-i))]));
        }

        return new ClipListing(groups, [@"C:\fake-clips"]);
    }

    [Fact]
    public void Durations_read_one_after_the_other_tell_the_screen_once_in_a_while_and_not_once_each()
    {
        var listing = Many(200);
        var rig = new ReadsRig(Settings, _ => TimeSpan.FromSeconds(7), _ => listing);

        var clock = Stopwatch.StartNew();
        rig.Reads.Request(Folders);
        rig.Settle();
        clock.Stop();

        Assert.Equal(200, rig.LengthsAsked.Count);
        Assert.All(rig.Last.Lines.Lines, line => Assert.NotNull(rig.Reads.LengthOf(line)));

        // A paint of the window takes tens of ms: one telling for each 200 ms that went by, and the one at the end. The bound
        // follows the time that really went by, so a slow PC does not move it.
        Assert.InRange(rig.LengthsTold, 1, 1 + (int)(clock.ElapsedMilliseconds / 200));
    }

    // ---------------------------------------------------------------- the work that outlives the close

    private string Clip(string name)
    {
        var folder = _root.Combine("clips");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[10]);
        File.SetLastWriteTimeUtc(path, Noon);
        return path;
    }

    [Fact]
    public void The_close_tells_on_the_loop_when_the_work_it_cut_short_has_ended_and_after_the_action_is_written()
    {
        var path = Clip("a.mp3");
        var rig = new ReadsRig(Settings, _ => null);
        rig.HoldScan.Reset();
        var told = 0;
        var starsWhenTold = -1;

        rig.Reads.Act(LibraryAction.SetFavorite(new ClipGroup([new ClipFile(path, 10, Noon)]), true), Folders);
        rig.Wait(rig.ScanEntered);
        rig.Reads.Close(() =>
        {
            told++;
            starsWhenTold = new LibraryStore(Settings).Read().Data.Clips.Count(clip => clip.Favorite);
        });
        rig.Turn();
        Assert.Equal(0, told);

        rig.HoldScan.Set();
        rig.Settle();

        Assert.Equal(1, told);
        Assert.Equal(1, starsWhenTold);
        Assert.Empty(rig.Delivered);
    }

    [Fact]
    public void The_close_tells_nothing_when_no_work_was_left_to_end()
    {
        Clip("a.mp3");
        var rig = new ReadsRig(Settings, _ => null);
        rig.Reads.Request(Folders);
        rig.Settle();
        var told = 0;

        rig.Reads.Close(() => told++);
        rig.Settle();

        Assert.Equal(0, told);
    }

    [Fact]
    public void A_close_that_asks_to_be_told_is_told_once_however_many_looks_it_cut_short()
    {
        Clip("a.mp3");
        var rig = new ReadsRig(Settings, _ => null);
        rig.HoldScan.Reset();
        var told = 0;

        rig.Reads.Request(Folders);
        rig.Wait(rig.ScanEntered);
        rig.Reads.Request(Folders);
        rig.Reads.Close(() => told++);
        rig.HoldScan.Set();
        rig.Settle();

        Assert.Equal(1, told);
    }

    [Fact]
    public void A_telling_that_fails_on_the_loop_gives_the_journal_only_the_type_of_the_failure()
    {
        var path = Clip("Collection-Canary-7F3.mp3");
        var rig = new ReadsRig(Settings, _ => null);
        rig.HoldScan.Reset();

        rig.Reads.Act(LibraryAction.SetFavorite(new ClipGroup([new ClipFile(path, 10, Noon)]), true), Folders);
        rig.Wait(rig.ScanEntered);
        rig.Reads.Close(() => throw new IOException(@"C:\Collection-Canary-7F3\clip.mp3 is being used"));
        rig.HoldScan.Set();
        rig.Settle();

        var line = Assert.Single(rig.Journal);
        Assert.Contains("IOException", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Canary", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":\\", line, StringComparison.Ordinal);
    }
}
