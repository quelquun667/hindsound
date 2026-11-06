namespace Hindsound.Export;

/// <summary>What the interface needs after a save.</summary>
/// <param name="Paths">The file(s) written: one in mix mode, <c>_system</c> then <c>_mic</c> in multitrack mode.</param>
/// <param name="Duration">Length of the clip. All files have this length.</param>
/// <param name="SizeBytes">Total size of the file(s) on disk.</param>
/// <param name="IsSilent">The clip never goes above -60 dBFS.</param>
/// <param name="Bars">The 24 bars of the notification, from 0 to 1, the most recent last.</param>
/// <param name="Rescued">The output folder was not writable, so the file(s) are in the rescue folder.</param>
public sealed record ClipResult(
    IReadOnlyList<string> Paths,
    TimeSpan Duration,
    long SizeBytes,
    bool IsSilent,
    IReadOnlyList<float> Bars,
    bool Rescued)
{
    /// <summary>The first file: the whole clip in mix mode, the PC audio track in multitrack mode.</summary>
    public string PrimaryPath => Paths[0];
}
