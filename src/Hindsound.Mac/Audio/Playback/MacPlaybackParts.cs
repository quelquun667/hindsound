namespace Hindsound.Mac.Audio.Playback;

/// <summary>
/// Where the preview plays: an AudioQueue on the default output (<see cref="AudioQueueOutput"/>), or a fake in a test.
/// Called on the preview's thread.
/// </summary>
internal interface IMacPlaybackOutput
{
    /// <summary>
    /// Opens a stream on the default output, stopped: it plays nothing until <see cref="IMacPlaybackStream.Start"/>. Throws if
    /// the output refuses it.
    /// </summary>
    /// <param name="channels">1 or 2, of 32-bit floats at the engine's rate.</param>
    /// <param name="feed">Called on the output's own thread, never after <see cref="IMacPlaybackStream.Close"/> has returned.</param>
    IMacPlaybackStream Open(int channels, IMacPlaybackFeed feed);
}

/// <summary>One stream of the output. Called on the preview's thread.</summary>
internal interface IMacPlaybackStream
{
    /// <summary>Plays, from where it was paused. A stream that has already played to its end says it drained again.</summary>
    void Start();

    /// <summary>The sound stops at once; the next <see cref="Start"/> goes on from what was heard.</summary>
    void Pause();

    /// <summary>Ends the stream and what it plays. Once it has returned, the feed is called no more. Twice is once.</summary>
    void Close();
}

/// <summary>What a stream asks of its track, on the output's own thread (or the preview's, while it starts). Nothing may be thrown out of these.</summary>
internal interface IMacPlaybackFeed
{
    /// <summary>
    /// Fills <paramref name="interleaved"/> with as many whole frames as the file has left. Returns the number of floats
    /// written; 0 at the end of the file, after which the stream plays out what it holds and drains; less than 0 if the file
    /// failed, after which the stream stops.
    /// </summary>
    int Fill(Span<float> interleaved);

    /// <summary>The stream has played everything it was given, or could not (<paramref name="played"/> false).</summary>
    void Drained(bool played);

    /// <summary>The stream ended by itself: the output stopped, or refused it.</summary>
    void Failed(string reason);
}
