using System.Runtime.InteropServices;

namespace Hindsound.Linux.Export;

/// <summary>
/// A write-only stream that takes 16-bit little-endian PCM (interleaved if stereo) and writes constant-bitrate MP3 to
/// <c>output</c>. Disposing finishes the file: what LAME still holds is flushed, then the Info/LAME frame it keeps for the
/// start of the file is written over the placeholder frame that opens the stream (the output must then be seekable, as the
/// file is). The output stream stays open; its owner closes it.
/// </summary>
internal sealed class LameMp3Stream : Stream
{
    /// <summary>Frames handed to LAME at a time (about 0.17 s at 48 kHz), as in the WAV and MP3 writers of the portable code.</summary>
    private const int ChunkFrames = 8192;

    private readonly LameLibrary _lame;
    private readonly Stream _output;
    private readonly int _channels;
    private readonly nint _encoder;
    private readonly short[] _samples;
    private readonly byte[] _mp3;
    private readonly byte[] _partial;
    private int _partialBytes;
    private bool _disposed;

    public LameMp3Stream(LameLibrary lame, Stream output, int sampleRate, int channels, int bitrateKbps)
    {
        if (channels is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Only mono and stereo are supported.");

        _lame = lame;
        _output = output;
        _channels = channels;
        _encoder = lame.OpenEncoder(sampleRate, channels, bitrateKbps);
        _samples = new short[ChunkFrames * channels];
        _mp3 = new byte[LameLibrary.Mp3BufferSize(ChunkFrames)];
        _partial = new byte[2 * channels];
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var frameBytes = 2 * _channels;
        var chunkBytes = ChunkFrames * frameBytes;

        // A write may end in the middle of a frame; the rest waits here for the next one.
        if (_partialBytes > 0)
        {
            var take = Math.Min(frameBytes - _partialBytes, bytes.Length);
            bytes[..take].CopyTo(_partial.AsSpan(_partialBytes));
            _partialBytes += take;
            bytes = bytes[take..];
            if (_partialBytes < frameBytes)
                return;

            _partial.CopyTo(MemoryMarshal.AsBytes(_samples.AsSpan()));
            EncodeSamples(_channels);
            _partialBytes = 0;
        }

        while (bytes.Length >= frameBytes)
        {
            var count = Math.Min(chunkBytes, bytes.Length / frameBytes * frameBytes);
            bytes[..count].CopyTo(MemoryMarshal.AsBytes(_samples.AsSpan()));
            EncodeSamples(count / 2);
            bytes = bytes[count..];
        }

        bytes.CopyTo(_partial);
        _partialBytes = bytes.Length;
    }

    private void EncodeSamples(int sampleCount)
    {
        var written = _lame.Encode(_encoder, _samples.AsSpan(0, sampleCount), _channels, _mp3);
        if (written > 0)
            _output.Write(_mp3, 0, written);
    }

    public override void Flush()
    {
        // Nothing to give early: LAME holds what it has until the end.
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (disposing)
                Finish();
        }
        finally
        {
            _lame.CloseEncoder(_encoder);
            base.Dispose(disposing);
        }
    }

    private void Finish()
    {
        var written = _lame.Flush(_encoder, _mp3);
        if (written > 0)
            _output.Write(_mp3, 0, written);

        var frame = _lame.LametagFrame(_encoder);
        if (frame.Length > 0 && _output.CanSeek)
        {
            var end = _output.Position;
            _output.Position = 0;
            _output.Write(frame, 0, frame.Length);
            _output.Position = end;
        }
    }
}
