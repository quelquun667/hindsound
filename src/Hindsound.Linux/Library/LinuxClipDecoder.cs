using Hindsound.Library;

namespace Hindsound.Linux.Library;

/// <summary>
/// Reads a clip of the library back to float samples on Linux, in code that runs anywhere: a wav by <see cref="ClipDecoderWav"/>,
/// an mp3 by NLayer (<see cref="ClipDecoderMp3"/>). What the app reads is stereo or mono at 48 kHz, as the Windows decoder
/// gives it, and the start and the end of an mp3 are cut at the same frames.
/// </summary>
internal sealed class LinuxClipDecoder : IClipDecoder
{
    public IDecodedStream Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        FileStream file;
        try
        {
            // Opened as the Windows decoder does: a file another program is writing or deleting can still be read.
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // The message names neither the file nor its folder: it ends up in the log.
            throw new ClipDecodeException(ex is FileNotFoundException or DirectoryNotFoundException ? "the file is not there" : "the file can't be opened", ex);
        }

        try
        {
            Span<byte> head = stackalloc byte[12];
            var length = file.Read(head);
            IClipDecoderSource source = ClipDecoderWav.Looks(head[..length])
                ? ClipDecoderWav.Open(file)
                : ClipDecoderMp3.Open(file);
            try
            {
                return new ClipDecoderStream(source);
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }
        catch (ClipDecodeException)
        {
            file.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            file.Dispose();
            throw new ClipDecodeException("the file can't be read", ex);
        }
    }
}
