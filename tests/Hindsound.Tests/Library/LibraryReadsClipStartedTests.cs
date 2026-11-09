using Hindsound.Tests.Export;
using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>The moment a clip started is taken when a look starts, not when it was asked for.</summary>
public sealed class LibraryReadsClipStartedTests : IDisposable
{
    private static readonly DateTime Pressed = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private ClipScanRequest Folders => new() { OutputFolder = _root.Combine("clips"), RescueFolder = _root.Combine("no-rescue-folder") };

    private ReadsRig Rig() =>
        new(_root.Combine("settings.json"), scan: _ => new ClipListing([], []));

    [Fact]
    public void A_look_kept_waiting_behind_another_one_hears_of_a_press_made_in_between()
    {
        var rig = Rig();
        rig.HoldScan.Reset();
        rig.Reads.Request(Folders);
        rig.Wait(rig.ScanEntered);

        rig.Reads.Request(Folders);
        DateTime? started = null;
        rig.Reads.ClipStarted = () => started;
        started = Pressed;
        rig.HoldScan.Set();
        rig.Settle();

        var asked = rig.Asked.ToArray();
        Assert.Equal(2, asked.Length);
        Assert.Null(asked[0].ClipStartedUtc);
        Assert.Equal(Pressed, asked[1].ClipStartedUtc);
    }

    [Fact]
    public void Without_a_source_the_moment_of_the_request_stands()
    {
        var rig = Rig();

        rig.Reads.Request(Folders with { ClipStartedUtc = Pressed });
        rig.Settle();

        Assert.Equal(Pressed, rig.Asked.Single().ClipStartedUtc);
    }
}
