using Hindsound.Library;

namespace Hindsound.Audio;

/// <summary>Reads an mp3 or a wav back to float samples through Media Foundation (IMFSourceReader).</summary>
internal sealed class MediaFoundationClipDecoder : IClipDecoder
{
    public IDecodedStream Open(string path) => throw new ClipDecodeException("not decoded yet");
}
