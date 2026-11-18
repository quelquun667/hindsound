using System.Collections.Concurrent;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Library;
using Hindsound.Settings;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>The editor of one window: the selection heard from a WAV of its own, which goes as soon as it is not played, and the sizes it shows.</summary>
public sealed class WindowEditorTests : IDisposable
{
    private const int Rate = InternalFormat.SampleRate;
    private readonly ScreenRig _rig = new();
    private readonly EditFolder _clips = new();
    private readonly EditFolder _temp = new();
    private readonly FakeDecoder _decoder = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly ConcurrentQueue<string> _events = new();
    private readonly ConcurrentQueue<string> _journal = new();
    private readonly ExportSettings _export = new() { Mp3Bitrate = 192, PeakCeilingDb = -1 };
    private Func<ClipGroup, bool>? _mayPlay;
    private WindowEditor? _editor;

    public void Dispose()
    {
        _editor?.Close();
        _editor?.Preview?.WaitForEnd(5000);
        _rig.Dispose();
        _clips.Dispose();
        _temp.Dispose();
    }

    private WindowEditor Editor(IClipDecoder? decoder = null)
    {
        _editor = new WindowEditor(
            _rig.Screen,
            _loop.Enqueue,
            decoder ?? _decoder,
            new FakeBin(RecycleOutcome.CannotRecycle),
            new FakeIndex(),
            () => _export,
            () => new NotingPlayer(_events),
            _mayPlay,
            () => _events.Enqueue("stop library"),
            null,
            new OffscreenFolderShell(_journal.Enqueue),
            _temp.Path,
            _journal.Enqueue);
        return _editor;
    }

    private ClipGroup Clip(params (string Name, int Channels)[] files) =>
        new([.. files.Select(file => new ClipFile(_clips.Clip(file.Name, _decoder, new float[Rate * file.Channels], file.Channels), 4, default))]);

    private static EditRequest Selection(double start, double end, int files = 1) =>
        new(EditorMode.Normal, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), [.. Enumerable.Repeat(0.0, files)]);

    private WindowEditor Loaded(ClipGroup clip, IClipDecoder? decoder = null)
    {
        var editor = Editor(decoder);
        editor.Load(clip);
        Assert.True(editor.WaitForWork(10_000));
        PumpAll();
        return editor;
    }

    private void PumpAll()
    {
        while (_loop.TryDequeue(out var work))
        {
            work();
        }
    }

    /// <summary>Runs what reached the loop until this holds, or fails after a while: the player answers on a thread of its own.</summary>
    private void PumpUntil(Func<bool> done)
    {
        var until = Environment.TickCount64 + 10_000;
        while (!done())
        {
            Assert.True(Environment.TickCount64 < until, "never came");
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

    private string[] Opened() => [.. _events.Where(e => e.StartsWith("open ", StringComparison.Ordinal))];

    [Fact]
    public void The_list_stops_before_the_selection_plays_from_a_wav_of_its_own_which_goes_when_it_stops()
    {
        var editor = Loaded(Clip(("dogs.mp3", 2)));

        editor.Play(Selection(0.2, 0.6));
        Assert.True(editor.WaitForWork(10_000));
        PumpUntil(() => Opened().Length == 1);

        var events = _events.ToArray();
        var stopped = Array.IndexOf(events, "stop library");
        Assert.True(stopped >= 0, "the list was never stopped");
        Assert.True(stopped < Array.FindIndex(events, e => e.StartsWith("open ", StringComparison.Ordinal)), "the list was stopped after the selection opened");
        var wav = Assert.Single(Directory.GetFiles(_temp.Path));
        Assert.Matches(@"^Hindsound-preview-\d+-\d+\.wav$", Path.GetFileName(wav));
        Assert.Contains(Path.GetFileName(wav), Opened()[0], StringComparison.OrdinalIgnoreCase);

        editor.StopPlaying();
        PumpUntil(() => Directory.GetFiles(_temp.Path).Length == 0);
    }

    [Fact]
    public void A_new_play_removes_the_wav_of_the_last_one_and_closing_removes_every_one()
    {
        var editor = Loaded(Clip(("dogs_system.wav", 2), ("dogs_mic.wav", 1)));

        editor.Play(Selection(0, 0.5, 2));
        Assert.True(editor.WaitForWork(10_000));
        PumpUntil(() => Opened().Length == 1);
        var first = Assert.Single(Directory.GetFiles(_temp.Path));

        editor.Play(Selection(0.5, 1, 2));
        Assert.True(editor.WaitForWork(10_000));
        PumpUntil(() => Opened().Length == 2 && !File.Exists(first));
        Assert.Single(Directory.GetFiles(_temp.Path));

        var preview = editor.Preview!;
        editor.Close();
        Assert.Null(editor.Preview);
        Assert.True(preview.WaitForEnd(5000));
        PumpUntil(() => Directory.GetFiles(_temp.Path).Length == 0);
    }

    [Fact]
    public void A_clip_this_run_may_not_play_opens_nothing_and_writes_nothing()
    {
        _mayPlay = _ => false;
        var editor = Loaded(Clip(("dogs.mp3", 2)));

        editor.Play(Selection(0, 0.5));
        Assert.True(editor.WaitForWork(10_000));
        PumpAll();

        Assert.Empty(Opened());
        Assert.Null(editor.Preview);
        Assert.Empty(Directory.GetFiles(_temp.Path));
        Assert.DoesNotContain("stop library", _events);
    }

    [Fact]
    public void The_size_shown_follows_the_format_the_bitrate_and_the_files_of_the_clip()
    {
        var mp3 = Loaded(Clip(("dogs.mp3", 2)));
        Assert.Equal((192 * 1000 * 10 / 8) + 1024, mp3.EstimateBytes(Selection(5, 15)));

        var soundboard = new EditRequest(EditorMode.Soundboard, TimeSpan.Zero, TimeSpan.FromSeconds(5), [0]);
        Assert.Equal((128 * 1000 * 5 / 8) + 1024, mp3.EstimateBytes(soundboard));
        mp3.Close();

        var wav = Loaded(Clip(("cats_system.wav", 2), ("cats_mic.wav", 1)));
        Assert.Equal((Rate * 2 * 2 * 2) + 44 + (Rate * 1 * 2 * 2) + 44, wav.EstimateBytes(Selection(1, 3, 2)));
    }

    [Fact]
    public void A_save_asked_goes_to_its_end_once_the_page_is_left_and_its_answer_is_told()
    {
        // The clip is held while it is read for the save, and the page is left right then.
        var gate = new ManualResetEventSlim(true);
        var editor = Loaded(Clip(("dogs.wav", 1)), new GatedDecoder(_decoder, gate));
        gate.Reset();

        editor.Save(Selection(0, 0.5), new EditSave(EditSaveChoice.Copy, "dogs (cut)"));
        editor.Close();
        gate.Set();
        Assert.True(editor.WaitForWork(10_000));

        Assert.Equal(["dogs (cut).wav", "dogs.wav"], _clips.Names());
        Assert.Single(_loop);
        PumpAll();
        Assert.Contains("editor: saved after the page was left", _journal);
        Assert.DoesNotContain(_journal, line => line.Contains(_clips.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_save_that_fails_after_another_page_was_opened_is_written_to_the_log_without_a_path()
    {
        var gate = new ManualResetEventSlim(true);
        var editor = Loaded(Clip(("dogs.wav", 1)), new GatedDecoder(_decoder, gate));
        gate.Reset();
        _decoder.Files.Clear();

        // The decoder knows no file any more: the save fails. Another page is opened before it ends, so no screen is told.
        editor.Save(Selection(0, 0.5), new EditSave(EditSaveChoice.Copy, "dogs (cut)"));
        editor.Close();
        editor.Load(Clip(("cats.wav", 1)));
        gate.Set();
        Assert.True(editor.WaitForWork(10_000));
        PumpAll();

        Assert.Contains("editor: not saved after the page was left (Failed)", _journal);
        Assert.DoesNotContain(_journal, line => line.Contains(_clips.Path, StringComparison.OrdinalIgnoreCase) || line.Contains("dogs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_save_that_ends_after_the_page_was_left_is_still_told_to_the_screen()
    {
        var library = new PlayingLibraryFake();
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.Screen.SetLibrary(library);
        _rig.Screen.ShowSection(Section.Library);
        var gate = new ManualResetEventSlim(true);
        var editor = Loaded(Clip(("dogs.wav", 1)), new GatedDecoder(_decoder, gate));
        gate.Reset();

        editor.Save(Selection(0, 0.5), new EditSave(EditSaveChoice.Copy, "dogs (cut)"));
        editor.Close();
        gate.Set();
        Assert.True(editor.WaitForWork(10_000));

        // The reading that brings the copy is there before the answer: the screen chooses the copy only once it is told.
        var copy = Path.Combine(_clips.Path, "dogs (cut).wav");
        library.Lines = new ClipLines([.. library.Lines!.Lines, new ClipGroup([new ClipFile(copy, 4, default)])], 0);
        _rig.Screen.LibraryReadingArrived();
        Assert.NotEqual(copy, _rig.Screen.EditorChosenPath);

        PumpAll();
        Assert.Equal(copy, _rig.Screen.EditorChosenPath);
    }

    [Fact]
    public void Leaving_the_page_while_the_clip_is_read_stops_the_reading_and_tells_nothing()
    {
        var gate = new ManualResetEventSlim(false);
        var editor = Editor(new GatedDecoder(_decoder, gate));

        editor.Load(Clip(("dogs.wav", 1)));
        editor.Close();
        gate.Set();
        Assert.True(editor.WaitForWork(10_000));

        Assert.Empty(_loop);
    }

    [Fact]
    public void Leaving_the_page_while_the_wav_of_the_selection_is_written_removes_it_and_opens_nothing()
    {
        var gate = new ManualResetEventSlim(true);
        var editor = Loaded(Clip(("dogs.mp3", 2)), new GatedDecoder(_decoder, gate));
        gate.Reset();

        editor.Play(Selection(0.2, 0.6));
        editor.Close();
        gate.Set();
        Assert.True(editor.WaitForWork(10_000));

        Assert.Empty(Directory.GetFiles(_temp.Path));
        Assert.Empty(Opened());
    }

    [Fact]
    public void A_stop_while_the_wav_of_the_selection_is_written_removes_it_and_plays_nothing()
    {
        var gate = new ManualResetEventSlim(true);
        var editor = Loaded(Clip(("dogs.mp3", 2)), new GatedDecoder(_decoder, gate));
        gate.Reset();

        editor.Play(Selection(0.2, 0.6));
        editor.StopPlaying();
        gate.Set();
        Assert.True(editor.WaitForWork(10_000));

        // Never handed to the player: the WAV is gone at once.
        Assert.Empty(Directory.GetFiles(_temp.Path));
        Assert.Empty(Opened());
    }

    [Fact]
    public void A_soundboard_copy_opens_discord_then_shows_the_file_off_the_loop()
    {
        var shown = new ManualResetEventSlim();
        var opened = new ConcurrentQueue<string>();
        var shell = new NotingShell(shown);
        _editor = new WindowEditor(
            _rig.Screen, _loop.Enqueue, _decoder, new FakeBin(RecycleOutcome.CannotRecycle), new FakeIndex(), () => _export, null, null, () => { },
            new FailingOpener(opened), shell, _temp.Path, _journal.Enqueue);
        var path = Path.Combine(_clips.Path, "dogs (soundboard).mp3");

        _editor.HandToDiscord(path);

        Assert.True(shown.Wait(10_000));
        Assert.Equal(["discord://"], opened);
        Assert.Equal((_clips.Path, path), shell.Shown);
        Assert.Contains("soundboard: Discord not opened", _journal);
        Assert.Empty(_loop);
    }

    /// <summary>Opens a file only once the gate is open: the work of the editor is caught halfway.</summary>
    private sealed class GatedDecoder(IClipDecoder inner, ManualResetEventSlim gate) : IClipDecoder
    {
        public IDecodedStream Open(string path)
        {
            Assert.True(gate.Wait(10_000), "the gate never opened");
            return inner.Open(path);
        }
    }

    /// <summary>Answers as a system without Discord would.</summary>
    private sealed class FailingOpener(ConcurrentQueue<string> opened) : IFileOpener
    {
        public string? Open(string target)
        {
            opened.Enqueue(target);
            return "no application for this protocol";
        }
    }

    private sealed class NotingShell(ManualResetEventSlim shown) : IFolderShell
    {
        public (string Folder, string File)? Shown { get; private set; }

        public int ShowSelected(string folder, IReadOnlyList<string> files)
        {
            Shown = (folder, files.Single());
            shown.Set();
            return 0;
        }
    }

    /// <summary>Plays nothing: every track reads as playing until it is closed, and each verb is noted with the others.</summary>
    private sealed class NotingPlayer(ConcurrentQueue<string> events) : IPreviewPlayer
    {
        public int Open(string path, string track) => Note($"open {path} as {track}");

        public int Play(string track) => Note("play " + track);

        public int Stop(string track) => Note("stop " + track);

        public int Close(string track) => Note("close " + track);

        private int Note(string command)
        {
            events.Enqueue(command);
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
