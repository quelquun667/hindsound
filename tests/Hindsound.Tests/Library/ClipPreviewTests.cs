using System.IO;
using Hindsound.Audio;
using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>The preview against a fake player: every state, the thread that goes away, and no sound at all.</summary>
public sealed class ClipPreviewTests
{
    private const string Canary = "Collection-Canary-7F3";
    private const int FileNotFound = 275;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static ClipGroup Line(params string[] paths) =>
        new(paths.Select(path => new ClipFile(path, 10, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc))).ToList());

    private sealed class FakePlayer : IPreviewPlayer
    {
        private readonly object _gate = new();
        private readonly List<string> _commands = [];
        private readonly Dictionary<string, int> _modes = [];
        private int _threadOfLastCall;

        public int DefaultMode { get; set; } = ClipPreview.ModePlaying;

        public Func<string, int>? OnSend { get; set; }

        public Func<string, int>? OnMode { get; set; }

        public Func<WaitHandle, int, bool>? OnWait { get; set; }

        public int Waits { get; private set; }

        public ApartmentState Apartment { get; private set; } = ApartmentState.Unknown;

        public bool IsBackground { get; private set; }

        public IReadOnlyList<int> Threads { get; } = new List<int>();

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

        public void SetMode(string alias, int mode)
        {
            lock (_gate)
            {
                _modes[alias] = mode;
            }
        }

        public void SetAllModes(int mode)
        {
            lock (_gate)
            {
                _modes.Clear();
                DefaultMode = mode;
            }
        }

        public string AliasOfOpen(int index)
        {
            var open = Commands.Where(c => c.StartsWith("open ", StringComparison.Ordinal)).ElementAt(index);
            return open[(open.LastIndexOf(' ') + 1)..];
        }

        public int Send(string command)
        {
            Note();
            lock (_gate)
            {
                _commands.Add(command);
            }

            return OnSend?.Invoke(command) ?? 0;
        }

        public int ModeOf(string alias, out int mode)
        {
            Note();
            lock (_gate)
            {
                mode = _modes.TryGetValue(alias, out var known) ? known : DefaultMode;
            }

            return OnMode?.Invoke(alias) ?? 0;
        }

        public void Wait(WaitHandle wake, int milliseconds)
        {
            Waits++;
            if (OnWait?.Invoke(wake, milliseconds) != true)
            {
                wake.WaitOne(1);
            }
        }

        private void Note()
        {
            var id = Environment.CurrentManagedThreadId;
            if (id != _threadOfLastCall)
            {
                _threadOfLastCall = id;
                lock (_gate)
                {
                    ((List<int>)Threads).Add(id);
                }

                Apartment = Thread.CurrentThread.GetApartmentState();
                IsBackground = Thread.CurrentThread.IsBackground;
            }
        }
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
            Assert.True(SpinWait.SpinUntil(() => States.Count(s => s == state) >= count, Patience), $"never {state}");
    }

    private sealed class Log
    {
        private readonly List<string> _lines = [];

        public void Write(string line)
        {
            lock (_lines)
            {
                _lines.Add(line);
            }
        }

        public string Text
        {
            get
            {
                lock (_lines)
                {
                    return string.Join("\n", _lines);
                }
            }
        }
    }

    private static void AssertThreadGone(ClipPreview preview) =>
        Assert.True(preview.WaitForEnd((int)Patience.TotalMilliseconds), "the thread is still there");

    [Fact]
    public void A_clip_goes_from_opening_to_playing_to_its_end_and_its_thread_goes()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        var clip = Line(@"C:\clips\a.mp3");
        using var preview = new ClipPreview(player, heard.Add);

        Assert.Equal(PreviewState.Stopped, preview.Current.State);
        preview.Play(clip);
        heard.WaitForCount(PreviewState.Playing);
        player.SetAllModes(ClipPreview.ModeStopped);
        heard.WaitForCount(PreviewState.Ended);
        AssertThreadGone(preview);

        Assert.Equal([PreviewState.Opening, PreviewState.Playing, PreviewState.Ended], heard.States);
        Assert.All(heard.Changes, change => Assert.Same(clip, change.Clip));
        Assert.Equal(new PreviewChange(PreviewState.Ended, clip), preview.Current);
        var alias = player.AliasOfOpen(0);
        Assert.Equal([$"open \"C:\\clips\\a.mp3\" type mpegvideo alias {alias}", $"play {alias}", $"close {alias}"], player.Commands);
    }

    [Fact]
    public void The_end_is_told_once_nothing_is_left_open()
    {
        var player = new FakePlayer { DefaultMode = ClipPreview.ModePlaying };
        var commandsAtEnd = new List<string>();
        var heard = new Listener();
        using var preview = new ClipPreview(player, change =>
        {
            heard.Add(change);
            if (change.State == PreviewState.Ended)
            {
                commandsAtEnd.AddRange(player.Commands);
            }
        });

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        player.SetAllModes(ClipPreview.ModeStopped);
        heard.WaitForCount(PreviewState.Ended);

        Assert.StartsWith("close ", commandsAtEnd[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void It_runs_on_a_thread_of_its_own_in_a_single_threaded_apartment_that_keeps_the_process_free_to_leave()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);

        Assert.Equal(ApartmentState.STA, player.Apartment);
        Assert.True(player.IsBackground);
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, player.Threads);
    }

    [Fact]
    public void A_wait_asks_for_the_poll_delay()
    {
        var player = new FakePlayer();
        var delays = new List<int>();
        player.OnWait = (_, ms) =>
        {
            lock (delays)
            {
                delays.Add(ms);
            }

            return false;
        };
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        preview.Stop();
        AssertThreadGone(preview);

        Assert.All(delays, ms => Assert.Equal(ClipPreview.PollMs, ms));
    }

    [Fact]
    public void Stop_closes_the_file_says_stopped_and_the_thread_goes()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        preview.Stop();
        heard.WaitForCount(PreviewState.Stopped);
        AssertThreadGone(preview);

        Assert.Equal([PreviewState.Opening, PreviewState.Playing, PreviewState.Stopped], heard.States);
        Assert.Null(heard.Changes[^1].Clip);
        Assert.Equal("close " + player.AliasOfOpen(0), player.Commands[^1]);
    }

    [Fact]
    public void Stop_wakes_a_wait_that_has_a_long_way_to_go()
    {
        var player = new FakePlayer();
        player.OnWait = (wake, _) => wake.WaitOne(Patience);
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        Assert.True(SpinWait.SpinUntil(() => player.Waits >= 1, Patience));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        preview.Stop();
        AssertThreadGone(preview);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
    }

    [Fact]
    public void Stop_with_nothing_playing_does_nothing_and_starts_no_thread()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Stop();
        AssertThreadGone(preview);

        Assert.Empty(heard.States);
        Assert.Empty(player.Commands);
    }

    [Fact]
    public void A_stop_does_not_cancel_the_next_play()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        preview.Stop();
        preview.Play(Line(@"C:\clips\b.mp3"));
        heard.WaitForCount(PreviewState.Playing, 2);

        Assert.Contains(player.Commands, c => c.Contains("b.mp3", StringComparison.Ordinal));
    }

    [Fact]
    public void Closing_the_window_stops_the_sound_and_the_window_hears_nothing_more()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        preview.Dispose();
        AssertThreadGone(preview);

        Assert.Equal([PreviewState.Opening, PreviewState.Playing], heard.States);
        Assert.Equal("close " + player.AliasOfOpen(0), player.Commands[^1]);
        Assert.Equal(PreviewState.Stopped, preview.Current.State);
    }

    [Fact]
    public void Closing_the_window_never_waits_for_an_open_that_hangs()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var player = new FakePlayer();
        player.OnSend = command =>
        {
            if (command.StartsWith("open", StringComparison.Ordinal))
            {
                entered.Set();
                release.Wait(Patience);
            }

            return 0;
        };
        var preview = new ClipPreview(player, _ => { });

        preview.Play(Line(@"C:\clips\a.mp3"));
        Assert.True(entered.Wait(Patience));
        preview.Dispose();

        Assert.False(preview.WaitForEnd(0));
        release.Set();
        AssertThreadGone(preview);
        Assert.DoesNotContain(player.Commands, c => c.StartsWith("play", StringComparison.Ordinal));
        Assert.StartsWith("close ", player.Commands[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void After_the_window_closed_a_play_does_nothing()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        var preview = new ClipPreview(player, heard.Add);

        preview.Dispose();
        preview.Play(Line(@"C:\clips\a.mp3"));
        AssertThreadGone(preview);

        Assert.Empty(player.Commands);
        Assert.Empty(heard.States);
    }

    [Fact]
    public void A_second_play_replaces_the_first_on_the_same_thread_with_no_stop_in_between()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        var first = Line(@"C:\clips\a.mp3");
        var second = Line(@"C:\clips\b.mp3");
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(first);
        heard.WaitForCount(PreviewState.Playing);
        preview.Play(second);
        heard.WaitForCount(PreviewState.Playing, 2);

        Assert.Equal([PreviewState.Opening, PreviewState.Playing, PreviewState.Opening, PreviewState.Playing], heard.States);
        Assert.Same(first, heard.Changes[1].Clip);
        Assert.Same(second, heard.Changes[3].Clip);
        Assert.Single(player.Threads);

        var firstAlias = player.AliasOfOpen(0);
        var secondAlias = player.AliasOfOpen(1);
        Assert.NotEqual(firstAlias, secondAlias);
        var commands = player.Commands.ToList();
        Assert.True(commands.IndexOf("close " + firstAlias) < commands.FindIndex(c => c.Contains("b.mp3", StringComparison.Ordinal)), "the first is closed before the second opens");
    }

    [Fact]
    public void A_second_play_during_an_open_never_plays_the_first()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var player = new FakePlayer();
        player.OnSend = command =>
        {
            if (command.Contains("a.mp3", StringComparison.Ordinal))
            {
                entered.Set();
                release.Wait(Patience);
            }

            return 0;
        };
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3", @"C:\clips\a_mic.mp3"));
        Assert.True(entered.Wait(Patience));

        // The caller is back at once, while the first open still hangs.
        preview.Play(Line(@"C:\clips\b.mp3"));
        release.Set();
        heard.WaitForCount(PreviewState.Playing);

        var plays = player.Commands.Where(c => c.StartsWith("play ", StringComparison.Ordinal)).ToList();
        Assert.Equal(["play " + player.AliasOfOpen(1)], plays);
        Assert.DoesNotContain(player.Commands, c => c.Contains("a_mic.mp3", StringComparison.Ordinal));
        Assert.Contains("close " + player.AliasOfOpen(0), player.Commands);
    }

    [Fact]
    public void Two_tracks_open_before_either_starts_and_the_gap_is_noted()
    {
        long now = 1000;
        var player = new FakePlayer();
        player.OnSend = command =>
        {
            if (command.StartsWith("play ", StringComparison.Ordinal))
            {
                Interlocked.Add(ref now, 7);
            }

            return 0;
        };
        var heard = new Listener();
        var log = new Log();
        using var preview = new ClipPreview(player, heard.Add, log.Write, () => Interlocked.Read(ref now));

        preview.Play(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"));
        heard.WaitForCount(PreviewState.Playing);

        var system = player.AliasOfOpen(0);
        var mic = player.AliasOfOpen(1);
        Assert.Equal(
            [
                $"open \"C:\\clips\\a_system.mp3\" type mpegvideo alias {system}",
                $"open \"C:\\clips\\a_mic.mp3\" type mpegvideo alias {mic}",
                $"play {system}",
                $"play {mic}",
            ],
            player.Commands);
        Assert.Equal(14, preview.StartGapMs);
        Assert.Contains("2 tracks started in 14 ms", log.Text);
    }

    [Fact]
    public void The_end_waits_for_both_tracks()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        Assert.True(SpinWait.SpinUntil(() => player.Waits >= 2, Patience));
        player.SetMode(player.AliasOfOpen(0), ClipPreview.ModeStopped);
        var waits = player.Waits;
        Assert.True(SpinWait.SpinUntil(() => player.Waits >= waits + 5, Patience));

        Assert.DoesNotContain(PreviewState.Ended, heard.States);
        player.SetMode(player.AliasOfOpen(1), ClipPreview.ModeStopped);
        heard.WaitForCount(PreviewState.Ended);
        AssertThreadGone(preview);
    }

    [Fact]
    public void One_track_has_no_gap_and_a_new_clip_forgets_the_old_one()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        Assert.NotNull(preview.StartGapMs);

        preview.Play(Line(@"C:\clips\b.mp3"));
        heard.WaitForCount(PreviewState.Playing, 2);
        Assert.Null(preview.StartGapMs);
    }

    [Fact]
    public void A_stopped_track_just_after_the_start_is_not_its_end_until_the_grace_is_over()
    {
        long now = 0;
        var player = new FakePlayer { DefaultMode = ClipPreview.ModeStopped };
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add, null, () => Interlocked.Read(ref now));

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        var waits = player.Waits;
        Assert.True(SpinWait.SpinUntil(() => player.Waits >= waits + 5, Patience));
        Interlocked.Exchange(ref now, ClipPreview.StartGraceMs - 1);
        waits = player.Waits;
        Assert.True(SpinWait.SpinUntil(() => player.Waits >= waits + 5, Patience));
        Assert.DoesNotContain(PreviewState.Ended, heard.States);

        Interlocked.Exchange(ref now, ClipPreview.StartGraceMs);
        heard.WaitForCount(PreviewState.Ended);
    }

    [Fact]
    public void A_track_seen_playing_ends_at_its_stop_whatever_the_clock_says()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add, null, () => 0);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        Assert.True(SpinWait.SpinUntil(() => player.Waits >= 2, Patience));
        player.SetAllModes(ClipPreview.ModeStopped);

        heard.WaitForCount(PreviewState.Ended);
    }

    [Fact]
    public void Any_other_mode_keeps_waiting()
    {
        const int NotReady = 524;
        var player = new FakePlayer { DefaultMode = NotReady };
        var heard = new Listener();
        long now = 0;
        using var preview = new ClipPreview(player, heard.Add, null, () => Interlocked.Read(ref now));

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        Interlocked.Exchange(ref now, 60_000);
        var waits = player.Waits;
        Assert.True(SpinWait.SpinUntil(() => player.Waits >= waits + 5, Patience));

        Assert.DoesNotContain(PreviewState.Ended, heard.States);
    }

    [Fact]
    public void A_file_that_is_not_found_is_an_error_with_no_path_in_the_log_and_no_thread_left()
    {
        var player = new FakePlayer { OnSend = command => command.StartsWith("open", StringComparison.Ordinal) ? FileNotFound : 0 };
        var heard = new Listener();
        var log = new Log();
        var clip = Line($@"C:\{Canary}\{Canary}.mp3");
        using var preview = new ClipPreview(player, heard.Add, log.Write);

        preview.Play(clip);
        heard.WaitForCount(PreviewState.Error);
        AssertThreadGone(preview);

        Assert.Equal([PreviewState.Opening, PreviewState.Error], heard.States);
        Assert.Same(clip, heard.Changes[^1].Clip);
        Assert.Single(player.Commands);
        Assert.Contains("275", log.Text);
        Assert.DoesNotContain("Canary", log.Text);
        Assert.Equal(ClipPreview.CantPlayText, ClipPreview.ErrorTextFor(preview.Current, clip));
    }

    [Fact]
    public void A_line_with_one_track_left_still_plays_it()
    {
        var player = new FakePlayer { OnSend = command => command.Contains("_mic", StringComparison.Ordinal) && command.StartsWith("open", StringComparison.Ordinal) ? FileNotFound : 0 };
        var heard = new Listener();
        var log = new Log();
        using var preview = new ClipPreview(player, heard.Add, log.Write);

        preview.Play(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"));
        heard.WaitForCount(PreviewState.Playing);

        Assert.Null(preview.StartGapMs);
        Assert.Contains("275", log.Text);
        Assert.DoesNotContain("tracks started", log.Text);
    }

    [Fact]
    public void A_start_that_fails_closes_what_was_opened_and_is_an_error()
    {
        var player = new FakePlayer { OnSend = command => command.StartsWith("play", StringComparison.Ordinal) ? 277 : 0 };
        var heard = new Listener();
        var log = new Log();
        using var preview = new ClipPreview(player, heard.Add, log.Write);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Error);
        AssertThreadGone(preview);

        Assert.Equal([PreviewState.Opening, PreviewState.Error], heard.States);
        Assert.Equal("close " + player.AliasOfOpen(0), player.Commands[^1]);
        Assert.Contains("277", log.Text);
    }

    [Fact]
    public void One_start_that_fails_leaves_the_other_track_to_end_normally()
    {
        var failing = "";
        var player = new FakePlayer();
        player.OnSend = command =>
        {
            if (command.StartsWith("open", StringComparison.Ordinal) && failing.Length == 0)
            {
                failing = "play " + command[(command.LastIndexOf(' ') + 1)..];
            }

            return command == failing ? 277 : 0;
        };
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        Assert.Null(preview.StartGapMs);
        player.SetAllModes(ClipPreview.ModeStopped);
        heard.WaitForCount(PreviewState.Ended);
        AssertThreadGone(preview);

        Assert.DoesNotContain(PreviewState.Error, heard.States);
    }

    [Fact]
    public void A_track_that_stops_answering_is_an_error_if_it_was_the_only_one()
    {
        var player = new FakePlayer { OnMode = _ => 263 };
        var heard = new Listener();
        var log = new Log();
        using var preview = new ClipPreview(player, heard.Add, log.Write);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Error);
        AssertThreadGone(preview);

        Assert.Equal([PreviewState.Opening, PreviewState.Playing, PreviewState.Error], heard.States);
        Assert.Contains("263", log.Text);
    }

    [Fact]
    public void A_track_that_stops_answering_does_not_turn_the_end_of_the_other_into_an_error()
    {
        string? broken = null;
        var player = new FakePlayer();
        player.OnMode = alias =>
        {
            broken ??= alias;
            return alias == broken ? 263 : 0;
        };
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        Assert.True(SpinWait.SpinUntil(() => player.Waits >= 3, Patience));
        player.SetAllModes(ClipPreview.ModeStopped);
        heard.WaitForCount(PreviewState.Ended);

        Assert.DoesNotContain(PreviewState.Error, heard.States);
    }

    [Fact]
    public void An_exception_of_the_player_is_an_error_keeps_the_process_and_stays_out_of_the_log()
    {
        var player = new FakePlayer
        {
            OnSend = command => command.StartsWith("play", StringComparison.Ordinal)
                ? throw new IOException($@"C:\{Canary}\{Canary}.mp3 is held") { HResult = unchecked((int)0x80070020) }
                : 0,
        };
        var heard = new Listener();
        var log = new Log();
        using var preview = new ClipPreview(player, heard.Add, log.Write);

        preview.Play(Line($@"C:\{Canary}\{Canary}.mp3"));
        heard.WaitForCount(PreviewState.Error);
        AssertThreadGone(preview);

        Assert.Equal("close " + player.AliasOfOpen(0), player.Commands[^1]);
        Assert.Contains("IOException", log.Text);
        Assert.Contains("0x80070020", log.Text);
        Assert.DoesNotContain("Canary", log.Text);
    }

    [Fact]
    public void An_exception_of_the_player_during_a_stop_says_stopped_not_error()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var player = new FakePlayer();
        player.OnSend = command =>
        {
            if (command.StartsWith("open", StringComparison.Ordinal))
            {
                entered.Set();
                release.Wait(Patience);
                throw new IOException("held");
            }

            return 0;
        };
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3"));
        Assert.True(entered.Wait(Patience));
        preview.Stop();
        release.Set();
        AssertThreadGone(preview);

        Assert.Equal([PreviewState.Opening, PreviewState.Stopped], heard.States);
    }

    [Fact]
    public void A_close_that_fails_or_throws_closes_the_rest_and_stays_out_of_the_log()
    {
        var closes = 0;
        var player = new FakePlayer();
        player.OnSend = command =>
        {
            if (command.StartsWith("close", StringComparison.Ordinal))
            {
                return ++closes == 1 ? throw new IOException($@"C:\{Canary}") : 263;
            }

            return 0;
        };
        var heard = new Listener();
        var log = new Log();
        using var preview = new ClipPreview(player, heard.Add, log.Write);

        preview.Play(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        preview.Stop();
        heard.WaitForCount(PreviewState.Stopped);

        Assert.Equal(2, closes);
        Assert.Contains("IOException", log.Text);
        Assert.Contains("263", log.Text);
        Assert.DoesNotContain("Canary", log.Text);
    }

    [Fact]
    public void A_listener_that_throws_never_ends_the_process_and_stays_out_of_the_log()
    {
        var player = new FakePlayer();
        var log = new Log();
        var heard = new Listener();
        using var preview = new ClipPreview(
            player,
            change =>
            {
                heard.Add(change);
                throw new IOException($@"C:\{Canary}");
            },
            log.Write);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);
        player.SetAllModes(ClipPreview.ModeStopped);
        heard.WaitForCount(PreviewState.Ended);
        AssertThreadGone(preview);

        Assert.Contains("IOException", log.Text);
        Assert.DoesNotContain("Canary", log.Text);
    }

    [Fact]
    public void A_line_with_no_file_is_an_error()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(new ClipGroup([]));
        heard.WaitForCount(PreviewState.Error);
        AssertThreadGone(preview);

        Assert.Empty(player.Commands);
    }

    [Fact]
    public void After_an_end_or_an_error_a_new_play_starts_a_new_thread()
    {
        var failing = true;
        var player = new FakePlayer { OnSend = command => failing && command.StartsWith("open", StringComparison.Ordinal) ? FileNotFound : 0 };
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Error);
        AssertThreadGone(preview);

        failing = false;
        preview.Play(Line(@"C:\clips\a.mp3"));
        heard.WaitForCount(PreviewState.Playing);

        Assert.Equal(2, player.Threads.Count);
    }

    [Fact]
    public void Two_previews_never_share_an_alias()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var one = new ClipPreview(player, heard.Add);
        using var two = new ClipPreview(player, heard.Add);

        one.Play(Line(@"C:\clips\a.mp3"));
        two.Play(Line(@"C:\clips\b.mp3"));
        heard.WaitForCount(PreviewState.Playing, 2);

        Assert.NotEqual(player.AliasOfOpen(0), player.AliasOfOpen(1));
    }

    [Fact]
    public void A_long_path_prefix_never_reaches_MCI()
    {
        var player = new FakePlayer();
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"\\?\C:\clips\a b.mp3"));
        heard.WaitForCount(PreviewState.Playing);

        Assert.StartsWith("open \"C:\\clips\\a b.mp3\" type mpegvideo alias ", player.Commands[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Following_a_track_allocates_nothing_from_one_look_to_the_next()
    {
        var player = new FakePlayer();
        long least = long.MaxValue;
        long last = -1;
        var looks = 0;
        player.OnWait = (wake, _) =>
        {
            var now = GC.GetAllocatedBytesForCurrentThread();
            if (last >= 0)
            {
                Volatile.Write(ref least, Math.Min(Volatile.Read(ref least), now - last));
                Interlocked.Increment(ref looks);
            }

            last = GC.GetAllocatedBytesForCurrentThread();
            return false;
        };
        var heard = new Listener();
        using var preview = new ClipPreview(player, heard.Add);

        preview.Play(Line(@"C:\clips\a_system.mp3", @"C:\clips\a_mic.mp3"));
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref looks) >= 200, Patience));
        preview.Stop();
        AssertThreadGone(preview);

        Assert.Equal(0, Volatile.Read(ref least));
    }

    [Theory]
    [InlineData(true, null, false, true)]
    [InlineData(true, null, true, false)]
    [InlineData(true, "Discord.exe", false, false)]
    [InlineData(false, null, false, false)]
    public void The_sentence_about_the_next_clip_follows_what_is_captured(bool capturing, string? app, bool excluded, bool expected)
    {
        var status = new AudioEngineStatus(CaptureActivity.Capturing, capturing, false, false, "Speakers", null, excluded, TimeSpan.Zero, 0, app);

        Assert.Equal(expected, ClipPreview.CanEndUpInClip(status));
    }

    [Fact]
    public void Only_the_line_that_failed_says_it()
    {
        var failed = Line(@"C:\clips\a.mp3");
        var error = new PreviewChange(PreviewState.Error, failed);

        Assert.Equal("Can't play this file", ClipPreview.ErrorTextFor(error, Line(@"C:\CLIPS\A.mp3")));
        Assert.Null(ClipPreview.ErrorTextFor(error, Line(@"C:\clips\b.mp3")));
        Assert.Null(ClipPreview.ErrorTextFor(error, null));
        Assert.Null(ClipPreview.ErrorTextFor(error, new ClipGroup([])));
        Assert.Null(ClipPreview.ErrorTextFor(new PreviewChange(PreviewState.Ended, failed), failed));
        Assert.Null(ClipPreview.ErrorTextFor(new PreviewChange(PreviewState.Error, null), failed));
    }

    [Theory]
    [InlineData(PreviewState.Opening, true)]
    [InlineData(PreviewState.Playing, true)]
    [InlineData(PreviewState.Stopped, false)]
    [InlineData(PreviewState.Ended, false)]
    [InlineData(PreviewState.Error, false)]
    public void The_button_says_stop_only_while_this_line_sounds(PreviewState state, bool expected)
    {
        var line = Line(@"C:\clips\a.mp3");

        Assert.Equal(expected, ClipPreview.ShowsStopFor(new PreviewChange(state, line), line));
        Assert.False(ClipPreview.ShowsStopFor(new PreviewChange(state, line), Line(@"C:\clips\b.mp3")));
    }
}
