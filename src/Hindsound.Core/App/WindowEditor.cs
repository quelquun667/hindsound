using System.Globalization;
using System.IO;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.App;

/// <summary>
/// What the editor page of one window gets from the disk: the clip decoded, the selection played, the edit written. It lives
/// and dies with that window. The work runs off the loop, and every answer comes back through it.
/// </summary>
/// <param name="screen">The screen of the window, the only one this ever answers to.</param>
/// <param name="post">Runs an action on the thread of the loop.</param>
/// <param name="decoder">Reads the files of a clip back.</param>
/// <param name="recycleBin">Where a replaced original goes. Never a deletion for good.</param>
/// <param name="index">The labels that follow a replaced original, and the collections a copy joins.</param>
/// <param name="export">The export settings as of now: the bitrate of an mp3, the ceiling of the peaks.</param>
/// <param name="player">What plays the selection: MCI, or a fake. Without it, Play does nothing.</param>
/// <param name="mayPlay">Whether this file may be played at all; off screen, only pure silence. Without it, any may.</param>
/// <param name="stopLibraryPreview">Stops what the list of the Library plays, before the selection plays.</param>
/// <param name="opener">Opens <c>discord://</c>. Null off screen: nothing is opened.</param>
/// <param name="shell">Shows a file in its folder.</param>
/// <param name="tempFolder">The temporary folder of the system, where the WAV of the selection is written, under a name of its own.</param>
/// <param name="journal">Told of failures, by type and code only: a file name can hold a first name.</param>
internal sealed class WindowEditor(
    SettingsScreen screen,
    Action<Action> post,
    IClipDecoder decoder,
    IRecycleBin recycleBin,
    IClipIndex index,
    Func<ExportSettings> export,
    Func<IPreviewPlayer>? player,
    Func<ClipGroup, bool>? mayPlay,
    Action stopLibraryPreview,
    IFileOpener? opener,
    IFolderShell shell,
    string tempFolder,
    Action<string> journal) : IEditorPanel
{
    private static int s_lastPreview;

    private readonly SettingsScreen _screen = screen;
    private readonly Action<Action> _post = post;
    private readonly Func<ExportSettings> _export = export;
    private readonly Func<IPreviewPlayer>? _player = player;
    private readonly Func<ClipGroup, bool>? _mayPlay = mayPlay;
    private readonly Action _stopLibraryPreview = stopLibraryPreview;
    private readonly IFileOpener? _opener = opener;
    private readonly IFolderShell _shell = shell;
    private readonly string _tempFolder = tempFolder;
    private readonly Action<string> _journal = journal;
    private readonly ClipEditFiles _files = new(decoder, recycleBin, index, journal);

    // The work runs one piece at a time, in the order asked: a save asked while the clip is read waits for it.
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private CancellationTokenSource _cancel = new();

    // Cancels the selections asked to play so far: a Stop that comes while one is written is not lost.
    private CancellationTokenSource _stopPreview = new();

    // The WAVs of the selection not yet removed, and the one handed to the player last, which stays while it may play.
    private readonly List<string> _previewFiles = [];
    private string? _previewPlaying;

    // On the loop only. A session is one opening of the page: an answer of an older one reaches nobody.
    private int _session;
    private ClipGroup? _clip;
    private IReadOnlyList<int> _channels = [];
    private ExportFormat _format = ExportFormat.Mp3;
    private ClipPreview? _preview;

    /// <summary>The bars of one file of the clip. A test puts its own.</summary>
    internal Func<IDecodedStream, CancellationToken, IReadOnlyList<float>> Peaks { get; init; } = (stream, cancel) => EditPeaks.Compute(stream, EditPeaks.BarCount, cancel);

    /// <summary>The disk side, for a test that changes its bounds.</summary>
    internal ClipEditFiles Files
    {
        get => _files;
        init => _files = value;
    }

    /// <summary>The preview of the selection, for a test. Null until the first Play, and after Close.</summary>
    internal ClipPreview? Preview => _preview;

    /// <summary>Waits until the work asked so far is done, for a test.</summary>
    internal bool WaitForWork(int milliseconds)
    {
        Task tail;
        lock (_gate)
        {
            tail = _tail;
        }

        return tail.Wait(milliseconds);
    }

    public void Load(ClipGroup clip)
    {
        var session = ++_session;
        _clip = clip;
        _channels = [];
        _format = ClipEditRender.FormatOf(clip.Main.Path);
        var peaks = Peaks;
        Queue("read", cancel =>
        {
            ClipEditReading reading;
            try
            {
                reading = _files.Load(clip, peaks, cancel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The page waits for an answer: whatever went wrong, it gets one.
                _journal($"editor: clip not read ({ClipFolders.Describe(ex)})");
                reading = new ClipEditReading(new EditorLoad(EditorLoadOutcome.Unreadable, TimeSpan.Zero, [], ClipEditRender.FormatOf(clip.Main.Path)), []);
            }

            _post(() =>
            {
                if (session != _session)
                {
                    return;
                }

                _channels = reading.Channels;
                _format = reading.Load.Format;
                _screen.EditorLoaded(reading.Load);
            });
        });
    }

    public long EstimateBytes(EditRequest request)
    {
        var seconds = Math.Max(0, request.Kept.TotalSeconds);
        if (request.Mode == EditorMode.Soundboard)
        {
            return Mp3Bytes(ClipEditRender.SoundboardBitrateKbps, seconds);
        }

        var files = Math.Max(1, _channels.Count > 0 ? _channels.Count : request.GainsDb.Count);
        long total = 0;
        for (var i = 0; i < files; i++)
        {
            var channels = i < _channels.Count ? _channels[i] : 2;
            total += _format == ExportFormat.Wav
                ? (long)Math.Round(InternalFormat.SampleRate * channels * 2 * seconds) + 44
                : Mp3Bytes(ClipEditRender.Bitrate(_export().Mp3Bitrate), seconds);
        }

        return total;
    }

    public void Play(EditRequest request)
    {
        if (_player is null || _clip is not { } clip)
        {
            return;
        }

        var session = _session;
        if (_mayPlay is { } may && !may(clip))
        {
            // Told on the loop, as a real failure would be: the page gets every state the same way.
            _journal("editor preview: not played, not a file this run may play");
            _post(() => TellPreview(session, PreviewState.Error));
            return;
        }

        _stopLibraryPreview();
        var preview = _preview ??= NewPreview();
        DeletePreviewFiles();
        _post(() => TellPreview(session, PreviewState.Opening));
        var export = Snapshot();
        var path = Path.Combine(_tempFolder, string.Create(CultureInfo.InvariantCulture, $"Hindsound-preview-{Environment.ProcessId}-{Interlocked.Increment(ref s_lastPreview)}.wav"));
        CancellationToken stop;
        lock (_gate)
        {
            stop = _stopPreview.Token;
        }

        Queue("preview", left =>
        {
            using var both = CancellationTokenSource.CreateLinkedTokenSource(left, stop);
            var cancel = both.Token;
            try
            {
                _files.WritePreview(clip, request, path, export, cancel);
            }
            catch (Exception ex)
            {
                DeleteFile(path);
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                _journal($"editor preview: not written ({ClipFolders.Describe(ex)})");
                _post(() => TellPreview(session, PreviewState.Error));
                return;
            }

            lock (_gate)
            {
                if (cancel.IsCancellationRequested)
                {
                    DeleteFile(path);
                    return;
                }

                _previewFiles.Add(path);
                _previewPlaying = path;
            }

            preview.Play(new ClipGroup([new ClipFile(path, new FileInfo(path).Length, DateTime.UtcNow)]));
        });
    }

    public void StopPlaying()
    {
        lock (_gate)
        {
            // In the lock a written WAV is taken in, as for Close: one still being written is removed, never played.
            _stopPreview.Cancel();
            _stopPreview = new CancellationTokenSource();
            _previewPlaying = null;
        }

        _preview?.Stop();

        // Told at once: a selection stopped while its WAV was written never reaches the player, which would tell nothing.
        var session = _session;
        _post(() => TellPreview(session, PreviewState.Stopped));

        // A file the player still holds goes when the player says it has stopped.
        DeletePreviewFiles();
    }

    public void Save(EditRequest request, EditSave save)
    {
        var session = _session;
        if (_clip is not { } clip)
        {
            _post(() => TellSaved(session, new EditSaveResult(EditSaveOutcome.Failed, [])));
            return;
        }

        var export = Snapshot();
        Queue("save", _ =>
        {
            // Never cancelled by leaving the page: a save asked goes to its end, and its answer is told all the same.
            var result = _files.Save(clip, request, save, export, CancellationToken.None);
            _post(() => TellSaved(session, result));
        });
    }

    public void HandToDiscord(string path)
    {
        // Opening a protocol and showing a folder can both wait on the shell: neither runs on the loop.
        try
        {
            OffLoop.Start(
                () =>
                {
                    if (_opener?.Open(Soundboard.DiscordUri) is not null)
                    {
                        // Discord may well not be installed: that is all this says.
                        _journal("soundboard: Discord not opened");
                    }

                    var shown = _shell.ShowSelected(Path.GetDirectoryName(path) ?? "", [path]);
                    if (shown < 0)
                    {
                        _journal(string.Create(CultureInfo.InvariantCulture, $"soundboard: folder not shown (0x{shown:X8})"));
                    }
                },
                ex => _journal($"soundboard: failed ({ClipFolders.Describe(ex)})"));
        }
        catch (Exception ex)
        {
            _journal($"soundboard: not handed ({ClipFolders.Describe(ex)})");
        }
    }

    public void Close()
    {
        try
        {
            _session++;
            _clip = null;
            _channels = [];
            lock (_gate)
            {
                // In the lock a written WAV is taken in: from here, one written for this session is removed, never played.
                _cancel.Cancel();
                _cancel = new CancellationTokenSource();
                _previewPlaying = null;
            }

            var preview = _preview;
            _preview = null;
            preview?.Dispose();
            if (!DeletePreviewFiles() && preview is not null)
            {
                // The player lets go of its file on its own thread, which is not waited for here.
                Task.Run(() =>
                {
                    preview.WaitForEnd(5000);
                    DeletePreviewFiles();
                });
            }
        }
        catch (Exception ex)
        {
            _journal($"editor: not closed cleanly ({ClipFolders.Describe(ex)})");
        }
    }

    private static long Mp3Bytes(int kbps, double seconds) => (long)Math.Round(kbps * 1000 * seconds / 8) + 1024;

    private ClipPreview NewPreview()
    {
        ClipPreview? made = null;
        made = new ClipPreview(_player!(), change =>
        {
            // On the thread of the preview, once the player has let go of what it no longer plays.
            DeletePreviewFiles();
            _post(() =>
            {
                if (ReferenceEquals(_preview, made))
                {
                    _screen.EditorPreviewChanged(change.State);
                }
            });
        }, _journal);
        return made;
    }

    private void TellPreview(int session, PreviewState state)
    {
        if (session == _session)
        {
            _screen.EditorPreviewChanged(state);
        }
    }

    // The screen takes the answer of a save with its page closed too. A page opened since is another edit, which never
    // takes it for its own.
    private void TellSaved(int session, EditSaveResult result)
    {
        if (session != _session)
        {
            // Never a path: the line says only that the save ended after the page was left, and how.
            _journal(result.Outcome is EditSaveOutcome.Saved or EditSaveOutcome.SavedAside
                ? "editor: saved after the page was left"
                : $"editor: not saved after the page was left ({result.Outcome})");
        }

        if (session == _session || _clip is null)
        {
            _screen.EditorSaved(result);
        }
    }

    private ExportSettings Snapshot()
    {
        var export = _export();
        return new ExportSettings { Mp3Bitrate = export.Mp3Bitrate, PeakCeilingDb = export.PeakCeilingDb };
    }

    private void Queue(string what, Action<CancellationToken> work)
    {
        lock (_gate)
        {
            var cancel = _cancel.Token;
            _tail = _tail.ContinueWith(
                _ =>
                {
                    try
                    {
                        work(cancel);
                    }
                    catch (OperationCanceledException)
                    {
                        // The page was left: nothing is told, and nothing of the work is left on disk.
                    }
                    catch (Exception ex)
                    {
                        _journal($"editor {what}: failed ({ClipFolders.Describe(ex)})");
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    /// <summary>Removes every WAV of the selection but the one that may be playing. True if none is left over.</summary>
    private bool DeletePreviewFiles()
    {
        lock (_gate)
        {
            for (var i = _previewFiles.Count - 1; i >= 0; i--)
            {
                var path = _previewFiles[i];
                if (!string.Equals(path, _previewPlaying, StringComparison.Ordinal) && DeleteFile(path))
                {
                    _previewFiles.RemoveAt(i);
                }
            }

            return _previewFiles.Count == 0;
        }
    }

    private static bool DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
