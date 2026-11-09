using System.IO;
using Hindsound.Library;
using Hindsound.Tests.Audio;
using Hindsound.Tests.Export;
using Xunit.Abstractions;

namespace Hindsound.Tests.Library;

/// <summary>What stays in memory between two looks when every clip is saved as two tracks. With no other test running, for the count of the heap to mean something.</summary>
[Collection(nameof(TimingSensitive))]
public sealed class LibraryReadsMemoryTwoTracksTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    public void Two_thousand_lines_of_two_tracks_keep_both_paths_of_each_and_a_little()
    {
        var clips = _root.Combine("clips");
        Directory.CreateDirectory(clips);
        var longestName = 0;
        for (var i = 0; i < ClipScanner.MaxLines; i++)
        {
            foreach (var track in new[] { "system", "mic" })
            {
                var path = Path.Combine(clips, $"2026-03-10 12-00-{i:D5} Some Application_{track}.mp3");
                File.WriteAllBytes(path, [1]);
                longestName = Math.Max(longestName, path.Length);
            }
        }

        var rig = new ReadsRig(_root.Combine("settings.json"), _ => TimeSpan.FromSeconds(7));
        var before = Heap();

        rig.Reads.Request(new ClipScanRequest { OutputFolder = clips, RescueFolder = _root.Combine("no-rescue-folder") });
        rig.Settle();
        var snapshot = rig.Last;

        var kept = Heap() - before;
        GC.KeepAlive(rig);
        Assert.Equal(ClipScanner.MaxLines, snapshot.Lines.Lines.Count);
        Assert.All(snapshot.Lines.Lines, line => Assert.Equal(2, line.Files.Count));
        Assert.Equal(TimeSpan.FromSeconds(7), rig.Reads.LengthOf(snapshot.Lines.Lines[^1]));
        output.WriteLine($"{kept / 1024} KB kept for {ClipScanner.MaxLines} lines of two tracks, paths of {longestName} characters");

        // A line holds two paths, 2 bytes a character each, and the fixed part of a line, of its second file and of its duration.
        // About 1.1 MB for 2 000 lines at an ordinary path length: it goes with the window, not into what the app keeps closed.
        Assert.InRange(kept, 0, ClipScanner.MaxLines * (4L * longestName + 350));
    }

    private static long Heap()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
