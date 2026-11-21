using Hindsound.Library;
using Hindsound.Mac.Audio.Playback;

namespace Hindsound.Mac.Tests.Audio.Playback;

/// <summary>The logic of the Mac player on a fake output: no sound, so it runs everywhere.</summary>
public sealed class MacPreviewPlayerTests
{
    [Fact]
    public void Play_starts_the_stream_and_the_track_reads_playing()
    {
        var output = new FakeOutput();
        using var player = new MacPreviewPlayer(new FakeDecoder(), output);

        Assert.Equal(0, player.Open("clip.mp3", "a"));
        Assert.Equal(0, player.StateOf("a", out var before));
        Assert.Equal(PreviewTrackState.Stopped, before);

        Assert.Equal(0, player.Play("a"));
        Assert.Equal(1, output.Last.Starts);
        Assert.Equal(0, player.StateOf("a", out var state));
        Assert.Equal(PreviewTrackState.Playing, state);
        Assert.Equal(2, output.Last.Channels);
    }

    [Fact]
    public void Stop_pauses_the_stream_and_play_goes_on_with_the_same_stream()
    {
        var output = new FakeOutput();
        using var player = new MacPreviewPlayer(new FakeDecoder(), output);
        player.Open("clip.mp3", "a");
        player.Play("a");

        Assert.Equal(0, player.Stop("a"));
        Assert.Equal(1, output.Last.Pauses);
        player.StateOf("a", out var stopped);
        Assert.Equal(PreviewTrackState.Stopped, stopped);

        Assert.Equal(0, player.Play("a"));
        Assert.Equal(2, output.Last.Starts);
        Assert.Single(output.Streams);
        player.StateOf("a", out var playing);
        Assert.Equal(PreviewTrackState.Playing, playing);
    }

    [Fact]
    public void A_stop_of_a_track_that_does_not_play_leaves_the_stream_alone()
    {
        var output = new FakeOutput();
        using var player = new MacPreviewPlayer(new FakeDecoder(), output);
        player.Open("clip.mp3", "a");

        Assert.Equal(0, player.Stop("a"));
        Assert.Equal(0, output.Last.Pauses);
    }

    [Fact]
    public void Played_to_its_end_the_track_reads_stopped_and_neither_stop_nor_play_touch_the_stream()
    {
        var output = new FakeOutput();
        using var player = new MacPreviewPlayer(new FakeDecoder(), output);
        player.Open("clip.mp3", "a");
        player.Play("a");

        output.Last.Feed.Drained(true);

        Assert.Equal(0, player.StateOf("a", out var state));
        Assert.Equal(PreviewTrackState.Stopped, state);
        Assert.Equal(0, player.Stop("a"));
        Assert.Equal(0, player.Play("a"));
        Assert.Equal(0, output.Last.Pauses);
        Assert.Equal(1, output.Last.Starts);
    }

    [Fact]
    public void A_drain_while_the_track_is_stopped_is_not_its_end()
    {
        var output = new FakeOutput();
        using var player = new MacPreviewPlayer(new FakeDecoder(), output);
        player.Open("clip.mp3", "a");
        player.Play("a");
        player.Stop("a");

        output.Last.Feed.Drained(true);

        Assert.Equal(0, player.Play("a"));
        Assert.Equal(2, output.Last.Starts);
        player.StateOf("a", out var state);
        Assert.Equal(PreviewTrackState.Playing, state);
    }

    [Fact]
    public void A_drain_that_did_not_play_fails_the_track()
    {
        var output = new FakeOutput();
        var log = new List<string>();
        using var player = new MacPreviewPlayer(new FakeDecoder(), output, log.Add);
        player.Open("clip.mp3", "a");
        player.Play("a");

        output.Last.Feed.Drained(false);

        Assert.Equal(MacPreviewPlayer.TrackFailed, player.StateOf("a", out _));
        Assert.Equal(MacPreviewPlayer.TrackFailed, player.Play("a"));
        Assert.Equal(MacPreviewPlayer.TrackFailed, player.Stop("a"));
        Assert.Single(log, line => line.StartsWith("preview: a track failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Fill_hands_the_whole_file_then_zero()
    {
        var output = new FakeOutput();
        using var player = new MacPreviewPlayer(new FakeDecoder(floats: 10, chunk: 3), output);
        player.Open("clip.mp3", "a");

        var buffer = new float[8];
        Assert.Equal(8, output.Last.Feed.Fill(buffer));
        Assert.Equal([0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f], buffer);
        Assert.Equal(2, output.Last.Feed.Fill(buffer));
        Assert.Equal(0, output.Last.Feed.Fill(buffer));
    }

    [Fact]
    public void A_file_that_fails_while_playing_answers_minus_one_and_fails_the_track_without_its_message()
    {
        var output = new FakeOutput();
        var log = new List<string>();
        using var player = new MacPreviewPlayer(new FakeDecoder(failRead: true), output, log.Add);
        player.Open("clip.mp3", "a");
        player.Play("a");

        Assert.Equal(-1, output.Last.Feed.Fill(new float[4]));
        Assert.Equal(MacPreviewPlayer.TrackFailed, player.StateOf("a", out _));
        Assert.Contains(log, line => line.Contains("IOException", StringComparison.Ordinal));
        Assert.DoesNotContain(log, line => line.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stream_that_cannot_start_fails_the_track()
    {
        var output = new FakeOutput { StartThrows = true };
        using var player = new MacPreviewPlayer(new FakeDecoder(), output);
        player.Open("clip.mp3", "a");

        Assert.Equal(MacPreviewPlayer.TrackFailed, player.Play("a"));
        Assert.Equal(MacPreviewPlayer.TrackFailed, player.StateOf("a", out _));
    }

    [Fact]
    public void An_unreadable_file_or_a_refused_stream_opens_no_track_and_lets_the_file_go()
    {
        var decoder = new FakeDecoder();
        using var unreadable = new MacPreviewPlayer(new FakeDecoder(failOpen: true), new FakeOutput());
        Assert.Equal(MacPreviewPlayer.Unreadable, unreadable.Open("clip.mp3", "a"));
        Assert.Equal(MacPreviewPlayer.UnknownTrack, unreadable.Play("a"));

        using var refused = new MacPreviewPlayer(decoder, new FakeOutput { OpenThrows = true });
        Assert.Equal(MacPreviewPlayer.NoOutput, refused.Open("clip.mp3", "a"));
        Assert.True(decoder.Opened.Single().Disposed);
        Assert.Equal(MacPreviewPlayer.UnknownTrack, refused.StateOf("a", out var state));
        Assert.Equal(PreviewTrackState.Other, state);
    }

    [Fact]
    public void A_name_already_open_is_refused_and_an_unknown_one_answers_unknown()
    {
        using var player = new MacPreviewPlayer(new FakeDecoder(), new FakeOutput());
        Assert.Equal(0, player.Open("clip.mp3", "a"));
        Assert.Equal(MacPreviewPlayer.TrackInUse, player.Open("other.mp3", "a"));
        Assert.Equal(MacPreviewPlayer.UnknownTrack, player.Play("b"));
        Assert.Equal(MacPreviewPlayer.UnknownTrack, player.Stop("b"));
        Assert.Equal(MacPreviewPlayer.UnknownTrack, player.Close("b"));
    }

    [Fact]
    public void Close_ends_the_stream_lets_the_file_go_and_no_sample_is_read_after_it()
    {
        var output = new FakeOutput();
        var decoder = new FakeDecoder();
        using var player = new MacPreviewPlayer(decoder, output);
        player.Open("clip.mp3", "a");
        player.Play("a");

        Assert.Equal(0, player.Close("a"));

        Assert.Equal(1, output.Last.Closes);
        Assert.True(decoder.Opened.Single().Disposed);
        Assert.Equal(0, output.Last.Feed.Fill(new float[4]));
        Assert.Equal(MacPreviewPlayer.UnknownTrack, player.StateOf("a", out _));
        Assert.Equal(0, player.Open("clip.mp3", "a"));
    }

    [Fact]
    public void Dispose_closes_every_track_and_the_player_opens_no_more()
    {
        var output = new FakeOutput();
        var decoder = new FakeDecoder();
        var player = new MacPreviewPlayer(decoder, output);
        player.Open("one.mp3", "a");
        player.Open("two.mp3", "b");

        player.Dispose();

        Assert.All(output.Streams, stream => Assert.Equal(1, stream.Closes));
        Assert.All(decoder.Opened, file => Assert.True(file.Disposed));
        Assert.Equal(MacPreviewPlayer.PlayerClosed, player.Open("one.mp3", "c"));
    }

    [Fact]
    public void Wait_returns_when_woken()
    {
        using var player = new MacPreviewPlayer(new FakeDecoder(), new FakeOutput());
        using var wake = new AutoResetEvent(true);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        player.Wait(wake, 10_000);
        Assert.True(clock.ElapsedMilliseconds < 5_000);
    }

    private sealed class FakeDecoder(int floats = 96, int chunk = 1000, bool failOpen = false, bool failRead = false) : IClipDecoder
    {
        public List<FakeDecoded> Opened { get; } = [];

        public IDecodedStream Open(string path)
        {
            if (failOpen)
            {
                throw new ClipDecodeException("secret name " + path);
            }

            var decoded = new FakeDecoded(floats, chunk, failRead);
            Opened.Add(decoded);
            return decoded;
        }
    }

    private sealed class FakeDecoded(int floats, int chunk, bool failRead) : IDecodedStream
    {
        private int _next;

        public bool Disposed { get; private set; }

        public int Channels => 2;

        public TimeSpan Duration => TimeSpan.FromSeconds(1);

        public void Seek(TimeSpan at) => _next = 0;

        public int Read(Span<float> interleaved)
        {
            if (failRead)
            {
                throw new IOException("secret");
            }

            var count = Math.Min(Math.Min(chunk, interleaved.Length), floats - _next);
            for (var i = 0; i < count; i++)
            {
                interleaved[i] = _next++;
            }

            return count;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeOutput : IMacPlaybackOutput
    {
        public bool OpenThrows { get; init; }

        public bool StartThrows { get; init; }

        public List<FakeStream> Streams { get; } = [];

        public FakeStream Last => Streams[^1];

        public IMacPlaybackStream Open(int channels, IMacPlaybackFeed feed)
        {
            if (OpenThrows)
            {
                throw new IOException("refused");
            }

            var stream = new FakeStream(channels, feed, StartThrows);
            Streams.Add(stream);
            return stream;
        }
    }

    private sealed class FakeStream(int channels, IMacPlaybackFeed feed, bool startThrows) : IMacPlaybackStream
    {
        public int Channels => channels;

        public IMacPlaybackFeed Feed => feed;

        public int Starts { get; private set; }

        public int Pauses { get; private set; }

        public int Closes { get; private set; }

        public void Start()
        {
            Starts++;
            if (startThrows)
            {
                throw new IOException("no output");
            }
        }

        public void Pause() => Pauses++;

        public void Close() => Closes++;
    }
}
