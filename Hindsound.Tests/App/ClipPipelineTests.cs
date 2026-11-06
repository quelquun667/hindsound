using System.Collections.Concurrent;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.Tests.App;

public class ClipPipelineTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
    private static readonly ExportOptions Options = new();

    private sealed class FakeBackend : IClipBackend
    {
        public readonly ConcurrentQueue<string> Calls = new();
        public ManualResetEventSlim? HoldSave;
        public Func<AudioSnapshot>? NextSnapshot;
        public Exception? SaveThrows;
        public int Number;

        public AudioSnapshot Snapshot(TimeSpan duration)
        {
            var number = Interlocked.Increment(ref Number);
            Calls.Enqueue("snapshot " + number);
            return NextSnapshot?.Invoke() ?? new AudioSnapshot(new float[2 * number], []);
        }

        public ClipResult Save(AudioSnapshot snapshot, ExportOptions options, string? appName)
        {
            Calls.Enqueue("save " + snapshot.System.Length / 2);
            HoldSave?.Wait(Wait);
            if (SaveThrows is { } exception)
            {
                throw exception;
            }

            return new ClipResult(["clip"], snapshot.Duration, 1, false, [], false);
        }
    }

    private sealed class Rig
    {
        public readonly FakeBackend Backend = new();
        public readonly BlockingCollection<ClipOutcome> Outcomes = [];
        public readonly ConcurrentQueue<Exception> Errors = new();
        public readonly ConcurrentQueue<Action> Timers = new();
        public readonly ConcurrentQueue<ClipRequest> SlowNotices = new();
        public readonly ClipPipeline Pipeline;

        public Rig()
        {
            // The real loop's Post and After, as far as order goes: the post runs the action at once, the timer waits for the test.
            Pipeline = new ClipPipeline(Backend, action => action(), (_, action) => Timers.Enqueue(action), Errors.Enqueue)
            {
                Finished = Outcomes.Add,
                SavingSlowly = SlowNotices.Enqueue,
            };
        }

        public ClipOutcome NextOutcome()
        {
            Assert.True(Outcomes.TryTake(out var outcome, Wait), "no outcome");
            return outcome!;
        }
    }

    private static ClipRequest Request(int seconds = 30) => new(TimeSpan.FromSeconds(seconds), Options, "App", true);

    [Fact]
    public void A_press_writes_a_clip_off_the_callers_thread()
    {
        var rig = new Rig();
        rig.Pipeline.Press(Request());

        var outcome = rig.NextOutcome();

        Assert.Equal(ClipFailure.None, outcome.Failure);
        Assert.NotNull(outcome.Result);
    }

    [Fact]
    public void A_press_during_an_encoding_takes_its_snapshot_at_once()
    {
        var rig = new Rig();
        using var hold = new ManualResetEventSlim(false);
        rig.Backend.HoldSave = hold;

        rig.Pipeline.Press(Request());
        Assert.True(SpinWait.SpinUntil(() => rig.Backend.Calls.Contains("save 1"), Wait));

        rig.Pipeline.Press(Request());
        Assert.True(SpinWait.SpinUntil(() => rig.Backend.Calls.Contains("snapshot 2"), Wait), "the second moment waited for the first encoding");
        Assert.DoesNotContain("save 2", rig.Backend.Calls);

        hold.Set();
        rig.NextOutcome();
        rig.NextOutcome();
        Assert.Contains("save 2", rig.Backend.Calls);
    }

    [Fact]
    public void Only_one_encoding_runs_at_a_time_and_in_order_of_the_presses()
    {
        var rig = new Rig();
        var running = 0;
        var most = 0;
        var pipeline = new ClipPipeline(new CountingBackend(() =>
        {
            var now = Interlocked.Increment(ref running);
            most = Math.Max(most, now);
            Thread.Sleep(10);
            Interlocked.Decrement(ref running);
        }, rig.Backend), action => action(), (_, _) => { }, _ => { })
        {
            Finished = rig.Outcomes.Add,
        };

        for (var i = 0; i < 6; i++)
        {
            pipeline.Press(Request());
        }

        for (var i = 0; i < 6; i++)
        {
            rig.NextOutcome();
        }

        Assert.Equal(1, most);
        Assert.Equal(new[] { "save 1", "save 2", "save 3", "save 4", "save 5", "save 6" }, rig.Backend.Calls.Where(c => c.StartsWith("save")));
    }

    private sealed class CountingBackend(Action during, FakeBackend inner) : IClipBackend
    {
        public AudioSnapshot Snapshot(TimeSpan duration) => inner.Snapshot(duration);

        public ClipResult Save(AudioSnapshot snapshot, ExportOptions options, string? appName)
        {
            during();
            return inner.Save(snapshot, options, appName);
        }
    }

    [Fact]
    public void An_empty_snapshot_never_reaches_the_encoder()
    {
        var rig = new Rig();
        rig.Backend.NextSnapshot = () => new AudioSnapshot([], []);

        rig.Pipeline.Press(Request());

        Assert.Equal(ClipFailure.NothingCaptured, rig.NextOutcome().Failure);
        Assert.DoesNotContain(rig.Backend.Calls, c => c.StartsWith("save"));
    }

    [Fact]
    public void A_snapshot_with_only_the_microphone_is_a_clip()
    {
        var rig = new Rig();
        rig.Backend.NextSnapshot = () => new AudioSnapshot([], new float[10]);

        rig.Pipeline.Press(Request());

        Assert.Equal(ClipFailure.None, rig.NextOutcome().Failure);
    }

    [Fact]
    public void A_failing_write_is_reported_and_the_next_clip_still_goes_through()
    {
        var rig = new Rig();
        rig.Backend.SaveThrows = new IOException("disk full");
        rig.Pipeline.Press(Request());

        var failed = rig.NextOutcome();
        Assert.Equal(ClipFailure.Failed, failed.Failure);
        Assert.Null(failed.Result);
        Assert.Single(rig.Errors);

        rig.Backend.SaveThrows = null;
        rig.Pipeline.Press(Request());
        Assert.Equal(ClipFailure.None, rig.NextOutcome().Failure);
    }

    [Fact]
    public void A_failing_snapshot_is_reported_and_never_escapes()
    {
        var rig = new Rig();
        rig.Backend.NextSnapshot = () => throw new OutOfMemoryException();

        rig.Pipeline.Press(Request());

        Assert.Equal(ClipFailure.Failed, rig.NextOutcome().Failure);
        Assert.IsType<OutOfMemoryException>(Assert.Single(rig.Errors));
    }

    [Fact]
    public void The_saving_notice_shows_only_if_the_write_outlasts_the_delay()
    {
        var rig = new Rig();
        using var hold = new ManualResetEventSlim(false);
        rig.Backend.HoldSave = hold;
        rig.Pipeline.Press(Request());
        Assert.True(SpinWait.SpinUntil(() => !rig.Timers.IsEmpty, Wait));

        // The delay ends while the write is still running.
        Assert.True(rig.Timers.TryDequeue(out var whileWriting));
        whileWriting();
        Assert.Single(rig.SlowNotices);

        hold.Set();
        rig.NextOutcome();

        // The next write ends before its delay does.
        rig.Backend.HoldSave = null;
        rig.Pipeline.Press(Request());
        rig.NextOutcome();
        Assert.True(rig.Timers.TryDequeue(out var afterWriting));
        afterWriting();
        Assert.Single(rig.SlowNotices);
    }

    [Fact]
    public void Leaving_waits_for_the_clips_still_queued()
    {
        var rig = new Rig();
        using var hold = new ManualResetEventSlim(false);
        rig.Backend.HoldSave = hold;
        rig.Pipeline.Press(Request());
        Assert.True(SpinWait.SpinUntil(() => rig.Backend.Calls.Contains("save 1"), Wait));

        Assert.False(rig.Pipeline.WaitIdle(TimeSpan.FromMilliseconds(30)));
        hold.Set();
        Assert.True(rig.Pipeline.WaitIdle(Wait));
    }

    [Fact]
    public void A_queue_survives_a_piece_that_throws()
    {
        var errors = new ConcurrentQueue<Exception>();
        var queue = new SerialWorkQueue(errors.Enqueue);
        var ran = false;

        queue.Enqueue(() => throw new InvalidOperationException());
        queue.Enqueue(() => ran = true);

        Assert.True(queue.WaitIdle(Wait));
        Assert.True(ran);
        Assert.Single(errors);
    }
}
