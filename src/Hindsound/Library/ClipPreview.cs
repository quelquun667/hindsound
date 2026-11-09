using System.Runtime.InteropServices;
using Hindsound.Audio;
using Hindsound.Ui;

namespace Hindsound.Library;

public enum PreviewState
{
    Stopped,
    Opening,
    Playing,

    /// <summary>Played to its end. Nothing is left open, and the thread is on its way out.</summary>
    Ended,
    Error,
}

/// <summary>What the preview is doing, and for which line of the library.</summary>
internal readonly record struct PreviewChange(PreviewState State, ClipGroup? Clip);

/// <summary>What MCI is to the preview, so that a test plays no sound. All of it is called on one thread, the one of the preview.</summary>
internal interface IPreviewPlayer
{
    /// <summary>One MCI command string. Answers 0, or the MCI error.</summary>
    int Send(string command);

    /// <summary>The state of an open alias, one of the <c>MCI_MODE_</c> numbers. Answers 0, or the MCI error.</summary>
    int ModeOf(string alias, out int mode);

    /// <summary>Waits for <paramref name="wake"/> or the end of the delay, and answers what Windows sends the thread meanwhile.</summary>
    void Wait(WaitHandle wake, int milliseconds);
}

/// <summary>
/// Plays a line of the library, without copying the file. The thread lives only while something plays: with nothing playing
/// there is no thread, no timer and no handle. A file name can hold a first name, so the log gets MCI codes and the type of
/// an error, never a path or a message.
/// </summary>
/// <param name="player">MCI, or a fake.</param>
/// <param name="changed">
/// Told of every state, in order, on the thread of the preview. A state published before <see cref="Dispose"/> can still be
/// on its way in while Dispose runs, or after it has returned: Dispose never waits for the listener. What a listener passes on
/// to a window must check, on the window's own thread, that the window is still the one that asked.
/// </param>
/// <param name="journal">The log. Called on the thread of the preview.</param>
/// <param name="nowMs">The clock, for a test. A monotonic count by default.</param>
internal sealed class ClipPreview(IPreviewPlayer player, Action<PreviewChange> changed, Action<string>? journal = null, Func<long>? nowMs = null) : IDisposable
{
    public const string CantPlayText = "Can't play this file";
    public const string CanEndUpInClipText = "Playback can end up in your next clip on this setup.";

    internal const int ModeStopped = 525;
    internal const int ModePlaying = 526;

    /// <summary>How often the end is looked for. It only decides how late the state says it, not when the sound ends.</summary>
    internal const int PollMs = 250;

    /// <summary>
    /// A track can read as stopped just after <c>play</c> returned, before it has really started. A file too short to ever be
    /// seen playing still ends, this long after the start.
    /// </summary>
    internal const int StartGraceMs = 1000;

    private static int s_lastAlias;

    // Seams for a test, which has no other way to land a Play or a Stop in a gap of a few instructions. Both run on the thread of the preview.
    internal Action? AfterEndDecision { get; set; }

    internal Action? BeforeThreadLeaves { get; set; }

    private readonly object _gate = new();
    private readonly Func<long> _nowMs = nowMs ?? (static () => Environment.TickCount64);

    private ClipGroup? _next;
    private bool _stopWanted;
    private bool _closed;
    private AutoResetEvent? _wake;
    private Thread? _thread;
    private PreviewChange _current = new(PreviewState.Stopped, null);
    private long? _startGapMs;

    public PreviewChange Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// How long it took to start the two tracks of the last clip, one after the other. Null with one track. It is the gap
    /// between the calls: what the ear gets also holds whatever each graph takes to start, which MCI does not tell.
    /// </summary>
    public long? StartGapMs
    {
        get
        {
            lock (_gate)
            {
                return _startGapMs;
            }
        }
    }

    /// <summary>
    /// Whether the sentence about the next clip applies: the sound of the PC is captured, not one app, and this app's own sounds
    /// are not left out. MCI plays on the default output, which is the capture when the chosen output is that one. A microphone
    /// hearing the speakers can't be told from here, so it says nothing about that.
    /// </summary>
    public static bool CanEndUpInClip(AudioEngineStatus status) =>
        status.SystemCapturing && status.SourceApp is null && !status.OwnSoundsExcluded;

    /// <summary>The words for this line: only the line that failed says it failed.</summary>
    public static string? ErrorTextFor(PreviewChange change, ClipGroup? line) =>
        change.State == PreviewState.Error && IsSameLine(change.Clip, line) ? CantPlayText : null;

    /// <summary>Whether the button of this line says <c>Stop</c> rather than <c>Play</c>.</summary>
    public static bool ShowsStopFor(PreviewChange change, ClipGroup? line) =>
        change.State is PreviewState.Opening or PreviewState.Playing && IsSameLine(change.Clip, line);

    private static bool IsSameLine(ClipGroup? a, ClipGroup? b) =>
        a is { Files.Count: > 0 } && b is { Files.Count: > 0 } && string.Equals(a.Main.Path, b.Main.Path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Starts this line, in place of whatever plays. Returns at once: opening a file can take a quarter of a second.</summary>
    public void Play(ClipGroup clip)
    {
        var noThread = false;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _next = clip;
            if (_wake is { } wake)
            {
                wake.Set();
                return;
            }

            noThread = !TryStartThread();
        }

        if (noThread)
        {
            Publish(new PreviewChange(PreviewState.Error, clip));
        }
    }

    /// <summary>Stops what plays. Returns at once, and the state says <see cref="PreviewState.Stopped"/> once it is closed.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _next = null;
            _stopWanted = true;
            _wake?.Set();
        }
    }

    /// <summary>The window closes: the sound stops, the thread goes, and the window hears nothing more. Never waits for MCI.</summary>
    public void Dispose()
    {
        // The listener is called outside the lock, so it can be running now: it is not waited for.
        lock (_gate)
        {
            _closed = true;
        }

        Stop();
    }

    /// <summary>Waits for the thread to be gone, for a test or for a measure.</summary>
    internal bool WaitForEnd(int milliseconds)
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
        }

        return thread is null || thread.Join(milliseconds);
    }

    private bool TryStartThread()
    {
        var wake = new AutoResetEvent(false);
        try
        {
            _wake = wake;
            _thread = OffLoop.Start(() => Work(wake), ex => journal?.Invoke($"preview: failed ({ClipFolders.Describe(ex)})"));
            return true;
        }
        catch (Exception ex)
        {
            _wake = null;
            _next = null;
            wake.Dispose();
            journal?.Invoke($"preview: no thread ({ClipFolders.Describe(ex)})");
            return false;
        }
    }

    // One thread at a time, and it does the replacing itself: MCI wants its commands from the thread that opened the file,
    // and a second Play must not wait for an open that is still going on.
    private void Work(AutoResetEvent wake)
    {
        try
        {
            // The end of a session that a Play has replaced is not told: the next session tells its own. If a Stop then wipes that
            // Play before it is taken, nothing would tell it, and the button would say Stop for a sound that is gone.
            var withheld = false;
            while (true)
            {
                var clip = TakeNext(withheld);
                if (clip is null)
                {
                    if (!withheld)
                    {
                        break;
                    }

                    withheld = false;
                    Publish(new PreviewChange(PreviewState.Stopped, null));
                    continue;
                }

                withheld = false;
                var end = Session(clip, wake);
                if (IsReplaced())
                {
                    withheld = true;
                }
                else
                {
                    Publish(new PreviewChange(end, end == PreviewState.Stopped ? null : clip));
                }

                AfterEndDecision?.Invoke();
            }
        }
        finally
        {
            BeforeThreadLeaves?.Invoke();
            lock (_gate)
            {
                if (ReferenceEquals(_wake, wake))
                {
                    _wake = null;
                }
            }

            wake.Dispose();
        }
    }

    // keepWake: an end was withheld and is still to be told, so this thread stays the one that a Play wakes.
    private ClipGroup? TakeNext(bool keepWake)
    {
        lock (_gate)
        {
            var clip = _next;
            _next = null;
            _stopWanted = false;
            if (clip is null && !keepWake)
            {
                // In the same lock as the look at _next: a Play from now on starts a new thread.
                _wake = null;
            }

            return clip;
        }
    }

    private bool IsReplaced()
    {
        lock (_gate)
        {
            return _next is not null;
        }
    }

    private bool IsInterrupted()
    {
        lock (_gate)
        {
            return _stopWanted || _next is not null;
        }
    }

    private PreviewState Session(ClipGroup clip, AutoResetEvent wake)
    {
        var tracks = new List<Track>(clip.Files.Count);
        try
        {
            lock (_gate)
            {
                _startGapMs = null;
            }

            Publish(new PreviewChange(PreviewState.Opening, clip));
            foreach (var file in clip.Files)
            {
                if (IsInterrupted())
                {
                    return PreviewState.Stopped;
                }

                Open(file, tracks);
            }

            // Opening is the slow part, so both tracks are open before either starts: it keeps them as close as MCI allows.
            if (IsInterrupted())
            {
                return PreviewState.Stopped;
            }

            if (!StartAll(tracks))
            {
                return PreviewState.Error;
            }

            Publish(new PreviewChange(PreviewState.Playing, clip));
            return Follow(tracks, wake);
        }
        catch (Exception ex)
        {
            journal?.Invoke($"preview: failed ({ClipFolders.Describe(ex)})");
            return IsInterrupted() ? PreviewState.Stopped : PreviewState.Error;
        }
        finally
        {
            // Before the state is told: a state that says it is over means nothing is left open.
            CloseAll(tracks);
        }
    }

    private void Open(ClipFile file, List<Track> tracks)
    {
        var alias = "hindsound_preview_" + Interlocked.Increment(ref s_lastAlias);
        var code = player.Send($"open \"{ClipFolders.Key(file.Path)}\" type mpegvideo alias {alias}");
        if (code == 0)
        {
            tracks.Add(new Track(alias));
        }
        else
        {
            // A line whose other track opens still plays: one of the two can have been deleted on its own.
            journal?.Invoke($"preview: a track can't be opened (MCI error {code})");
        }
    }

    private bool StartAll(List<Track> tracks)
    {
        var first = _nowMs();
        var started = 0;
        foreach (var track in tracks)
        {
            var code = player.Send("play " + track.Alias);
            if (code == 0)
            {
                started++;
            }
            else
            {
                track.Fail();
                journal?.Invoke($"preview: a track can't be started (MCI error {code})");
            }
        }

        if (started > 1)
        {
            var gap = _nowMs() - first;
            lock (_gate)
            {
                _startGapMs = gap;
            }

            journal?.Invoke($"preview: {started} tracks started in {gap} ms");
        }

        return started > 0;
    }

    // The end comes from asking, not from a notify: a notify needs a window to land on, and this thread has none of its own.
    private PreviewState Follow(List<Track> tracks, AutoResetEvent wake)
    {
        var startedAt = _nowMs();
        while (true)
        {
            player.Wait(wake, PollMs);
            if (IsInterrupted())
            {
                return PreviewState.Stopped;
            }

            var elapsed = _nowMs() - startedAt;
            var left = 0;
            var failed = 0;
            for (var i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (!track.Finished)
                {
                    Look(track, elapsed);
                }

                left += track.Finished ? 0 : 1;
                failed += track.Failed ? 1 : 0;
            }

            if (left == 0)
            {
                return failed == tracks.Count ? PreviewState.Error : PreviewState.Ended;
            }
        }
    }

    private void Look(Track track, long elapsedMs)
    {
        var code = player.ModeOf(track.Alias, out var mode);
        if (code != 0)
        {
            track.Fail();
            journal?.Invoke($"preview: a track can't be followed (MCI error {code})");
        }
        else if (mode == ModePlaying)
        {
            track.SeenPlaying = true;
        }
        else if (mode == ModeStopped && (track.SeenPlaying || elapsedMs >= StartGraceMs))
        {
            track.Finished = true;
        }
    }

    private void CloseAll(List<Track> tracks)
    {
        foreach (var track in tracks)
        {
            try
            {
                var code = player.Send("close " + track.Alias);
                if (code != 0)
                {
                    journal?.Invoke($"preview: a track can't be closed (MCI error {code})");
                }
            }
            catch (Exception ex)
            {
                journal?.Invoke($"preview: a track can't be closed ({ClipFolders.Describe(ex)})");
            }
        }
    }

    private void Publish(PreviewChange change)
    {
        lock (_gate)
        {
            _current = change;
            if (_closed)
            {
                return;
            }
        }

        try
        {
            changed(change);
        }
        catch (Exception ex)
        {
            // An exception out of this thread would end the process, and the buffer with it.
            journal?.Invoke($"preview: a listener failed ({ClipFolders.Describe(ex)})");
        }
    }

    private sealed class Track(string alias)
    {
        public string Alias { get; } = alias;

        public bool SeenPlaying { get; set; }

        public bool Finished { get; set; }

        public bool Failed { get; private set; }

        public void Fail()
        {
            Finished = true;
            Failed = true;
        }
    }
}

/// <summary>
/// MCI itself. The <c>mpegvideo</c> type goes through DirectShow, which reads MP3 and WAV alike and plays on the default output.
/// The mode is read as a number: the words of <c>status mode</c> come from a string table of Windows, which can be translated.
/// </summary>
internal sealed unsafe partial class MciPreviewPlayer : IPreviewPlayer
{
    private const int InvalidDeviceName = 263;

    private const uint MCI_STATUS = 0x0814;
    private const nuint MCI_STATUS_ITEM = 0x00000100;
    private const uint MCI_STATUS_MODE = 0x00000004;

    private const uint QS_ALLINPUT = 0x04FF;
    private const uint MWMO_INPUTAVAILABLE = 0x0004;
    private const uint PM_REMOVE = 0x0001;

    // Enough for what a graph posts itself, and a thread that never stops being posted to still looks at its wake event.
    private const int MostMessagesPerWait = 64;

    public int Send(string command) => (int)mciSendStringW(command, null, 0, 0);

    public int ModeOf(string alias, out int mode)
    {
        mode = 0;
        var device = mciGetDeviceIDW(alias);
        if (device == 0)
        {
            return InvalidDeviceName;
        }

        var status = new StatusParameters { Item = MCI_STATUS_MODE };
        var code = mciSendCommandW(device, MCI_STATUS, MCI_STATUS_ITEM, (nuint)(&status));
        if (code == 0)
        {
            mode = (int)status.Return;
        }

        return (int)code;
    }

    // The documentation asks for no message loop by name. DirectShow creates hidden windows on the thread that builds a graph,
    // and a single-threaded apartment receives COM calls as messages, so the thread answers them rather than leave them waiting.
    public void Wait(WaitHandle wake, int milliseconds)
    {
        var handle = wake.SafeWaitHandle.DangerousGetHandle();
        _ = MsgWaitForMultipleObjectsEx(1, &handle, (uint)milliseconds, QS_ALLINPUT, MWMO_INPUTAVAILABLE);

        Message message;
        for (var i = 0; i < MostMessagesPerWait && PeekMessageW(&message, 0, 0, 0, PM_REMOVE) != 0; i++)
        {
            _ = DispatchMessageW(&message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StatusParameters
    {
        public nuint Callback;
        public nuint Return;
        public uint Item;
        public uint Track;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint mciSendStringW(string command, char* answer, uint answerChars, nint callback);

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint mciGetDeviceIDW(string alias);

    [LibraryImport("winmm.dll")]
    private static partial uint mciSendCommandW(uint device, uint message, nuint flags, nuint parameters);

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wakeMask, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int PeekMessageW(Message* message, nint window, uint first, uint last, uint remove);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Message* message);
}
