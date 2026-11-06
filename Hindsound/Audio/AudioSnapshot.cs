namespace Hindsound.Audio;

/// <summary>
/// The past audio handed from capture to export, in <see cref="InternalFormat"/>.
/// Both sources end at the same instant. They are time-aligned, so <see cref="System"/> holds
/// two samples for each sample of <see cref="Mic"/>, unless the microphone is off.
/// </summary>
/// <param name="System">PC audio, interleaved stereo. Empty if there is none.</param>
/// <param name="Mic">Microphone, mono. Empty if the microphone is off.</param>
public sealed record AudioSnapshot(float[] System, float[] Mic)
{
    public TimeSpan Duration
    {
        get
        {
            var frames = Math.Max(System.Length / InternalFormat.SystemChannels, Mic.Length / InternalFormat.MicChannels);
            return TimeSpan.FromSeconds((double)frames / InternalFormat.SampleRate);
        }
    }
}
