using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.Tests.App;

/// <summary>
/// The pipeline tells when a clip starts, as it tells when one ends: the update card greys its buttons from the press on,
/// not only after the first clip is over.
/// </summary>
public class ClipStartTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private sealed class HeldBackend : IClipBackend
    {
        public readonly ManualResetEventSlim Release = new();
        public readonly ManualResetEventSlim Done = new();

        public AudioSnapshot Snapshot(TimeSpan duration)
        {
            Release.Wait(Wait);
            Done.Set();
            return new AudioSnapshot([], []);
        }

        public ClipResult Save(AudioSnapshot snapshot, ExportOptions options, string? appName) => throw new InvalidOperationException("nothing to save");
    }

    private static ClipRequest Request() => new(TimeSpan.FromSeconds(5), new ExportOptions(), null, false);

    [Fact]
    public void Press_says_a_clip_started_once_it_counts_as_busy()
    {
        var backend = new HeldBackend();
        var pipeline = new ClipPipeline(backend, _ => { }, (_, _) => { }, _ => { });
        var busyWhenTold = new List<bool>();
        pipeline.Started = () => busyWhenTold.Add(pipeline.Busy);

        pipeline.Press(Request());

        Assert.Equal([true], busyWhenTold);
        backend.Release.Set();
        Assert.True(backend.Done.Wait(Wait));
    }

    [Fact]
    public void Each_press_says_so()
    {
        var backend = new HeldBackend();
        var pipeline = new ClipPipeline(backend, _ => { }, (_, _) => { }, _ => { });
        var told = 0;
        pipeline.Started = () => told++;

        pipeline.Press(Request());
        pipeline.Press(Request());

        Assert.Equal(2, told);
        backend.Release.Set();
        Assert.True(backend.Done.Wait(Wait));
    }
}
