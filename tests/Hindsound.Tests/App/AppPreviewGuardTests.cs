using System.Collections.Concurrent;
using System.IO;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// What the app lets the Library play. A run drawn off screen is a test or a probe and must never make a sound: it plays a WAV
/// read back as pure silence and nothing else. The player here is a fake that notes every command, so nothing is ever heard.
/// </summary>
public sealed class AppPreviewGuardTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly List<NotingPlayer> _players = [];

    private HindsoundApp? _app;

    public void Dispose()
    {
        _app?.LibraryWindowClosed();
        _rig.Dispose();
        _temp.Dispose();
    }

    private string Clips => _temp.Combine("clips");

    private string Log => _temp.Combine("settings.log");

    private HindsoundApp Built(params string[] flags)
    {
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic", .. flags]);
        Directory.CreateDirectory(Clips);
        return _app = new HindsoundApp(options, new Journal(Log))
        {
            PostInstead = _loop.Enqueue,
            LibraryFoldersInstead = () => new ClipScanRequest { OutputFolder = Clips, RescueFolder = _temp.Combine("rescued") },
            PreviewPlayerInstead = () =>
            {
                var player = new NotingPlayer();
                _players.Add(player);
                return player;
            },
        };
    }

    private static string SharedText(string path)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void Turn(Func<bool> until)
    {
        var end = Environment.TickCount64 + 20_000;
        while (!until())
        {
            if (_loop.TryDequeue(out var work))
            {
                work();
            }
            else if (Environment.TickCount64 > end)
            {
                Assert.Fail("the loop never got there");
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private bool Shows(string text)
    {
        _rig.Canvas.Drawn.Clear();
        _rig.Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn.Contains(text);
    }

    private void Silent(string name)
    {
        Directory.CreateDirectory(Clips);
        SilentWav.Write(Path.Combine(Clips, name), 200);
    }

    private void Sound(string name)
    {
        Directory.CreateDirectory(Clips);
        File.WriteAllBytes(Path.Combine(Clips, name), [1, 2, 3]);
    }

    private void PlayTheChosen(HindsoundApp app, string shownAs, string chosen)
    {
        app.WireScreen(_rig.Screen);
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.Screen.ShowSection(Section.Library);
        Turn(() => Shows(shownAs));
        _rig.Screen.SelectClip(Path.Combine(Clips, chosen));
        _rig.FocusOn(SettingsScreen.ClipPlayId);
        _rig.Press(Vk.Enter);
    }

    private static bool Opened(NotingPlayer player) => player.Commands.Any(command => command.StartsWith("open ", StringComparison.Ordinal));

    [Theory]
    [InlineData("--offscreen")]
    [InlineData("--settings-probe")]
    public void A_run_off_screen_or_a_probe_refuses_a_clip_that_is_not_silence_and_opens_nothing(string flag)
    {
        Sound("pets.mp3");
        var app = Built(flag);

        PlayTheChosen(app, "pets", "pets.mp3");

        Turn(() => _rig.Screen.Preview.State == PreviewState.Error);
        Turn(() => Shows(ClipPreview.CantPlayText));
        Assert.All(_players, player => Assert.False(Opened(player)));
        var log = SharedText(Log);
        Assert.Contains("preview: not played, not a file this run may play", log, StringComparison.Ordinal);
        Assert.DoesNotContain("pets", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_wav_with_one_sound_byte_in_it_is_refused_off_screen()
    {
        Silent("dogs.wav");
        var path = Path.Combine(Clips, "dogs.wav");
        var bytes = File.ReadAllBytes(path);
        bytes[^1] = 1;
        File.WriteAllBytes(path, bytes);
        var app = Built("--offscreen");

        PlayTheChosen(app, "dogs", "dogs.wav");

        Turn(() => _rig.Screen.Preview.State == PreviewState.Error);
        Assert.All(_players, player => Assert.False(Opened(player)));
    }

    [Fact]
    public void A_clip_of_two_tracks_needs_both_to_be_silent()
    {
        Silent("pets_system.wav");
        Sound("pets_mic.wav");
        var app = Built("--offscreen");

        PlayTheChosen(app, "pets", "pets_system.wav");

        Turn(() => _rig.Screen.Preview.State == PreviewState.Error);
        Assert.All(_players, player => Assert.False(Opened(player)));
    }

    [Fact]
    public void A_wav_of_pure_silence_is_played_off_screen()
    {
        Silent("dogs.wav");
        var app = Built("--offscreen");

        PlayTheChosen(app, "dogs", "dogs.wav");

        Turn(() => _rig.Screen.Preview.State == PreviewState.Playing);
        Assert.Single(_players);
        Assert.True(Opened(_players[0]));
        Assert.DoesNotContain("not a file this run may play", SharedText(Log), StringComparison.Ordinal);
    }

    [Fact]
    public void A_normal_run_has_no_such_guard_and_gives_any_clip_to_the_player()
    {
        Sound("pets.mp3");
        var app = Built();

        PlayTheChosen(app, "pets", "pets.mp3");

        Turn(() => _rig.Screen.Preview.State == PreviewState.Playing);
        Assert.True(Opened(_players[0]));
    }

    /// <summary>Opens and plays nothing: every track reads as playing until it is closed.</summary>
    private sealed class NotingPlayer : IPreviewPlayer
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
