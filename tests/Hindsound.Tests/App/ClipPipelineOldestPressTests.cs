using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.Tests.App;

/// <summary>The time the Library holds back files from is that of the oldest press still being written, not of the newest.</summary>
public sealed class ClipPipelineOldestPressTests
{
    private sealed class HeldBackend : IClipBackend
    {
        public readonly ManualResetEventSlim Release = new();

        public AudioSnapshot Snapshot(TimeSpan duration) => new(new float[2], []);

        ClipResult IClipBackend.Save(AudioSnapshot snapshot, ExportOptions options, string? appName)
        {
            Release.Wait(TimeSpan.FromSeconds(20));
            return new ClipResult(["clip"], snapshot.Duration, 1, false, [], false);
        }
    }

    private static ClipRequest Request(int seconds) => new(TimeSpan.FromSeconds(seconds), new ExportOptions(), null, false);

    [Fact]
    public void With_two_presses_being_written_the_time_is_that_of_the_first()
    {
        var backend = new HeldBackend();
        using var ended = new SemaphoreSlim(0);
        var pipeline = new ClipPipeline(backend, work => work(), (_, _) => { }, _ => { })
        {
            Finished = _ => ended.Release(),
        };

        pipeline.Press(Request(1));
        var first = pipeline.OldestPressUtc;

        // Far enough apart for the clock to tell them apart.
        Thread.Sleep(50);
        pipeline.Press(Request(3));

        Assert.NotNull(first);
        Assert.Equal(first, pipeline.OldestPressUtc);

        backend.Release.Set();
        Assert.True(ended.Wait(TimeSpan.FromSeconds(20)));
        Assert.True(ended.Wait(TimeSpan.FromSeconds(20)));
        Assert.Null(pipeline.OldestPressUtc);
    }
}
