namespace Hindsound.Audio;

/// <summary>The format audio is captured and exported in: 48 kHz. The buffer stores it as 16-bit samples.</summary>
public static class InternalFormat
{
    public const int SampleRate = 48000;

    /// <summary>PC audio is kept in stereo, samples interleaved left then right.</summary>
    public const int SystemChannels = 2;

    /// <summary>The microphone is kept in mono.</summary>
    public const int MicChannels = 1;

    /// <summary>Memory used per second of buffer by the PC audio source.</summary>
    public const int SystemBytesPerSecond = SampleRate * SystemChannels * sizeof(short);

    /// <summary>Memory used per second of buffer by the microphone source.</summary>
    public const int MicBytesPerSecond = SampleRate * MicChannels * sizeof(short);
}
