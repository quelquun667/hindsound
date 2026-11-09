using System.IO;
using System.Runtime.CompilerServices;
using Hindsound.Library;
using Hindsound.Tests.Audio;
using Hindsound.Tests.Export;
using Xunit.Abstractions;

namespace Hindsound.Tests.Library;

/// <summary>What stays in memory between two looks: the lines under the cap, and not every clip found. With no other test running, for the count of the heap to mean something.</summary>
[Collection(nameof(TimingSensitive))]
public sealed class LibraryReadsMemoryTests(ITestOutputHelper output) : IDisposable
{
    private static readonly DateTime Noon = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private ClipScanRequest Folders => new() { OutputFolder = _root.Combine("clips"), RescueFolder = _root.Combine("no-rescue-folder") };

    [Fact]
    public void Two_thousand_lines_with_their_durations_keep_no_more_than_their_paths_and_a_little()
    {
        var clips = _root.Combine("clips");
        Directory.CreateDirectory(clips);
        var longestName = 0;
        for (var i = 0; i < ClipScanner.MaxLines; i++)
        {
            // The length of a name the app gives a clip by default.
            var path = Path.Combine(clips, $"2026-03-10 12-00-{i:D5} Some Application.mp3");
            File.WriteAllBytes(path, [1]);
            longestName = Math.Max(longestName, path.Length);
        }

        var rig = new ReadsRig(_root.Combine("settings.json"), _ => TimeSpan.FromSeconds(7));
        var before = Heap();

        rig.Reads.Request(Folders);
        rig.Settle();
        var snapshot = rig.Last;

        var kept = Heap() - before;
        GC.KeepAlive(rig);
        Assert.Equal(ClipScanner.MaxLines, snapshot.Lines.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(7), rig.Reads.LengthOf(snapshot.Lines.Lines[^1]));
        output.WriteLine($"{kept / 1024} KB kept for {ClipScanner.MaxLines} lines, paths of {longestName} characters");

        // The path is what weighs most, and depends on the PC: 2 bytes a character, and the fixed part of a line and of its duration.
        Assert.InRange(kept, 0, ClipScanner.MaxLines * (2L * longestName + 300));
    }

    [Fact]
    public void What_the_cap_left_out_is_not_kept()
    {
        var (rig, list, cut) = LookAtMany();

        // The list of every clip, and the ones past the cap: nothing on the loop nor in the reader holds them any longer.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(list.TryGetTarget(out _), "the whole listing is still held");
        Assert.False(cut.TryGetTarget(out _), "a clip past the cap is still held");
        Assert.Equal(ClipScanner.MaxLines, rig.Last.Lines.Lines.Count);
        Assert.Equal(500, rig.Last.Lines.LeftOut);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (ReadsRig Rig, WeakReference<IReadOnlyList<ClipGroup>> List, WeakReference<ClipGroup> Cut) LookAtMany()
    {
        var groups = new List<ClipGroup>();
        for (var i = 0; i < ClipScanner.MaxLines + 500; i++)
        {
            groups.Add(new ClipGroup([new ClipFile(@"C:\fake-clips\clip-" + i + ".mp3", 10, Noon.AddSeconds(-i))]));
        }

        var list = new WeakReference<IReadOnlyList<ClipGroup>>(groups);
        var cut = new WeakReference<ClipGroup>(groups[^1]);
        var given = groups;
        var rig = new ReadsRig(_root.Combine("settings.json"), _ => null, _ =>
        {
            var listing = new ClipListing(given!, [@"C:\fake-clips"]);
            given = null;
            return listing;
        });
        groups = null;
        rig.Reads.Request(Folders);
        rig.Settle();
        return (rig, list, cut);
    }

    private static long Heap()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
