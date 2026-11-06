using System.Collections.Concurrent;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;

namespace Hindsound.Tests.App;

public class ClipFilesTests
{
    [Fact]
    public void A_clip_with_all_its_files_can_be_copied()
    {
        Assert.True(ClipFiles.AllThere(["a_system.wav", "a_mic.wav"], _ => true));
    }

    [Theory]
    [InlineData("a_system.wav")]
    [InlineData("a_mic.wav")]
    public void One_file_missing_is_enough_to_say_no(string missing)
    {
        Assert.False(ClipFiles.AllThere(["a_system.wav", "a_mic.wav"], path => path != missing));
    }

    [Fact]
    public void A_clip_without_files_cannot_be_copied()
    {
        Assert.False(ClipFiles.AllThere([], _ => true));
    }
}

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

    // 5:00 takes 172.8 MB, so six of them hold 1.0368 GB and a seventh would go past the limit.
    private const int FiveMinutes = 300;
    private const int FitBelowTheLimit = 6;

    private static int Snapshots(FakeBackend backend) => backend.Calls.Count(c => c.StartsWith("snapshot"));

    private static void HoldTheEncoder(Rig rig, ManualResetEventSlim hold)
    {
        rig.Backend.HoldSave = hold;
        rig.Pipeline.Press(Request(FiveMinutes));
        Assert.True(SpinWait.SpinUntil(() => rig.Backend.Calls.Contains("save 1"), Wait));
    }

    [Theory]
    [InlineData(300, 172_800_000L)]
    [InlineData(1800, 1_036_800_000L)]
    [InlineData(15, 8_640_000L)]
    public void The_memory_is_estimated_from_the_duration_asked(int seconds, long bytes)
    {
        Assert.Equal(bytes, ClipPipeline.EstimatedBytes(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Presses_past_the_limit_copy_nothing_and_say_busy_at_once()
    {
        var rig = new Rig();
        using var hold = new ManualResetEventSlim(false);
        HoldTheEncoder(rig, hold);
        for (var i = 1; i < FitBelowTheLimit; i++)
        {
            rig.Pipeline.Press(Request(FiveMinutes));
        }

        Assert.True(SpinWait.SpinUntil(() => Snapshots(rig.Backend) == FitBelowTheLimit, Wait));

        for (var i = 0; i < 44; i++)
        {
            rig.Pipeline.Press(Request(FiveMinutes));
        }

        for (var i = 0; i < 44; i++)
        {
            Assert.Equal(ClipFailure.Busy, rig.NextOutcome().Failure);
        }

        Assert.Equal(FitBelowTheLimit, Snapshots(rig.Backend));
        hold.Set();
        for (var i = 0; i < FitBelowTheLimit; i++)
        {
            Assert.Equal(ClipFailure.None, rig.NextOutcome().Failure);
        }
    }

    [Fact]
    public void A_short_press_still_fits_where_a_long_one_does_not()
    {
        var rig = new Rig();
        using var hold = new ManualResetEventSlim(false);
        HoldTheEncoder(rig, hold);
        for (var i = 1; i < FitBelowTheLimit; i++)
        {
            rig.Pipeline.Press(Request(FiveMinutes));
        }

        rig.Pipeline.Press(Request(30));
        Assert.True(SpinWait.SpinUntil(() => Snapshots(rig.Backend) == FitBelowTheLimit + 1, Wait), "a press that fits was refused");
        hold.Set();
    }

    [Fact]
    public void A_single_clip_goes_through_whatever_its_size()
    {
        var rig = new Rig();

        rig.Pipeline.Press(Request(3600)); // 2 GB on its own

        Assert.Equal(ClipFailure.None, rig.NextOutcome().Failure);
    }

    [Fact]
    public void A_clip_too_large_for_the_limit_is_refused_only_while_another_one_waits()
    {
        var rig = new Rig();
        using var hold = new ManualResetEventSlim(false);
        HoldTheEncoder(rig, hold);

        rig.Pipeline.Press(Request(3600));
        Assert.Equal(ClipFailure.Busy, rig.NextOutcome().Failure);

        hold.Set();
        Assert.Equal(ClipFailure.None, rig.NextOutcome().Failure);

        rig.Pipeline.Press(Request(3600));
        Assert.Equal(ClipFailure.None, rig.NextOutcome().Failure);
    }

    // A clip of 40:00 is over the limit on its own: each one goes through only if the one before gave its memory back.
    private const int OverTheLimit = 2400;

    [Fact]
    public void The_memory_comes_back_when_a_clip_is_written()
    {
        var rig = new Rig();
        for (var i = 0; i < 3; i++)
        {
            rig.Pipeline.Press(Request(OverTheLimit));
            Assert.Equal(ClipFailure.None, rig.NextOutcome().Failure);
        }
    }

    [Fact]
    public void The_memory_comes_back_when_the_writing_fails()
    {
        var rig = new Rig();
        rig.Backend.SaveThrows = new IOException("disk full");
        for (var i = 0; i < 3; i++)
        {
            rig.Pipeline.Press(Request(OverTheLimit));
            Assert.Equal(ClipFailure.Failed, rig.NextOutcome().Failure);
        }
    }

    [Fact]
    public void The_memory_comes_back_when_the_snapshot_fails()
    {
        var rig = new Rig();
        rig.Backend.NextSnapshot = () => throw new OutOfMemoryException();
        for (var i = 0; i < 3; i++)
        {
            rig.Pipeline.Press(Request(OverTheLimit));
            Assert.Equal(ClipFailure.Failed, rig.NextOutcome().Failure);
        }
    }

    [Fact]
    public void The_memory_comes_back_when_the_snapshot_is_empty()
    {
        var rig = new Rig();
        rig.Backend.NextSnapshot = () => new AudioSnapshot([], []);
        for (var i = 0; i < 3; i++)
        {
            rig.Pipeline.Press(Request(OverTheLimit));
            Assert.Equal(ClipFailure.NothingCaptured, rig.NextOutcome().Failure);
        }
    }

    [Fact]
    public void Leaving_gives_the_two_queues_one_deadline_not_one_each()
    {
        var rig = new Rig();
        using var hold = new ManualResetEventSlim(false);
        rig.Backend.HoldSave = hold;
        rig.Backend.NextSnapshot = () =>
        {
            Thread.Sleep(1500);
            return new AudioSnapshot(new float[2], []);
        };
        rig.Pipeline.Press(Request());

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var idle = rig.Pipeline.WaitIdle(TimeSpan.FromSeconds(2));
        watch.Stop();
        hold.Set();

        Assert.False(idle);
        Assert.True(watch.ElapsedMilliseconds < 3000, $"waited {watch.ElapsedMilliseconds} ms for a limit of 2000 ms");
        rig.NextOutcome();
    }

    [Fact]
    public void Busy_holds_from_the_press_until_the_last_clip_is_heard_of()
    {
        var rig = new Rig();
        using var hold = new ManualResetEventSlim(false);
        rig.Backend.HoldSave = hold;
        var busyWhenHeard = new ConcurrentQueue<bool>();
        rig.Pipeline.Finished = outcome =>
        {
            busyWhenHeard.Enqueue(rig.Pipeline.Busy);
            rig.Outcomes.Add(outcome);
        };

        Assert.False(rig.Pipeline.Busy);
        rig.Pipeline.Press(Request());
        rig.Pipeline.Press(Request());
        Assert.True(rig.Pipeline.Busy);
        hold.Set();
        rig.NextOutcome();
        rig.NextOutcome();

        // The first one still has the second behind it. The last one can already leave, the queues being a step behind.
        Assert.Equal([true, false], busyWhenHeard);
        Assert.False(rig.Pipeline.Busy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_press_that_ends_without_a_clip_stops_being_busy(bool snapshotThrows)
    {
        var rig = new Rig();
        rig.Backend.NextSnapshot = () => snapshotThrows ? throw new InvalidOperationException("on purpose") : new AudioSnapshot([], []);

        rig.Pipeline.Press(Request());

        Assert.Equal(snapshotThrows ? ClipFailure.Failed : ClipFailure.NothingCaptured, rig.NextOutcome().Failure);
        Assert.False(rig.Pipeline.Busy);
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
