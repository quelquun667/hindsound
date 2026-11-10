using System.Collections.Concurrent;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>The preview of one window, with a fake player: made at the first Play, told on the loop, stopped when the window goes.</summary>
public sealed class WindowLibraryPreviewTests : IDisposable
{
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly ConcurrentQueue<string> _journal = new();
    private readonly List<QuietPlayer> _players = [];
    private Func<ClipGroup, bool>? _mayPlay;
    private WindowLibrary? _panel;

    public void Dispose()
    {
        _panel?.Close();
        _rig.Dispose();
    }

    private static ClipGroup Clip(string name) => new([new ClipFile(@"C:\Clips\" + name, 1000, default)]);

    private WindowLibrary Panel(bool withPlayer = true)
    {
        _panel = new WindowLibrary(
            _rig.Screen,
            new LibraryStore(Path.Combine(Path.GetTempPath(), "hindsound-none", "settings.json"), _journal.Enqueue),
            () => new ClipScanRequest(),
            _loop.Enqueue,
            new OffscreenFolderShell(_journal.Enqueue),
            _ => true,
            _ => true,
            (attempt, done) => done(attempt()),
            _journal.Enqueue,
            player: withPlayer ? NewPlayer : null,
            mayPlay: _mayPlay);
        return _panel;
    }

    private QuietPlayer NewPlayer()
    {
        var player = new QuietPlayer();
        _players.Add(player);
        return player;
    }

    /// <summary>Runs what reached the loop until the screen says this, or fails after a while.</summary>
    private void PumpUntil(Func<bool> done)
    {
        var until = Environment.TickCount64 + 10_000;
        while (!done())
        {
            Assert.True(Environment.TickCount64 < until, "the state never reached the screen");
            if (_loop.TryDequeue(out var work))
            {
                work();
            }
            else
            {
                Thread.Sleep(5);
            }
        }
    }

    private void PumpAll()
    {
        while (_loop.TryDequeue(out var work))
        {
            work();
        }
    }

    [Fact]
    public void Nothing_is_made_before_the_first_play()
    {
        var panel = Panel();
        Assert.Null(panel.Preview);
        panel.StopPlaying();
        Assert.Empty(_players);
    }

    [Fact]
    public void A_play_reaches_the_screen_through_the_loop_only()
    {
        var panel = Panel();
        var clip = Clip("dogs.mp3");
        panel.Play(clip);
        Assert.Single(_players);
        PumpUntil(() => _rig.Screen.Preview.State == PreviewState.Playing);
        Assert.Equal(clip, _rig.Screen.Preview.Clip);

        panel.StopPlaying();
        PumpUntil(() => _rig.Screen.Preview.State == PreviewState.Stopped);
        Assert.True(panel.Preview!.WaitForEnd(5000));
    }

    [Fact]
    public void One_preview_serves_every_play_of_the_window()
    {
        var panel = Panel();
        panel.Play(Clip("dogs.mp3"));
        var first = panel.Preview;
        panel.Play(Clip("pets.mp3"));
        Assert.Same(first, panel.Preview);
        Assert.Single(_players);
        PumpUntil(() => _rig.Screen.Preview.Clip?.Main.Path.EndsWith("pets.mp3", StringComparison.Ordinal) == true && _rig.Screen.Preview.State == PreviewState.Playing);
    }

    [Fact]
    public void Closing_the_window_stops_the_sound_ends_the_thread_and_tells_the_screen_nothing_more()
    {
        var panel = Panel();
        panel.Play(Clip("dogs.mp3"));
        PumpUntil(() => _rig.Screen.Preview.State == PreviewState.Playing);
        var preview = panel.Preview!;

        panel.Close();
        Assert.Null(panel.Preview);
        Assert.True(preview.WaitForEnd(5000));
        Assert.Contains(_players[0].Commands, command => command.StartsWith("close ", StringComparison.Ordinal));

        // The Stopped published on the way out arrives on the loop after the window has gone: it reaches nobody.
        PumpAll();
        Assert.Equal(PreviewState.Playing, _rig.Screen.Preview.State);

        panel.Play(Clip("pets.mp3"));
        Assert.Null(panel.Preview);
        Assert.Single(_players);
    }

    [Fact]
    public void A_clip_this_run_may_not_play_is_told_as_one_that_can_not_be_played_and_nothing_is_opened()
    {
        _mayPlay = _ => false;
        var panel = Panel();
        var clip = Clip("dogs.mp3");
        panel.Play(clip);
        Assert.Empty(_players);
        Assert.NotEqual(PreviewState.Error, _rig.Screen.Preview.State);
        PumpAll();
        Assert.Equal(new PreviewChange(PreviewState.Error, clip), _rig.Screen.Preview);
        Assert.DoesNotContain(_journal, line => line.Contains("dogs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Without_a_player_Play_does_nothing()
    {
        var panel = Panel(withPlayer: false);
        panel.Play(Clip("dogs.mp3"));
        PumpAll();
        Assert.Null(panel.Preview);
        Assert.Equal(PreviewState.Stopped, _rig.Screen.Preview.State);
    }

    /// <summary>Opens and plays nothing: every track reads as playing until it is closed.</summary>
    private sealed class QuietPlayer : IPreviewPlayer
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyList<string> Commands => [.. _commands];

        public int Open(string path, string track) => Record($"open {path} as {track}");

        public int Play(string track) => Record("play " + track);

        public int Stop(string track) => Record("stop " + track);

        public int Close(string track) => Record("close " + track);

        private int Record(string command)
        {
            _commands.Enqueue(command);
            return 0;
        }

        public int StateOf(string alias, out PreviewTrackState mode)
        {
            mode = PreviewTrackState.Playing;
            return 0;
        }

        public void Wait(WaitHandle wake, int milliseconds) => wake.WaitOne(Math.Min(milliseconds, 20));
    }
}
