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
    /// <summary>
    /// Copies a window of both sources. Audio the buffers no longer hold by the time of the copy, on a loaded machine
    /// or because of a resize meanwhile, is cut from the start of both: a shorter clip, and no silence made up.
    /// Silence that is real stays, like a source that began later or has nothing to say at the end.
    /// </summary>
    /// <param name="system">The PC sound, if any.</param>
    /// <param name="mic">The microphone, if any.</param>
    /// <param name="startFrame">Timeline frame the window starts at.</param>
    /// <param name="frames">Length of the window.</param>
    public static AudioSnapshot CopyFrom(SourceTrack? system, SourceTrack? mic, long startFrame, int frames)
    {
        // Every sample gets written by the copy, real or zero, so clearing the memory first would be wasted time.
        var systemSamples = Allocate(system, frames);
        var micSamples = Allocate(mic, frames);
        var lost = Math.Max(
            system?.CopyWindow(startFrame, systemSamples) ?? 0,
            mic?.CopyWindow(startFrame, micSamples) ?? 0);
        return lost == 0
            ? new AudioSnapshot(systemSamples, micSamples)
            : new AudioSnapshot(systemSamples[(lost * (system?.Channels ?? 0))..], micSamples[(lost * (mic?.Channels ?? 0))..]);
    }

    private static float[] Allocate(SourceTrack? track, int frames)
    {
        return track is null || frames == 0 ? [] : GC.AllocateUninitializedArray<float>(frames * track.Channels);
    }

    public TimeSpan Duration
    {
        get
        {
            var frames = Math.Max(System.Length / InternalFormat.SystemChannels, Mic.Length / InternalFormat.MicChannels);
            return TimeSpan.FromSeconds((double)frames / InternalFormat.SampleRate);
        }
    }
}
