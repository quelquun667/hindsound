using System.IO;
using Hindsound.Export;

namespace Hindsound.Library;

/// <summary>What the editor is opened for.</summary>
internal enum EditorMode
{
    /// <summary>Cut and volume, saved as a copy or in place of the original.</summary>
    Normal,

    /// <summary>A short copy for the Discord soundboard: the selection holds at most <see cref="Soundboard.MaxSeconds"/>.</summary>
    Soundboard,
}

/// <summary>What the Discord soundboard takes, as of November 2026.</summary>
internal static class Soundboard
{
    public const double MaxSeconds = 5.2;
    public const long MaxBytes = 512 * 1024;
    public const int MinNameLength = 2, MaxNameLength = 32;
    public const string CopySuffix = " (soundboard)";
    public const string DiscordUri = "discord://";
    public const string FitsText = "Fits the Discord soundboard (5.2 s max)";
    public const string TooLongText = "Too long for the soundboard: 5.2 s max";
    public const string UploadHint = "In Discord: Server Settings > Soundboard > Upload Sound";
}

/// <summary>The bounds of an edit.</summary>
internal static class EditLimits
{
    public const double MinSelectionSeconds = 0.2;
    public const double NudgeSeconds = 0.1, BigNudgeSeconds = 1;
    public const double MinGainDb = -12, MaxGainDb = 20, GainStepDb = 1;
    public const string CutSuffix = " (cut)";
}

/// <summary>A file that can't be decoded: absent, damaged, or in a format the system can't read.</summary>
public sealed class ClipDecodeException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>Decodes an audio file of the library (mp3 or wav) to float samples. Media Foundation on Windows.</summary>
internal interface IClipDecoder
{
    /// <summary>Opens the file for reading. Throws <see cref="ClipDecodeException"/> if it can't be read.</summary>
    IDecodedStream Open(string path);
}

/// <summary>Where no decoder is given (a system without one): every file is unreadable.</summary>
internal sealed class NoClipDecoder : IClipDecoder
{
    public static readonly NoClipDecoder Instance = new();

    public IDecodedStream Open(string path) => throw new ClipDecodeException("no decoder on this system");
}

/// <summary>
/// The samples of one file, read in order: interleaved floats at <see cref="Audio.InternalFormat.SampleRate"/>, in one or two
/// channels, whatever the file holds. Used on one thread at a time.
/// </summary>
internal interface IDecodedStream : IDisposable
{
    /// <summary>1 or 2.</summary>
    int Channels { get; }

    /// <summary>The length the file announces. The frames read may end a little before or after it.</summary>
    TimeSpan Duration { get; }

    /// <summary>The next read starts at this time, to the frame. Throws <see cref="ClipDecodeException"/> if the file can't seek.</summary>
    void Seek(TimeSpan at);

    /// <summary>Fills as much of <paramref name="interleaved"/> as it can, in whole frames. Returns the number of floats written; 0 at the end.</summary>
    int Read(Span<float> interleaved);
}

/// <summary>What the editor asks for: a stretch of the clip, and the volume of each of its files.</summary>
/// <param name="Mode">Normal, or soundboard: a mix of the tracks, in one mp3 of at most <see cref="Soundboard.MaxBytes"/>.</param>
/// <param name="Start">From the start of the clip.</param>
/// <param name="End">From the start of the clip, after <paramref name="Start"/>.</param>
/// <param name="GainsDb">One per file of the clip, in its order, from <see cref="EditLimits.MinGainDb"/> to <see cref="EditLimits.MaxGainDb"/>.</param>
internal sealed record EditRequest(EditorMode Mode, TimeSpan Start, TimeSpan End, IReadOnlyList<double> GainsDb)
{
    public TimeSpan Kept => End - Start;
}

/// <summary>Where the edit goes.</summary>
internal enum EditSaveChoice
{
    /// <summary>A new file next to the original, which stays as it is. The default.</summary>
    Copy,

    /// <summary>The original goes to the Recycle Bin and the edit takes its name, its star and its collections.</summary>
    Replace,
}

/// <summary>How to save the edit.</summary>
/// <param name="Choice">Copy or replace. A soundboard edit is always a copy.</param>
/// <param name="CopyName">The name asked for the copy, without extension; made unique on save. Null for a replacement.</param>
internal sealed record EditSave(EditSaveChoice Choice, string? CopyName);

public enum EditSaveOutcome
{
    Saved,

    /// <summary>The Recycle Bin can't take the original: nothing changed, no file was left behind.</summary>
    CannotRecycle,

    /// <summary>The original is no longer there.</summary>
    SourceGone,

    /// <summary>A soundboard copy would be bigger than <see cref="Soundboard.MaxBytes"/>. Nothing was written.</summary>
    TooBig,

    /// <summary>
    /// A replacement that could not take the place of the original: the original went to the Recycle Bin but its name could not
    /// be taken, only part of it went, or it was gone by the time it was to go. The edit was kept as a clip of its own, whose
    /// paths are in <see cref="EditSaveResult.Paths"/>; what went to the Recycle Bin can come back from it. Paths is empty, or
    /// short of a file, when not even a name of its own could be made: that part of the edit was removed, never left hidden.
    /// </summary>
    SavedAside,

    /// <summary>Anything else. The original is as it was, and nothing of the edit is left on disk.</summary>
    Failed,
}

/// <summary>What a save did.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Paths">The files written, in the order of the files of the clip (one for a soundboard copy). Empty unless saved, or unless a replacement kept its edit under a name of its own.</param>
/// <param name="OriginalInBin">True when at least one file of the original went to the Recycle Bin. Only a replacement sets it.</param>
internal sealed record EditSaveResult(EditSaveOutcome Outcome, IReadOnlyList<string> Paths, bool OriginalInBin = false);

public enum EditorLoadOutcome
{
    Loaded,

    /// <summary>The clip is no longer there.</summary>
    Gone,

    /// <summary>The system can't decode a file of the clip.</summary>
    Unreadable,
}

/// <summary>The clip as the editor shows it.</summary>
/// <param name="Outcome">Whether it could be read.</param>
/// <param name="Duration">The length of the clip: of its longest file for a clip in two files.</param>
/// <param name="Peaks">One list of bars per file, in the order of the files, each value from 0 to 1, spread over the whole length.</param>
/// <param name="Format">Mp3 or Wav: what a save writes, but for a soundboard copy, always mp3.</param>
internal sealed record EditorLoad(EditorLoadOutcome Outcome, TimeSpan Duration, IReadOnlyList<IReadOnlyList<float>> Peaks, ExportFormat Format);
