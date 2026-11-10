using Hindsound.Audio;

namespace Hindsound.Linux.Export;

/// <summary>
/// The MP3 encoder of <see cref="Hindsound.App.PlatformDefaults.Mp3Encoder"/>, through the libmp3lame.so.0 shipped with the
/// app (built from the same LAME 3.100 as the DLL of Windows), or the one of the system. Constant bitrate and the other
/// settings of LAME at their defaults, as NAudio.Lame leaves them on Windows. When the library is missing, <see cref="Open"/>
/// throws and the saver writes the clip as WAV.
/// </summary>
internal static class LameMp3Encoder
{
    /// <summary>A stream that takes 16-bit interleaved PCM and writes MP3 to <paramref name="output"/>.</summary>
    public static Stream Open(Stream output, int channels, int bitrateKbps) =>
        Open(LameLibrary.Shared ?? throw new DllNotFoundException(LameLibrary.LastError), output, channels, bitrateKbps);

    /// <summary>The same through a given library.</summary>
    internal static Stream Open(LameLibrary lame, Stream output, int channels, int bitrateKbps) =>
        new LameMp3Stream(lame, output, InternalFormat.SampleRate, channels, bitrateKbps);
}
