using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>Which end of the selection.</summary>
internal enum EditorEnd
{
    Start,
    End,
}

/// <summary>
/// The part of the clip kept, in whole milliseconds so that a hundred nudges land where they should. Its rules hold whatever moves
/// it, the mouse or the keyboard: at least <see cref="EditLimits.MinSelectionSeconds"/>, inside the clip, and for the soundboard at
/// most <see cref="Soundboard.MaxSeconds"/>, the other end following the one that pushes past it.
/// </summary>
internal sealed class EditorSelection
{
    public static readonly long MinMs = ToMs(EditLimits.MinSelectionSeconds);
    public static readonly long NudgeMs = ToMs(EditLimits.NudgeSeconds);
    public static readonly long BigNudgeMs = ToMs(EditLimits.BigNudgeSeconds);
    public static readonly long SoundboardMs = ToMs(Soundboard.MaxSeconds);

    public EditorSelection(TimeSpan duration, EditorMode mode)
    {
        DurationMs = Math.Max(0, (long)Math.Round(duration.TotalMilliseconds));
        Mode = mode;
        StartMs = 0;
        EndMs = mode == EditorMode.Soundboard ? Math.Min(SoundboardMs, DurationMs) : DurationMs;
        _startEndMs = EndMs;
    }

    // Where the selection stood when the clip was read: what "moved" is measured from.
    private readonly long _startEndMs;

    public long DurationMs { get; }

    public EditorMode Mode { get; }

    public long StartMs { get; private set; }

    public long EndMs { get; private set; }

    public long KeptMs => EndMs - StartMs;

    /// <summary>Either end is not where the clip was read: the user moved it.</summary>
    public bool Moved => StartMs != 0 || EndMs != _startEndMs;

    /// <summary>The shortest selection: the clip itself when it is shorter than the rule.</summary>
    private long Shortest => Math.Min(MinMs, DurationMs);

    public long At(EditorEnd end) => end == EditorEnd.Start ? StartMs : EndMs;

    /// <summary>Longer than the soundboard takes. Never in soundboard mode, where the selection can't get there.</summary>
    public bool TooLongForSoundboard => KeptMs > SoundboardMs;

    public static long ToMs(double seconds) => (long)Math.Round(seconds * 1000);

    public static TimeSpan Time(long ms) => TimeSpan.FromMilliseconds(ms);

    /// <summary>
    /// A time kept as the page shows it, to the tenth. Just past the soundboard's limit the tenth would read as the limit
    /// itself, beside the warning that the selection is over it: there it goes to the hundredth, rounded up.
    /// </summary>
    public static string KeptText(long ms)
    {
        if (ms > SoundboardMs && ms <= SoundboardMs + 50)
        {
            var hundredths = Math.Ceiling(ms / 10.0) / 100;
            return Tr.Format($"{hundredths:0.00} s", $"{Tr.Number(hundredths, "0.00")}\u00A0s");
        }

        var seconds = ms / 1000.0;
        return Tr.Format($"{seconds:0.0} s", $"{Tr.Number(seconds, "0.0")}\u00A0s");
    }

    /// <summary>Moves one end by this much, by the same rules as a drag.</summary>
    public void Nudge(EditorEnd end, long byMs) => Move(end, At(end) + byMs);

    /// <summary>Puts one end here, or as near as the rules let it.</summary>
    public void Move(EditorEnd end, long ms)
    {
        if (end == EditorEnd.Start)
        {
            StartMs = Math.Clamp(ms, 0, Math.Max(0, EndMs - Shortest));
            if (Mode == EditorMode.Soundboard && EndMs - StartMs > SoundboardMs)
            {
                EndMs = StartMs + SoundboardMs;
            }
        }
        else
        {
            EndMs = Math.Clamp(ms, Math.Min(DurationMs, StartMs + Shortest), DurationMs);
            if (Mode == EditorMode.Soundboard && EndMs - StartMs > SoundboardMs)
            {
                StartMs = EndMs - SoundboardMs;
            }
        }
    }
}
