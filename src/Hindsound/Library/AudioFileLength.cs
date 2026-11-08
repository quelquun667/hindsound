using System.Buffers.Binary;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace Hindsound.Library;

/// <summary>
/// The duration of a WAV or MP3 file, from its headers alone: nothing is decoded, and only a few KB of the file are read.
/// A truncated or false file has no known duration, which is null: a duration read from a broken header would be shown
/// as if it were true.
/// </summary>
public static class AudioFileLength
{
    // Far more than a real WAV has, for a false file made of chunks of nothing.
    private const int MaxWavChunks = 64;

    // What is read after the tags to find the first MP3 frame: enough to hold a frame of any size and the next header.
    private const int Mp3HeadBytes = 8192;

    private const int Id3HeaderBytes = 10;
    private const int Id3v1Bytes = 128;

    // The attributes of a file kept in the cloud (OneDrive and the like, where a Music folder often lives): opening it
    // downloads it, which a look at a list must not do. The two recall flags have no name in FileAttributes.
    private const FileAttributes OnlineOnly = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;

    private static readonly int[] Mpeg1Kbps = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Kbps = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[] Mpeg1Rates = [44100, 48000, 32000];

    /// <summary>Null when the duration is not known. Never throws for a file. Touches the disk: not for the thread of the window.</summary>
    /// <param name="journal">Told of a failure: only the type of the exception and its code, never a path or a message.</param>
    public static TimeSpan? Read(string path, Action<string>? journal = null)
    {
        try
        {
            if ((File.GetAttributes(path) & OnlineOnly) != 0)
            {
                return null;
            }

            // Shared in every way: this look must never get in the way of a program using the file.
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var extension = Path.GetExtension(path.AsSpan());
            if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)) return ReadWav(file);
            if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) return ReadMp3(file);
            return null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException)
        {
            // Removed since the scan: nothing to tell.
            return null;
        }
        catch (Exception ex)
        {
            journal?.Invoke($"clip length: a file can't be read ({ClipFolders.Describe(ex)})");
            return null;
        }
    }

    // ---------------------------------------------------------------- WAV

    private static TimeSpan? ReadWav(SafeFileHandle file)
    {
        var length = RandomAccess.GetLength(file);
        Span<byte> riff = stackalloc byte[12];
        if (Fill(file, riff, 0) < riff.Length || !riff[..4].SequenceEqual("RIFF"u8) || !riff[8..].SequenceEqual("WAVE"u8))
        {
            return null;
        }

        // A chunk header and the start of the format chunk that may follow it.
        Span<byte> chunk = stackalloc byte[8 + 16];
        uint bytesPerSecond = 0;
        long dataBytes = 0;
        var offset = (long)riff.Length;
        for (var i = 0; i < MaxWavChunks && offset + 8 <= length; i++)
        {
            var got = Fill(file, chunk, offset);
            if (got < 8) return null;

            long size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (size < 16 || got < chunk.Length) return null;
                bytesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian(chunk[16..]);
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                // Longer than the file: truncated, or still at the placeholder of a writer that never finished.
                if (offset + 8 + size > length) return null;
                dataBytes = size;
            }

            if (bytesPerSecond > 0 && dataBytes > 0)
            {
                return TimeSpan.FromSeconds((double)dataBytes / bytesPerSecond);
            }

            offset += 8 + size + (size & 1); // a chunk of odd size is followed by one byte of padding
        }

        return null;
    }

    // ---------------------------------------------------------------- MP3

    private static TimeSpan? ReadMp3(SafeFileHandle file)
    {
        var length = RandomAccess.GetLength(file);
        var audioStart = AfterId3v2(file);
        var audioEnd = length - Id3v1Size(file, length);

        Span<byte> buffer = stackalloc byte[Mp3HeadBytes];
        var head = buffer[..Fill(file, buffer, audioStart)];
        for (var i = 0; i + 4 <= head.Length; i++)
        {
            if (!Mp3Frame.TryRead(head[i..], out var frame))
            {
                continue;
            }

            var frameEnd = audioStart + i + frame.Length;
            if (frameEnd > audioEnd)
            {
                continue;
            }

            // Sound data can look like a header. The header of the next frame right behind it, or the end of the file, can't.
            if (frameEnd < audioEnd && !FollowedByItsLike(head, i + frame.Length, frame))
            {
                continue;
            }

            return Mp3Duration(head[i..], frame, audioEnd - (audioStart + i), length);
        }

        return null;
    }

    private static bool FollowedByItsLike(ReadOnlySpan<byte> head, int next, in Mp3Frame frame) =>
        next < head.Length
        && Mp3Frame.TryRead(head[next..], out var second)
        && second.Mpeg1 == frame.Mpeg1
        && second.SampleRate == frame.SampleRate;

    private static TimeSpan? Mp3Duration(ReadOnlySpan<byte> first, in Mp3Frame frame, long audioBytes, long fileBytes)
    {
        // The Info frame (Xing for a variable bitrate) sits where the side information of a frame would be.
        var tag = 4 + (frame.HasCrc ? 2 : 0) + (frame.Mpeg1 ? (frame.Mono ? 17 : 32) : (frame.Mono ? 9 : 17));
        if (first.Length >= tag + 8 && (first.Slice(tag, 4).SequenceEqual("Info"u8) || first.Slice(tag, 4).SequenceEqual("Xing"u8)))
        {
            var flags = BinaryPrimitives.ReadUInt32BigEndian(first[(tag + 4)..]);
            var at = tag + 8;
            uint frames = 0, bytes = 0;
            if ((flags & 1) != 0 && first.Length >= at + 4)
            {
                frames = BinaryPrimitives.ReadUInt32BigEndian(first[at..]);
                at += 4;
            }

            if ((flags & 2) != 0 && first.Length >= at + 4)
            {
                bytes = BinaryPrimitives.ReadUInt32BigEndian(first[at..]);
            }

            // The tag was written at the end of the encoding, so it knows the whole size: less than that on disk is a cut file.
            if (bytes > fileBytes)
            {
                return null;
            }

            if (frames > 0)
            {
                return TimeSpan.FromSeconds((double)frames * frame.SamplesPerFrame / frame.SampleRate);
            }
        }

        return TimeSpan.FromSeconds(audioBytes * 8.0 / frame.BitsPerSecond);
    }

    /// <summary>Where the sound starts: after an ID3v2 tag if there is one, whose size is in 4 bytes of 7 bits.</summary>
    private static long AfterId3v2(SafeFileHandle file)
    {
        Span<byte> id3 = stackalloc byte[Id3HeaderBytes];
        if (Fill(file, id3, 0) < id3.Length || !id3[..3].SequenceEqual("ID3"u8) || ((id3[6] | id3[7] | id3[8] | id3[9]) & 0x80) != 0)
        {
            return 0;
        }

        var size = (long)((id3[6] << 21) | (id3[7] << 14) | (id3[8] << 7) | id3[9]);
        var hasFooter = (id3[5] & 0x10) != 0;
        return Id3HeaderBytes + size + (hasFooter ? Id3HeaderBytes : 0);
    }

    private static int Id3v1Size(SafeFileHandle file, long length)
    {
        Span<byte> tag = stackalloc byte[3];
        return length >= Id3v1Bytes && Fill(file, tag, length - Id3v1Bytes) == tag.Length && tag.SequenceEqual("TAG"u8) ? Id3v1Bytes : 0;
    }

    /// <summary>Reads until the span is full or the file ends; a read may return less than was asked.</summary>
    private static int Fill(SafeFileHandle file, Span<byte> into, long offset)
    {
        var total = 0;
        while (total < into.Length)
        {
            var read = RandomAccess.Read(file, into[total..], offset + total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>An MPEG audio frame header, layer III only: that is what a file named .mp3 holds, and all that LAME writes.</summary>
    private readonly record struct Mp3Frame(bool Mpeg1, int SampleRate, int BitsPerSecond, int Length, int SamplesPerFrame, bool Mono, bool HasCrc)
    {
        public static bool TryRead(ReadOnlySpan<byte> bytes, out Mp3Frame frame)
        {
            frame = default;
            if (bytes.Length < 4 || bytes[0] != 0xFF || (bytes[1] & 0xE0) != 0xE0)
            {
                return false;
            }

            var version = (bytes[1] >> 3) & 3; // 3: MPEG 1, 2: MPEG 2, 0: MPEG 2.5, 1: reserved
            var layer = (bytes[1] >> 1) & 3; // 1: layer III
            var bitrateIndex = bytes[2] >> 4; // 0: free format, whose frame size no header gives; 15: forbidden
            var rateIndex = (bytes[2] >> 2) & 3; // 3: reserved
            if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3)
            {
                return false;
            }

            var mpeg1 = version == 3;
            var sampleRate = Mpeg1Rates[rateIndex] / (mpeg1 ? 1 : version == 2 ? 2 : 4);
            var bitsPerSecond = (mpeg1 ? Mpeg1Kbps : Mpeg2Kbps)[bitrateIndex] * 1000;
            var padding = (bytes[2] >> 1) & 1;
            var frameLength = (mpeg1 ? 144 : 72) * bitsPerSecond / sampleRate + padding;
            var mono = bytes[3] >> 6 == 3;
            var hasCrc = (bytes[1] & 1) == 0;
            frame = new Mp3Frame(mpeg1, sampleRate, bitsPerSecond, frameLength, mpeg1 ? 1152 : 576, mono, hasCrc);
            return true;
        }
    }
}
