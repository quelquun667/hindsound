using Hindsound.Library;

namespace Hindsound.Linux.Audio;

/// <summary>
/// The preview of a clip on Linux: each track is a playback stream of the sound server, fed with the samples an
/// <see cref="IClipDecoder"/> reads, on the default output. While a track plays, it holds <see cref="PulsePlaybackSilence"/>,
/// so the capture of the PC's sound writes silence rather than the clip being listened to.
/// <para>
/// One connection serves every open track, made when the first one opens and closed with the last: with nothing open there is
/// no connection and no thread. A connection the server dropped is not used again; the next track makes a new one.
/// </para>
/// The verbs are called on the preview's thread; the streams call their tracks back on the connection's thread. The log gets
/// the type of an error, never a path or a message: a file name can hold a first name.
/// </summary>
internal sealed class PulsePreviewPlayer : IPreviewPlayer, IDisposable
{
    // The player's own error codes: any code but zero is an error to the preview.
    public const int UnknownTrack = 1;
    public const int Unreadable = 2;
    public const int NoServer = 3;
    public const int TrackFailed = 4;
    public const int TrackInUse = 5;
    public const int PlayerClosed = 6;

    /// <summary>A hold of the silence lasts at most the file's length and this much more, if its track is never closed.</summary>
    public static readonly TimeSpan HoldSlack = TimeSpan.FromSeconds(5);

    private readonly IClipDecoder _decoder;
    private readonly PulsePlaybackSilence _silence;
    private readonly Func<IPulsePlaybackOutput> _connect;
    private readonly Action<string> _log;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private IPulsePlaybackOutput? _output;
    private bool _disposed;

    /// <param name="decoder">Reads the files: <c>UnixClipDecoder</c> in the app.</param>
    /// <param name="silence">The silence the capture of the PC's sound reads; the same one the capture factory was given.</param>
    /// <param name="log">The log of the run.</param>
    public PulsePreviewPlayer(IClipDecoder decoder, PulsePlaybackSilence silence, Action<string>? log = null)
        : this(decoder, silence, ConnectToServer, log)
    {
    }

    /// <param name="connect">Makes a connection; a test hands a fake.</param>
    internal PulsePreviewPlayer(IClipDecoder decoder, PulsePlaybackSilence silence, Func<IPulsePlaybackOutput> connect, Action<string>? log = null)
    {
        _decoder = decoder;
        _silence = silence;
        _connect = connect;
        _log = log ?? (static _ => { });
    }

    public int Open(string path, string track)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return PlayerClosed;
            }

            if (_tracks.ContainsKey(track))
            {
                return TrackInUse;
            }

            IDecodedStream decoded;
            try
            {
                decoded = _decoder.Open(path);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _log($"preview: a file can't be read ({Describe(exception)})");
                return Unreadable;
            }

            var item = new Track(decoded, _silence, _log);
            try
            {
                item.Stream = OutputNow().Open(decoded.Channels, item);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                decoded.Dispose();
                _log($"preview: the sound server refused a track ({Describe(exception)})");
                ReleaseIdleOutput();
                return NoServer;
            }

            _tracks.Add(track, item);
            return 0;
        }
    }

    public int Play(string track)
    {
        lock (_gate)
        {
            return _tracks.TryGetValue(track, out var item) ? item.Play() : UnknownTrack;
        }
    }

    public int Stop(string track)
    {
        lock (_gate)
        {
            return _tracks.TryGetValue(track, out var item) ? item.Stop() : UnknownTrack;
        }
    }

    public int Close(string track)
    {
        lock (_gate)
        {
            if (!_tracks.Remove(track, out var item))
            {
                return UnknownTrack;
            }

            try
            {
                item.Close();
            }
            finally
            {
                ReleaseIdleOutput();
            }

            return 0;
        }
    }

    public int StateOf(string track, out PreviewTrackState state)
    {
        lock (_gate)
        {
            if (!_tracks.TryGetValue(track, out var item))
            {
                state = PreviewTrackState.Other;
                return UnknownTrack;
            }

            if (_output is { IsLost: true })
            {
                item.Failed("the connection to the sound server ended");
            }

            return item.StateOf(out state);
        }
    }

    // The streams call back on their own thread: this thread has nothing to answer meanwhile.
    public void Wait(WaitHandle wake, int milliseconds) => wake.WaitOne(milliseconds);

    /// <summary>Closes every track and the connection: the sound stops, and the capture hears again.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var item in _tracks.Values)
            {
                try
                {
                    item.Close();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    _log($"preview: a track can't be closed ({Describe(exception)})");
                }
            }

            _tracks.Clear();
            ReleaseIdleOutput();
        }
    }

    private static string Describe(Exception exception) => ClipFolders.Describe(exception);

    private static IPulsePlaybackOutput ConnectToServer() => OperatingSystem.IsLinux()
        ? PulsePlaybackOutput.Connect()
        : throw new PlatformNotSupportedException("The sound server is reached on Linux only.");

    private IPulsePlaybackOutput OutputNow()
    {
        if (_output is { IsLost: true } && _tracks.Count == 0)
        {
            ReleaseIdleOutput();
        }

        if (_output is { IsLost: true })
        {
            // Tracks still open on the lost connection keep it until they close; the new track gets one of its own.
            throw new IOException("The connection to the sound server ended while a track was open.");
        }

        return _output ??= _connect();
    }

    private void ReleaseIdleOutput()
    {
        if (_tracks.Count > 0 || _output is null)
        {
            return;
        }

        var output = _output;
        _output = null;
        try
        {
            output.Dispose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log($"preview: the connection can't be closed ({Describe(exception)})");
        }
    }

    /// <summary>
    /// One open file and its stream. Its state is read on the preview's thread and written on both, under its own lock, which
    /// is never held while the stream is called: the stream's thread holds the server's lock when it calls back.
    /// </summary>
    private sealed class Track(IDecodedStream decoded, PulsePlaybackSilence silence, Action<string> log) : IPulsePlaybackFeed
    {
        private readonly Lock _gate = new();
        private readonly Lock _decoderGate = new();
        private bool _decoderDisposed;
        private bool _playing;
        private bool _ended;
        private bool _failed;
        private bool _closed;
        private PulsePlaybackSilence.Hold? _hold;

        public IPulsePlaybackStream? Stream { get; set; }

        public int Play()
        {
            lock (_gate)
            {
                if (_failed)
                {
                    return TrackFailed;
                }

                // Played to its end: there is nothing left to play, as with MCI, and no silence to hold.
                if (_ended || _playing)
                {
                    return 0;
                }

                _playing = true;

                // Before the stream starts: the capture is silent before the first sample reaches the output.
                _hold = silence.Silence(decoded.Duration + HoldSlack);
            }

            try
            {
                Stream!.Start();
                return 0;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failed("the stream can't start: " + exception.GetType().Name);
                return TrackFailed;
            }
        }

        public int Stop()
        {
            lock (_gate)
            {
                if (_failed)
                {
                    return TrackFailed;
                }

                if (!_playing)
                {
                    return 0;
                }
            }

            try
            {
                Stream!.Pause();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failed("the stream can't stop: " + exception.GetType().Name);
                return TrackFailed;
            }

            // After the stream has stopped, so the end of what it played is silenced too.
            lock (_gate)
            {
                _playing = false;
                Lift();
            }

            return 0;
        }

        public void Close()
        {
            try
            {
                // Once it returns, no callback comes: the file can go.
                Stream?.Close();
            }
            finally
            {
                lock (_gate)
                {
                    _closed = true;
                    _playing = false;
                    Lift();
                }

                // Never with the track's own lock held: Fill takes this one, then the track's.
                lock (_decoderGate)
                {
                    if (!_decoderDisposed)
                    {
                        _decoderDisposed = true;
                        decoded.Dispose();
                    }
                }
            }
        }

        public int StateOf(out PreviewTrackState state)
        {
            lock (_gate)
            {
                state = _ended || !_playing ? PreviewTrackState.Stopped : PreviewTrackState.Playing;
                return _failed ? TrackFailed : 0;
            }
        }

        public int Fill(Span<float> interleaved)
        {
            // A track still asking for samples is still playing, whatever length its file announced: the hold follows it. One
            // that lifted at its deadline, while the server stopped asking for a while, is taken again for what is left to play.
            lock (_gate)
            {
                if (_hold is { } hold && !hold.Extend(HoldSlack) && _playing)
                {
                    hold.Release();
                    _hold = silence.Silence(HoldSlack);
                }
            }

            // The file is read and let go of under one lock: it is never read after it was let go of, even if closing the stream failed.
            lock (_decoderGate)
            {
                if (_decoderDisposed)
                {
                    return 0;
                }

                try
                {
                    var written = 0;
                    while (written < interleaved.Length)
                    {
                        var read = decoded.Read(interleaved[written..]);
                        if (read <= 0)
                        {
                            break;
                        }

                        written += read;
                    }

                    return written;
                }
                catch (Exception exception)
                {
                    Failed("the file failed while playing: " + exception.GetType().Name);
                    return -1;
                }
            }
        }

        public void Drained(bool played)
        {
            if (!played)
            {
                Failed("the end of the file was not played");
                return;
            }

            lock (_gate)
            {
                // A drain that completes before the track plays, or after it was stopped, has played nothing: the stream drains again when it starts.
                if (!_playing)
                {
                    return;
                }

                // Played to its end: nothing left to stop, so a Stop after it does not touch the drained stream.
                _ended = true;
                _playing = false;
                Lift();
            }
        }

        public bool Seek(TimeSpan at)
        {
            // Under the lock Fill reads with: the next read starts at the new place, and nothing read before it is kept, since
            // Fill hands all it reads to the stream at once.
            lock (_decoderGate)
            {
                if (_decoderDisposed)
                {
                    return false;
                }

                try
                {
                    decoded.Seek(at);
                    return true;
                }
                catch (Exception exception)
                {
                    Note("preview: a stop goes on from what the stream held (the file can't seek: " + exception.GetType().Name + ")");
                    return false;
                }
            }
        }

        public void Note(string line)
        {
            try
            {
                log(line);
            }
            catch (Exception)
            {
                // Called on the server's thread: nothing may be thrown out of it.
            }
        }

        public void Failed(string reason)
        {
            bool first;
            lock (_gate)
            {
                first = !_failed && !_closed;
                _failed = true;
                Lift();
            }

            if (first)
            {
                try
                {
                    log("preview: a track failed (" + reason + ")");
                }
                catch (Exception)
                {
                    // Called on the server's thread: nothing may be thrown out of it.
                }
            }
        }

        // Under the lock.
        private void Lift()
        {
            _hold?.Release();
            _hold = null;
        }
    }
}
