namespace Hindsound.Linux.Audio;

/// <summary>
/// A connection the preview plays on: the sound server itself (<see cref="PulsePlaybackOutput"/>), or a fake in a test.
/// Called on the preview's thread.
/// </summary>
internal interface IPulsePlaybackOutput : IDisposable
{
    /// <summary>Whether the connection has ended by itself: the server stopped or went away. A lost one is not used again.</summary>
    bool IsLost { get; }

    /// <summary>
    /// Opens a playback stream on the default output, corked: it fills its buffer from <paramref name="feed"/> but plays
    /// nothing until <see cref="IPulsePlaybackStream.Start"/>. Throws if the server refuses it.
    /// </summary>
    /// <param name="channels">1 or 2, of 32-bit floats at the engine's rate.</param>
    /// <param name="feed">Called on the connection's own thread, never after <see cref="IPulsePlaybackStream.Close"/> has returned.</param>
    IPulsePlaybackStream Open(int channels, IPulsePlaybackFeed feed);
}

/// <summary>One playback stream. Called on the preview's thread.</summary>
internal interface IPulsePlaybackStream
{
    /// <summary>Uncorks the stream: it plays.</summary>
    void Start();

    /// <summary>
    /// Corks the stream: the sound stops at once, and the next <see cref="Start"/> goes on from what was heard, whether the
    /// output plays out or takes back what it held.
    /// </summary>
    void Pause();

    /// <summary>Ends the stream and what it plays. Once it has returned, the feed is called no more. Twice is once.</summary>
    void Close();
}

/// <summary>What a stream asks of its track, on the connection's own thread. Nothing may be thrown out of these.</summary>
internal interface IPulsePlaybackFeed
{
    /// <summary>
    /// Fills <paramref name="interleaved"/> with as many whole frames as the file has left. Returns the number of floats
    /// written; 0 at the end of the file, after which the stream drains; less than 0 if the file failed, after which the
    /// stream stops.
    /// </summary>
    int Fill(Span<float> interleaved);

    /// <summary>The stream has played everything it was given, or could not (<paramref name="played"/> false).</summary>
    void Drained(bool played);

    /// <summary>The stream ended by itself: the server stopped, or refused it.</summary>
    void Failed(string reason);

    /// <summary>
    /// The next <see cref="Fill"/> reads from <paramref name="at"/> in the file: a stopped stream goes on from what was heard.
    /// False if the file can't seek, or is closed; the stream then keeps what it holds.
    /// </summary>
    bool Seek(TimeSpan at);

    /// <summary>A line for the log of the run: what the stream did instead, never a path.</summary>
    void Note(string line);
}
