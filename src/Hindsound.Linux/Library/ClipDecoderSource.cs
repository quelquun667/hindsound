namespace Hindsound.Linux.Library;

/// <summary>
/// A file of the library read back at its own rate and channel count, to the frame: the part of a decoder that knows a
/// format. <see cref="ClipDecoderStream"/> turns it into what the app reads (stereo or mono, 48 kHz).
/// </summary>
internal interface IClipDecoderSource : IDisposable
{
    /// <summary>Samples per frame of the file: 1, 2, or more (a wav may hold more than two channels).</summary>
    int Channels { get; }

    /// <summary>Frames per second of the file.</summary>
    int Rate { get; }

    /// <summary>The frames of the clip, as the file announces them.</summary>
    long Frames { get; }

    /// <summary>The next read starts at this frame, exactly. A frame at or past <see cref="Frames"/> leaves nothing to read.</summary>
    void Seek(long frame);

    /// <summary>Fills as much of <paramref name="interleaved"/> as it can, in whole frames. Returns the number of floats written; 0 at the end.</summary>
    int Read(Span<float> interleaved);
}
