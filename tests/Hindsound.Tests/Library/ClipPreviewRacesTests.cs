using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>
/// The gaps of a few instructions in <see cref="ClipPreview"/>: a Play or a Stop landing between two of its steps, or a
/// listener still running when the window closes. Each test lands there on purpose, not by chance.
/// </summary>
public sealed class ClipPreviewRacesTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static ClipGroup Line(params string[] paths) =>
        new(paths.Select(path => new ClipFile(path, 10, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc))).ToList());

    /// <summary>A clock that is far past the grace of a start at every look, so that a stopped track is an ended one at once.</summary>
    private static Func<long> FastClock()
    {
        long now = 0;
        return () => Interlocked.Add(ref now, 2000);
    }

    /// <summary>A player that plays forever, and counts how many files are open at once and how many threads reach it.</summary>
    private sealed class Player : IPreviewPlayer
    {
        private readonly object _gate = new();
        private readonly List<string> _commands = [];
        private readonly HashSet<int> _threads = [];
        private int _open;

        public PreviewTrackState Mode { get; set; } = PreviewTrackState.Playing;

        public int MostOpen { get; private set; }

        public int ThreadCount
        {
            get
            {
                lock (_gate)
                {
                    return _threads.Count;
                }
            }
        }

        public IReadOnlyList<string> Commands
        {
            get
            {
                lock (_gate)
                {
                    return _commands.ToList();
                }
            }
        }

        public int Open(string path, string track) => Record($"open {path} as {track}");

        public int Play(string track) => Record("play " + track);

        public int Stop(string track) => Record("stop " + track);

        public int Close(string track) => Record("close " + track);

        private int Record(string command)
        {
            lock (_gate)
            {
                _threads.Add(Environment.CurrentManagedThreadId);
                _commands.Add(command);
                if (command.StartsWith("open ", StringComparison.Ordinal))
                {
                    MostOpen = Math.Max(MostOpen, ++_open);
                }
                else if (command.StartsWith("close ", StringComparison.Ordinal))
                {
                    _open--;
                }
            }

            return 0;
        }

        public int StateOf(string alias, out PreviewTrackState mode)
        {
            mode = Mode;
            return 0;
        }

        public void Wait(WaitHandle wake, int milliseconds) => wake.WaitOne(1);
    }

    private sealed class Listener
    {
        private readonly List<PreviewChange> _changes = [];

        public void Add(PreviewChange change)
        {
            lock (_changes)
            {
                _changes.Add(change);
            }
        }

        public IReadOnlyList<PreviewChange> Changes
        {
            get
            {
                lock (_changes)
                {
                    return _changes.ToList();
                }
            }
        }

        public IReadOnlyList<PreviewState> States => Changes.Select(c => c.State).ToList();

        public void WaitForCount(PreviewState state, int count = 1) =>
            Assert.True(SpinWait.SpinUntil(() => States.Count(s => s == state) >= count, Patience), $"never {state} x{count}");
    }

    [Fact]
    public void A_stop_that_wipes_the_play_which_replaced_the_sound_still_tells_that_it_is_over()
    {
        var player = new Player();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);
        var once = 0;

        // The session of A is over and B has replaced it: the end of A is not told. Now the Stop lands before B is taken.
        preview.AfterEndDecision = () =>
        {
            if (Interlocked.Exchange(ref once, 1) == 0)
            {
                preview.Stop();
            }
        };

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        preview.Play(Line(@"C:\clips\b.mp3"));
        Assert.True(preview.WaitForEnd((int)Patience.TotalMilliseconds), "the thread is still there");

        Assert.Equal(PreviewState.Stopped, preview.Current.State);
        Assert.False(ClipPreview.ShowsStopFor(preview.Current, Line(@"C:\clips\a.mp3")));
        Assert.Equal(PreviewState.Stopped, heard.States[^1]);
        Assert.Null(heard.Changes[^1].Clip);
        Assert.DoesNotContain(player.Commands, c => c.Contains("b.mp3", StringComparison.Ordinal));
    }

    [Fact]
    public void A_play_that_replaced_the_sound_and_is_not_wiped_still_tells_nothing_of_the_old_end()
    {
        var player = new Player();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        preview.Play(Line(@"C:\clips\b.mp3"));
        heard.WaitForCount(PreviewState.Playing, 2);

        Assert.Equal([PreviewState.Opening, PreviewState.Playing, PreviewState.Opening, PreviewState.Playing], heard.States);
    }

    [Fact]
    public void A_play_that_comes_as_the_thread_leaves_starts_a_new_one_and_is_not_lost()
    {
        var player = new Player();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add, null, FastClock());
        player.Mode = PreviewTrackState.Stopped;
        var once = 0;

        // The thread of A has decided there is nothing more to take and is on its way out: this Play must not be left to it.
        preview.BeforeThreadLeaves = () =>
        {
            if (Interlocked.Exchange(ref once, 1) == 0)
            {
                player.Mode = PreviewTrackState.Playing;
                preview.Play(Line(@"C:\clips\b.mp3"));
            }
        };

        preview.Play(Line(@"C:\clips\a.mp3"));
        Assert.True(SpinWait.SpinUntil(() => heard.States.Count(s => s == PreviewState.Playing) >= 2, TimeSpan.FromSeconds(10)), "the Play that came as the thread left never played");

        Assert.Contains(player.Commands, c => c.Contains("b.mp3", StringComparison.Ordinal));
    }

    [Fact]
    public void The_thread_that_leaves_does_not_take_the_place_of_the_one_that_replaced_it()
    {
        var player = new Player();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add, null, FastClock());
        player.Mode = PreviewTrackState.Stopped;
        Thread? first = null;
        var once = 0;

        preview.BeforeThreadLeaves = () =>
        {
            if (Interlocked.Exchange(ref once, 1) == 0)
            {
                first = Thread.CurrentThread;
                player.Mode = PreviewTrackState.Playing;
                preview.Play(Line(@"C:\clips\b.mp3"));
            }
        };

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Ended);
        heard.WaitForCount(PreviewState.Playing, 2);
        Assert.True(first!.Join(Patience), "the first thread is still there");

        // The first thread is gone for good. C must replace B on the thread of B, not start a third.
        preview.Play(Line(@"C:\clips\c.mp3"));
        heard.WaitForCount(PreviewState.Playing, 3);
        preview.Stop();
        Assert.True(preview.WaitForEnd((int)Patience.TotalMilliseconds), "the thread is still there");

        Assert.Equal(2, player.ThreadCount);
        Assert.Equal(1, player.MostOpen);
    }

    [Fact]
    public void A_run_of_plays_each_just_after_an_end_never_loses_one_and_never_overlaps_two_sessions()
    {
        var player = new Player();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add, null, FastClock());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plays = 0;

        while (watch.Elapsed < TimeSpan.FromSeconds(2) && plays < 5000)
        {
            plays++;
            player.Mode = PreviewTrackState.Playing;
            preview.Play(Line(@"C:\clips\a.mp3"));
            Assert.True(SpinWait.SpinUntil(() => heard.States.Count(s => s == PreviewState.Playing) >= plays, Patience), $"play {plays} never played");

            // The end, then the next Play as soon as it is told: it lands as the thread decides whether to go on.
            player.Mode = PreviewTrackState.Stopped;
            Assert.True(SpinWait.SpinUntil(() => heard.States.Count(s => s == PreviewState.Ended) >= plays, Patience), $"play {plays} never ended");
        }

        Assert.True(preview.WaitForEnd((int)Patience.TotalMilliseconds));
        Assert.Equal(1, player.MostOpen);
        Assert.Equal(plays, heard.States.Count(s => s == PreviewState.Playing));
    }

    [Fact]
    public void A_listener_still_running_when_the_window_closes_is_not_waited_for_and_ends_after_dispose_returned()
    {
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var finished = false;
        var player = new Player();
        var heard = new Listener();
        var preview = new ClipPreview(player, change =>
        {
            heard.Add(change);
            if (change.State == PreviewState.Playing)
            {
                inside.Set();
                release.Wait(Patience);
                Volatile.Write(ref finished, true);
            }
        });

        preview.Play(Line(@"C:\clips\a.mp3"));
        Assert.True(inside.Wait(Patience));

        // Dispose neither waits for the listener nor holds the lock of the preview while it runs.
        var dispose = Task.Run(preview.Dispose);
        Assert.True(SpinWait.SpinUntil(() => dispose.IsCompleted, TimeSpan.FromSeconds(5)), "Dispose waited for the listener");
        var current = Task.Run(() => preview.Current);
        Assert.True(SpinWait.SpinUntil(() => current.IsCompleted, TimeSpan.FromSeconds(5)), "the lock is held while the listener runs");
        Assert.False(Volatile.Read(ref finished), "the listener was already over, the test says nothing");

        release.Set();
        Assert.True(preview.WaitForEnd((int)Patience.TotalMilliseconds));

        // The contract: the listener of a state published before Dispose finishes after it. Nothing is told after that.
        Assert.True(Volatile.Read(ref finished));
        Assert.Equal([PreviewState.Opening, PreviewState.Playing], heard.States);
        Assert.StartsWith("close ", player.Commands[^1], StringComparison.Ordinal);
    }
}
