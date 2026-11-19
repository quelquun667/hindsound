using Hindsound.Audio;
using Hindsound.Library;
using Hindsound.Linux.Audio;

namespace Hindsound.Linux.Tests.Audio;

/// <summary>
/// The preview's player, on a fake connection the test drives as the server would: when the capture of the PC's sound is
/// silent, and that the silence lifts whatever ends the playback.
/// </summary>
public sealed class PulsePreviewPlayerTests
{
    private const string Track = "hindsound_preview_1";
    private const string Other = "hindsound_preview_2";
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "Alice's clip.mp3");

    private readonly Clock _clock = new();
    private readonly FakeOutputs _outputs = new();
    private readonly List<string> _log = [];
    private readonly PulsePlaybackSilence _silence;
    private readonly ToneDecoder _decoder = new();

    public PulsePreviewPlayerTests()
    {
        _silence = new PulsePlaybackSilence(() => _clock.Ms);
        _outputs.Silence = _silence;
    }

    [Fact]
    public void An_open_track_leaves_the_capture_alone_and_a_playing_one_silences_it()
    {
        using var player = NewPlayer();

        Assert.Equal(0, player.Open(FilePath, Track));
        Assert.False(_silence.IsSilent);
        Assert.Equal(0, player.Play(Track));

        Assert.True(_silence.IsSilent);
        Assert.Equal(1, _outputs.Last.Stream(0).Starts);
        Assert.Equal(0, player.StateOf(Track, out var state));
        Assert.Equal(PreviewTrackState.Playing, state);
    }

    [Fact]
    public void The_end_of_the_file_reads_as_stopped_and_the_capture_hears_again_after_the_tail()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);

        Assert.Equal(ToneDecoder.Frames * 2, stream.FillAll());
        Assert.True(_silence.IsSilent);
        stream.Feed.Drained(played: true);

        Assert.Equal(0, player.StateOf(Track, out var state));
        Assert.Equal(PreviewTrackState.Stopped, state);
        Assert.True(_silence.IsSilent);
        _clock.Ms += PulsePlaybackSilence.TailMs;
        Assert.False(_silence.IsSilent);
    }

    [Fact]
    public void Stop_stops_the_stream_before_the_silence_lifts_and_close_lets_go_of_the_file_and_the_connection()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);

        Assert.Equal(0, player.Stop(Track));
        Assert.Equal(1, stream.Pauses);
        Assert.True(stream.SilentWhenPaused);
        _clock.Ms += PulsePlaybackSilence.TailMs;
        Assert.False(_silence.IsSilent);

        Assert.Equal(0, player.Close(Track));
        Assert.True(stream.Closed);
        Assert.True(_decoder.AllDisposed);
        Assert.True(_outputs.Last.Disposed);
    }

    [Fact]
    public void A_close_during_the_drain_lifts_the_silence_and_a_drain_that_answers_late_changes_nothing()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);
        stream.FillAll();

        Assert.Equal(0, player.Close(Track));
        stream.Feed.Drained(played: false);
        _clock.Ms += PulsePlaybackSilence.TailMs;

        Assert.False(_silence.IsSilent);
        Assert.Equal(PulsePreviewPlayer.UnknownTrack, player.StateOf(Track, out _));
        Assert.Empty(_log);
    }

    [Fact]
    public void A_file_that_fails_while_playing_fails_its_track_and_lifts_the_silence_and_the_log_has_no_path()
    {
        _decoder.FailAfterReads = 1;
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);

        Assert.True(stream.Feed.Fill(new float[960]) > 0);
        Assert.True(stream.Feed.Fill(new float[960]) < 0);
        _clock.Ms += PulsePlaybackSilence.TailMs;

        Assert.Equal(PulsePreviewPlayer.TrackFailed, player.StateOf(Track, out _));
        Assert.False(_silence.IsSilent);
        var line = Assert.Single(_log);
        Assert.Contains(nameof(ClipDecodeException), line);
        Assert.DoesNotContain("Alice", line);
    }

    [Fact]
    public void A_server_that_restarts_while_playing_fails_the_track_lifts_the_silence_and_the_next_track_connects_again()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        _outputs.Last.IsLost = true;

        // The loss is read when the preview next asks: the server's own callback on the stream may come later, or never.
        Assert.Equal(PulsePreviewPlayer.TrackFailed, player.StateOf(Track, out _));
        _clock.Ms += PulsePlaybackSilence.TailMs;
        Assert.False(_silence.IsSilent);
        Assert.Equal(0, player.Close(Track));
        Assert.True(_outputs.Last.Disposed);

        Assert.Equal(0, player.Open(FilePath, Track));
        Assert.Equal(2, _outputs.Made);
    }

    [Fact]
    public void A_stream_the_server_ends_fails_its_track_and_lifts_the_silence()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);

        _outputs.Last.Stream(0).Feed.Failed("the sound server ended the stream");
        _clock.Ms += PulsePlaybackSilence.TailMs;

        Assert.Equal(PulsePreviewPlayer.TrackFailed, player.StateOf(Track, out _));
        Assert.False(_silence.IsSilent);
    }

    [Fact]
    public void With_two_tracks_the_capture_stays_silent_until_both_have_ended_on_one_connection()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Open(FilePath, Other);
        player.Play(Track);
        player.Play(Other);
        var output = _outputs.Last;

        output.Stream(0).FillAll();
        output.Stream(0).Feed.Drained(played: true);
        _clock.Ms += PulsePlaybackSilence.TailMs;
        Assert.True(_silence.IsSilent);

        output.Stream(1).FillAll();
        output.Stream(1).Feed.Drained(played: true);
        _clock.Ms += PulsePlaybackSilence.TailMs;
        Assert.False(_silence.IsSilent);
        Assert.Equal(1, _outputs.Made);
    }

    [Fact]
    public void Play_after_the_end_plays_nothing_and_does_not_silence_the_capture()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);
        stream.FillAll();
        stream.Feed.Drained(played: true);
        _clock.Ms += PulsePlaybackSilence.TailMs;

        Assert.Equal(0, player.Play(Track));

        Assert.False(_silence.IsSilent);
        Assert.Equal(1, stream.Starts);
        Assert.Equal(0, player.StateOf(Track, out var state));
        Assert.Equal(PreviewTrackState.Stopped, state);
    }

    [Fact]
    public void Dispose_while_playing_closes_every_track_and_the_connection_and_lifts_the_silence()
    {
        var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Open(FilePath, Other);
        player.Play(Track);
        player.Play(Other);

        player.Dispose();
        _clock.Ms += PulsePlaybackSilence.TailMs;

        Assert.False(_silence.IsSilent);
        Assert.True(_outputs.Last.Stream(0).Closed);
        Assert.True(_outputs.Last.Stream(1).Closed);
        Assert.True(_outputs.Last.Disposed);
        Assert.True(_decoder.AllDisposed);
        Assert.Equal(PulsePreviewPlayer.PlayerClosed, player.Open(FilePath, Track));
    }

    [Fact]
    public void A_file_that_cannot_be_read_opens_nothing_and_a_server_that_refuses_lets_go_of_the_file()
    {
        using var player = NewPlayer();
        _decoder.Refuse = true;
        Assert.Equal(PulsePreviewPlayer.Unreadable, player.Open(FilePath, Track));
        Assert.Equal(0, _outputs.Made);

        _decoder.Refuse = false;
        _outputs.Refuse = true;
        Assert.Equal(PulsePreviewPlayer.NoServer, player.Open(FilePath, Track));
        Assert.True(_decoder.AllDisposed);
        Assert.Equal(PulsePreviewPlayer.UnknownTrack, player.Play(Track));
        Assert.All(_log, line => Assert.DoesNotContain("Alice", line));
    }

    [Fact]
    public void A_track_never_closed_stops_silencing_the_capture_after_its_length_and_the_slack()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);

        _clock.Ms += (long)(ToneDecoder.Length + PulsePreviewPlayer.HoldSlack).TotalMilliseconds - 1;
        Assert.True(_silence.IsSilent);
        _clock.Ms += 1;
        Assert.False(_silence.IsSilent);
    }

    [Fact]
    public void A_file_longer_than_it_announced_keeps_the_capture_silent_while_it_is_fed_and_lets_go_after_it_stops()
    {
        // One second announced, twenty in the file: the stream asks for more every 100 ms as long as it plays.
        _decoder.Announced = TimeSpan.FromSeconds(1);
        _decoder.FramesInFile = 20 * InternalFormat.SampleRate;
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);

        var buffer = new float[960];
        for (var elapsed = 0; elapsed < 10_000; elapsed += 100)
        {
            _clock.Ms += 100;
            Assert.True(stream.Feed.Fill(buffer) > 0);
            Assert.True(_silence.IsSilent, $"the capture heard again {elapsed} ms in, with the file still playing");
        }

        _clock.Ms += (long)PulsePreviewPlayer.HoldSlack.TotalMilliseconds - 1;
        Assert.True(_silence.IsSilent);
        _clock.Ms += 1;
        Assert.False(_silence.IsSilent);
    }

    [Fact]
    public void A_track_fed_again_after_its_silence_lifted_while_the_server_stopped_asking_silences_the_capture_again()
    {
        // One second announced, twenty in the file: the server stops asking for longer than the hold lasts, then asks again.
        _decoder.Announced = TimeSpan.FromSeconds(1);
        _decoder.FramesInFile = 20 * InternalFormat.SampleRate;
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);

        _clock.Ms += (long)(TimeSpan.FromSeconds(1) + PulsePreviewPlayer.HoldSlack).TotalMilliseconds + 1_000;
        Assert.False(_silence.IsSilent);

        var buffer = new float[960];
        Assert.True(stream.Feed.Fill(buffer) > 0);
        Assert.True(_silence.IsSilent, "the rest of the file plays into the capture");

        _clock.Ms += (long)PulsePreviewPlayer.HoldSlack.TotalMilliseconds - 1;
        Assert.True(_silence.IsSilent);
        _clock.Ms += 1;
        Assert.False(_silence.IsSilent);

        // Stopped, it is not held again by a last request of the server.
        Assert.Equal(0, player.Stop(Track));
        _clock.Ms += PulsePlaybackSilence.TailMs;
        Assert.True(stream.Feed.Fill(buffer) > 0);
        Assert.False(_silence.IsSilent);
    }

    [Fact]
    public void A_stream_that_fails_to_close_does_not_leave_its_file_to_be_read_after_it_was_let_go_of()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);
        stream.CloseFails = true;

        Assert.Throws<IOException>(() => player.Close(Track));
        Assert.True(_decoder.AllDisposed);

        // The server's thread, not knowing the stream is gone, asks again.
        Assert.Equal(0, Task.Run(() => stream.Feed.Fill(new float[960])).GetAwaiter().GetResult());
        Assert.False(_decoder.ReadAfterDispose);
        _clock.Ms += PulsePlaybackSilence.TailMs;
        Assert.False(_silence.IsSilent);
    }

    [Fact]
    public void Stop_after_the_end_of_the_file_leaves_the_drained_stream_alone()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);
        stream.FillAll();
        stream.Feed.Drained(played: true);

        Assert.Equal(0, player.Stop(Track));

        Assert.Equal(0, stream.Pauses);
        Assert.Equal(0, player.StateOf(Track, out var state));
        Assert.Equal(PreviewTrackState.Stopped, state);
    }

    [Fact]
    public void A_drain_that_answers_before_the_track_plays_changes_nothing_and_play_still_starts_the_stream()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        var stream = _outputs.Last.Stream(0);
        stream.Feed.Drained(played: true);

        Assert.Equal(0, player.Play(Track));

        Assert.Equal(1, stream.Starts);
        Assert.True(_silence.IsSilent);
        Assert.Equal(0, player.StateOf(Track, out var state));
        Assert.Equal(PreviewTrackState.Playing, state);

        stream.FillAll();
        stream.Feed.Drained(played: true);
        Assert.Equal(0, player.StateOf(Track, out state));
        Assert.Equal(PreviewTrackState.Stopped, state);
    }

    [Fact]
    public void Stop_then_play_starts_the_stream_again_with_one_hold_at_a_time()
    {
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);

        Assert.Equal(0, player.Stop(Track));
        _clock.Ms += PulsePlaybackSilence.TailMs;
        Assert.False(_silence.IsSilent);

        Assert.Equal(0, player.Play(Track));
        Assert.Equal(2, stream.Starts);
        Assert.True(_silence.IsSilent);

        Assert.Equal(0, player.Stop(Track));
        _clock.Ms += PulsePlaybackSilence.TailMs;
        Assert.False(_silence.IsSilent);
    }

    [Fact]
    public void A_whole_preview_of_two_tracks_ends_with_nothing_open_and_the_capture_hearing_again()
    {
        _outputs.PlayWholeFileOnStart = true;
        using var player = NewPlayer();
        var changes = new List<PreviewState>();
        // The preview's own clock runs: a track that ends at once is told after the grace of its start.
        using var preview = new ClipPreview(player, change => { lock (changes) { changes.Add(change.State); } });
        var clip = new ClipGroup([new ClipFile(FilePath, 3, DateTime.UnixEpoch), new ClipFile(FilePath + ".mic.mp3", 3, DateTime.UnixEpoch)]);

        preview.Play(clip);
        Assert.True(preview.WaitForEnd(10_000));
        _clock.Ms += PulsePlaybackSilence.TailMs;

        lock (changes)
        {
            Assert.Equal([PreviewState.Opening, PreviewState.Playing, PreviewState.Ended], changes);
        }

        Assert.False(_silence.IsSilent);
        Assert.True(_outputs.Last.Disposed);
        Assert.Equal(2, _decoder.Opened);
        Assert.True(_decoder.AllDisposed);
    }

    [Fact]
    public void A_stopped_track_sent_back_to_a_frame_reads_its_file_from_that_frame_and_not_once_closed()
    {
        _decoder.FramesInFile = InternalFormat.SampleRate / 10;
        using var player = NewPlayer();
        player.Open(FilePath, Track);
        player.Play(Track);
        var stream = _outputs.Last.Stream(0);
        var buffer = new float[2 * InternalFormat.SampleRate];
        Assert.Equal(2 * _decoder.FramesInFile, stream.Feed.Fill(buffer));
        Assert.Equal(0, player.Stop(Track));

        // The stream sends the file back to the last frame heard: the rest of it is read again, from there.
        Assert.True(stream.Feed.Seek(PulseFormats.TimeOfFrame(_decoder.FramesInFile - 1001, InternalFormat.SampleRate)));
        Assert.Equal(2 * 1001, stream.Feed.Fill(buffer));

        stream.Feed.Note("a line of the stream");
        Assert.Contains("a line of the stream", _log);

        Assert.Equal(0, player.Close(Track));
        Assert.False(stream.Feed.Seek(TimeSpan.Zero), "a closed file is not read again");
    }

    private PulsePreviewPlayer NewPlayer() => new(_decoder, _silence, _outputs.Connect, line => { lock (_log) { _log.Add(line); } });

    private sealed class Clock
    {
        private long _ms = 1_000;

        public long Ms
        {
            get => Interlocked.Read(ref _ms);
            set => Interlocked.Exchange(ref _ms, value);
        }
    }

    /// <summary>A tone of a known length; it can refuse to open, or fail after some reads.</summary>
    private sealed class ToneDecoder : IClipDecoder
    {
        public const int Frames = 4800;
        public static readonly TimeSpan Length = TimeSpan.FromSeconds((double)Frames / InternalFormat.SampleRate);
        private readonly List<Stream> _streams = [];

        public bool Refuse { get; set; }

        public int FailAfterReads { get; set; } = -1;

        /// <summary>How many frames the file really has, whatever length it announces.</summary>
        public int FramesInFile { get; set; } = Frames;

        /// <summary>The length the file announces.</summary>
        public TimeSpan Announced { get; set; } = Length;

        public int Opened
        {
            get
            {
                lock (_streams)
                {
                    return _streams.Count;
                }
            }
        }

        /// <summary>Whether a file was read after it was let go of.</summary>
        public bool ReadAfterDispose
        {
            get
            {
                lock (_streams)
                {
                    return _streams.Exists(stream => stream.ReadAfterDispose);
                }
            }
        }

        public bool AllDisposed
        {
            get
            {
                lock (_streams)
                {
                    return _streams.TrueForAll(stream => stream.Disposed);
                }
            }
        }

        public IDecodedStream Open(string path)
        {
            if (Refuse)
            {
                throw new ClipDecodeException("can't read " + path);
            }

            var stream = new Stream(FailAfterReads, FramesInFile, Announced);
            lock (_streams)
            {
                _streams.Add(stream);
            }

            return stream;
        }

        private sealed class Stream(int failAfterReads, int framesInFile, TimeSpan announced) : IDecodedStream
        {
            private int _frame;
            private int _reads;

            public bool Disposed { get; private set; }

            public bool ReadAfterDispose { get; private set; }

            public int Channels => 2;

            public TimeSpan Duration => announced;

            public void Seek(TimeSpan at) => _frame = (int)(at.TotalSeconds * InternalFormat.SampleRate);

            public int Read(Span<float> interleaved)
            {
                ReadAfterDispose |= Disposed;

                if (failAfterReads >= 0 && _reads++ >= failAfterReads)
                {
                    throw new ClipDecodeException("damaged " + FilePath);
                }

                var frames = Math.Min(interleaved.Length / 2, framesInFile - _frame);
                for (var i = 0; i < frames; i++, _frame++)
                {
                    interleaved[2 * i] = interleaved[(2 * i) + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * _frame / InternalFormat.SampleRate));
                }

                return frames * 2;
            }

            public void Dispose() => Disposed = true;
        }
    }

    private sealed class FakeOutputs
    {
        public int Made { get; private set; }

        public FakeOutput Last { get; private set; } = null!;

        public bool Refuse { get; set; }

        public bool PlayWholeFileOnStart { get; set; }

        public PulsePlaybackSilence? Silence { get; set; }

        public IPulsePlaybackOutput Connect()
        {
            Made++;
            Last = new FakeOutput(this);
            return Last;
        }
    }

    private sealed class FakeOutput(FakeOutputs owner) : IPulsePlaybackOutput
    {
        private readonly List<FakeStream> _streams = [];

        public bool IsLost { get; set; }

        public bool Disposed { get; private set; }

        public FakeStream Stream(int index) => _streams[index];

        public IPulsePlaybackStream Open(int channels, IPulsePlaybackFeed feed)
        {
            if (owner.Refuse)
            {
                throw new IOException("The sound server refused a stream: Connection refused");
            }

            var stream = new FakeStream(feed, owner);
            _streams.Add(stream);
            return stream;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeStream(IPulsePlaybackFeed feed, FakeOutputs owner) : IPulsePlaybackStream
    {
        public IPulsePlaybackFeed Feed { get; } = feed;

        public int Starts { get; private set; }

        public int Pauses { get; private set; }

        public bool Closed { get; private set; }

        /// <summary>Whether the capture was still silent when the stream was told to stop: the end of what it played is silenced too.</summary>
        public bool SilentWhenPaused { get; private set; }

        /// <summary>Asks the feed for samples until the file ends, as the server would; the number of floats it gave.</summary>
        public int FillAll()
        {
            var total = 0;
            var buffer = new float[960];
            int filled;
            while ((filled = Feed.Fill(buffer)) == buffer.Length)
            {
                total += filled;
            }

            return total + Math.Max(filled, 0);
        }

        public void Start()
        {
            Starts++;
            if (owner.PlayWholeFileOnStart)
            {
                FillAll();
                Feed.Drained(played: true);
            }
        }

        public void Pause()
        {
            Pauses++;
            SilentWhenPaused = owner.Silence!.IsSilent;
        }

        /// <summary>Whether closing throws, as a connection already torn down does.</summary>
        public bool CloseFails { get; set; }

        public void Close()
        {
            Closed = true;
            if (CloseFails)
            {
                throw new IOException("The connection to the sound server ended.");
            }
        }
    }
}
