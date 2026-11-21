using Hindsound.Library;

namespace Hindsound.Mac.Audio.Playback;

/// <summary>
/// The preview of a clip on macOS: each track is a stream of the output (an AudioQueue on the default output in the app),
/// fed with the samples an <see cref="IClipDecoder"/> reads. The logic of the Linux player, without its hold of silence:
/// the capture of the Mac's sound is a tap that leaves the app's own process out, so it never hears the clip being listened to.
/// <para>
/// Each track has its own stream, made when it opens and ended when it closes: with nothing open there is no stream and no
/// thread. The verbs are called on the preview's thread; the streams call their tracks back on the output's thread. The log
/// gets the type of an error, never a path or a message: a file name can hold a first name.
/// </para>
/// </summary>
internal sealed class MacPreviewPlayer : IPreviewPlayer, IDisposable
{
    // The player's own error codes: any code but zero is an error to the preview.
    public const int UnknownTrack = 1;
    public const int Unreadable = 2;
    public const int NoOutput = 3;
    public const int TrackFailed = 4;
    public const int TrackInUse = 5;
    public const int PlayerClosed = 6;

    private readonly IClipDecoder _decoder;
    private readonly IMacPlaybackOutput _output;
    private readonly Action<string> _log;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <param name="decoder">Reads the files: <c>UnixClipDecoder</c> in the app.</param>
    /// <param name="output">Opens the streams: <see cref="AudioQueueOutput"/> in the app, a fake in a test.</param>
    /// <param name="log">The log of the run.</param>
    public MacPreviewPlayer(IClipDecoder decoder, IMacPlaybackOutput output, Action<string>? log = null)
    {
        _decoder = decoder;
        _output = output;
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

            var item = new Track(decoded, _log);
            try
            {
                item.Stream = _output.Open(decoded.Channels, item);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                decoded.Dispose();
                _log($"preview: the output refused a track ({Describe(exception)})");
                return NoOutput;
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

            item.Close();
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

            return item.StateOf(out state);
        }
    }

    // The streams call back on their own thread: this thread has nothing to answer meanwhile.
    public void Wait(WaitHandle wake, int milliseconds) => wake.WaitOne(milliseconds);

    /// <summary>Closes every track: the sound stops.</summary>
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
        }
    }

    private static string Describe(Exception exception) => ClipFolders.Describe(exception);

    /// <summary>
    /// One open file and its stream. Its state is read on the preview's thread and written on both, under its own lock, which
    /// is never held while the stream is called.
    /// </summary>
    private sealed class Track(IDecodedStream decoded, Action<string> log) : IMacPlaybackFeed
    {
        private readonly Lock _gate = new();
        private readonly Lock _decoderGate = new();
        private bool _decoderDisposed;
        private bool _playing;
        private bool _ended;
        private bool _failed;
        private bool _closed;

        public IMacPlaybackStream? Stream { get; set; }

        public int Play()
        {
            lock (_gate)
            {
                if (_failed)
                {
                    return TrackFailed;
                }

                // Played to its end: there is nothing left to play, as with MCI.
                if (_ended || _playing)
                {
                    return 0;
                }

                _playing = true;
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

            lock (_gate)
            {
                _playing = false;
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
                }

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
                // A drain that comes while the track is stopped has played nothing more: the stream says it again when it starts.
                if (!_playing)
                {
                    return;
                }

                // Played to its end: nothing left to stop, so a Stop after it does not touch the drained stream.
                _ended = true;
                _playing = false;
            }
        }

        public void Failed(string reason)
        {
            bool first;
            lock (_gate)
            {
                first = !_failed && !_closed;
                _failed = true;
            }

            if (first)
            {
                try
                {
                    log("preview: a track failed (" + reason + ")");
                }
                catch (Exception)
                {
                    // Called on the output's thread: nothing may be thrown out of it.
                }
            }
        }
    }
}
