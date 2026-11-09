using Hindsound.Audio;

namespace Hindsound.Ui.Window;

/// <summary>What the three labels of the wave's axis say.</summary>
internal readonly record struct WaveAxis(string Left, string Middle, string Right);

/// <summary>A change of source that shows in the window, and the column its line goes on.</summary>
internal readonly record struct WaveMarker(int Column, string? App)
{
    /// <summary>No line to draw.</summary>
    public static readonly WaveMarker None = new(-1, null);

    public bool Shows => Column >= 0;
}

/// <summary>
/// The colour of a bar. Two independent axes: the intensity says whether the save button would take it, the hue says
/// whether it comes from what is chosen now or from the mixed past before the last change of source.
/// </summary>
internal enum WaveTone
{
    /// <summary>The set colour: in the part that would be saved.</summary>
    Accent,

    /// <summary>The set colour, dimmed: kept, outside that part.</summary>
    AccentDim,

    /// <summary>Light grey: from before the change, and would be saved all the same.</summary>
    GrayLight,

    /// <summary>Dark grey: from before the change, outside that part.</summary>
    GrayDark,
}

/// <summary>
/// The logic of the live wave, with nothing to draw with: which bars, where the saved part starts, and the words.
/// <para>
/// Columns sit on a grid fixed on the timeline, not on the edge of the window. If they followed the edge, each tick
/// would group other slices into each bar, and every bar would flicker. On this grid a bar moves left as a whole.
/// </para>
/// </summary>
internal static class WaveView
{
    private const int Rate = InternalFormat.SampleRate;

    /// <summary>
    /// The height of a bar from the level of its loudest slice, 0 to <see cref="WaveLane.FullLevel"/>: the one place to
    /// change if loud music turns out to look like a solid block. The levels follow the dB scale of a clip's bars.
    /// </summary>
    public static byte Height(byte level) => level;

    /// <summary>
    /// Fills one bar per column, the newest on the right: <see cref="Height"/>, or <see cref="WaveLane.Empty"/> where
    /// the lane has nothing to show. A lane with no track keeps its place on the screen as a flat line, all 0.
    /// Allocates nothing.
    /// <para>
    /// Equal bars do not mean an equal picture: the line of a change and the start of the saved part slide with time
    /// while a long silence leaves every bar at 0. To know whether to repaint, compare the bars, and also
    /// <see cref="ZoneOf"/> and the column of <see cref="MarkerOf"/>: integers, so no allocation.
    /// </para>
    /// </summary>
    public static void Compose(WaveLane lane, in WaveFacts facts, Span<byte> bars)
    {
        if (!lane.HasTrack)
        {
            bars.Clear();
            return;
        }

        bars.Fill(WaveLane.Empty);
        if (facts.End <= facts.Start || bars.IsEmpty)
        {
            return;
        }

        var columns = bars.Length;
        var slices = SlicesOnScreen(facts);
        var right = LastColumnOfSlice(WaveLane.SliceOf(facts.End - 1), slices, columns);
        for (var at = 0; at < columns; at++)
        {
            var column = right - (columns - 1 - at);
            if (column < 0)
            {
                continue;
            }

            var loudest = WaveLane.Empty;
            var first = column * slices / columns;
            var stop = Math.Max(first + 1, (column + 1) * slices / columns);
            for (var slice = first; slice < stop; slice++)
            {
                var level = lane.LevelOf(slice);
                if (level != WaveLane.Empty && (loudest == WaveLane.Empty || level > loudest))
                {
                    loudest = level;
                }
            }

            bars[at] = loudest == WaveLane.Empty ? WaveLane.Empty : Height(loudest);
        }
    }

    /// <summary>
    /// The first column of the part a clip of this length would hold: the same start as the clip's, on the same end.
    /// As many as there are columns when there is nothing to save.
    /// </summary>
    public static int ZoneOf(int clipSeconds, in WaveFacts facts, int columns)
    {
        if (facts.End <= facts.Start)
        {
            return columns;
        }

        return Math.Max(0, FirstColumnOf(ZoneStart(clipSeconds, facts), facts, columns));
    }

    /// <summary>The line of the last change of source. It goes away once its frame leaves the window.</summary>
    public static WaveMarker MarkerOf(in WaveFacts facts, int columns)
    {
        if (LastChange(facts) is not { } change || change.Frame <= facts.Start || columns <= 0)
        {
            return WaveMarker.None;
        }

        var column = Math.Min(FirstColumnOf(change.Frame, facts, columns), columns - 1);
        return column < 0 ? WaveMarker.None : new WaveMarker(column, change.App);
    }

    /// <summary>The colour of a bar of the PC side. The microphone has no mixed past: pass no marker for it.</summary>
    public static WaveTone ToneOf(int column, int zoneFirstColumn, WaveMarker marker)
    {
        var saved = column >= zoneFirstColumn;
        return marker.Shows && column < marker.Column
            ? saved ? WaveTone.GrayLight : WaveTone.GrayDark
            : saved ? WaveTone.Accent : WaveTone.AccentDim;
    }

    public static WaveAxis AxisLabels(int bufferSeconds, CaptureActivity activity)
    {
        // Half of a 1 s memory has nothing to say.
        var middle = bufferSeconds < 2 ? string.Empty : "-" + NotificationStates.FormatDuration((bufferSeconds + 1) / 2);
        return new WaveAxis(
            "-" + NotificationStates.FormatDuration(bufferSeconds),
            middle,
            activity == CaptureActivity.Paused ? "paused" : "now");
    }

    /// <summary>The name of the PC side: the whole PC, or the one application asked for.</summary>
    public static string SystemLaneTitle(AudioEngineStatus? status)
    {
        if (status?.SourceApp is not { } exe)
        {
            return "PC sound";
        }

        var app = AppName(exe);
        return status.SourceAppMissing ? app + " (not running)" : app;
    }

    /// <summary>
    /// The name of the microphone's lane. Paused or stopped, no capture is open on purpose: that is not a lost microphone.
    /// </summary>
    public static string MicLaneTitle(AudioEngineStatus? status)
    {
        if (status is null)
        {
            return "Mic";
        }

        if (!status.MicEnabled)
        {
            return "Mic off";
        }

        return status.MicCapturing || status.Activity is CaptureActivity.Paused or CaptureActivity.Stopped ? "Mic" : "Mic lost";
    }

    /// <summary>
    /// The line under the wave. The time counted from before a change of source is said out loud: the clip holds it,
    /// and it is the sound of whatever was chosen back then.
    /// <para>
    /// It depends on the end of the window, so on time alone: with the memory full the status never changes, yet the
    /// seconds from before a change shrink until it falls out of the clip. Whoever draws it compares
    /// <see cref="SaveSeconds"/> at each tick, two integers and no allocation, and builds the text only when they differ.
    /// </para>
    /// </summary>
    public static string SaveLine(int clipSeconds, in WaveFacts facts)
    {
        var (kept, before) = SaveSeconds(clipSeconds, facts);
        var line = "Save: last " + NotificationStates.FormatDuration(kept);
        if (kept < clipSeconds)
        {
            line += " of " + NotificationStates.FormatDuration(clipSeconds);
        }

        if (before > 0)
        {
            line += $" ({NotificationStates.FormatDuration(before)} from before {SourceName(LastChange(facts)!.Value.App)})";
        }

        return line;
    }

    /// <summary>
    /// The seconds the save line is made of: the part a clip would keep, and how much of it comes from before the last
    /// change of source (0 when none does). Allocates nothing.
    /// </summary>
    public static (int Kept, int Before) SaveSeconds(int clipSeconds, in WaveFacts facts)
    {
        var start = ZoneStart(clipSeconds, facts);
        // A change right at the end counts: paused after choosing an app, everything held is from before it.
        var before = LastChange(facts) is { } change && change.Frame > start && change.Frame <= facts.End
            ? SecondsOf(change.Frame - start)
            : 0;
        return (SecondsOf(facts.End - start), before);
    }

    /// <summary>The words on the line of a change of source.</summary>
    public static string MarkerText(string? app) => app is null ? "PC sound from here" : AppName(app) + " only from here";

    /// <summary>
    /// What to say when nothing can be captured, or null. The same two cases as the status band: with an application
    /// asked for, what failed is that capture, not the devices.
    /// </summary>
    public static string? NoCaptureText(AudioEngineStatus? status)
    {
        if (status?.Activity != CaptureActivity.NoDevice)
        {
            return null;
        }

        return status.SourceApp is { } exe ? "Can't capture " + AppName(exe) : "No audio device";
    }

    private static string SourceName(string? app) => app is null ? "PC sound" : AppName(app);

    private static string AppName(string exe) => Path.GetFileNameWithoutExtension(exe);

    private static int SecondsOf(long frames) => NotificationStates.WholeSeconds(TimeSpan.FromSeconds((double)frames / Rate));

    // The same frame the engine would start a clip of this length at: the window already stops at the memory set.
    private static long ZoneStart(int clipSeconds, in WaveFacts facts) => Math.Max(facts.End - (long)clipSeconds * Rate, facts.Start);

    private static SourceChange? LastChange(in WaveFacts facts) => facts.Changes is { Count: > 0 } changes ? changes[changes.Count - 1] : null;

    // As many slices as the memory is set to, whatever it holds so far: this is what keeps the bars still.
    private static long SlicesOnScreen(in WaveFacts facts) =>
        Math.Max(1, (facts.BufferFrames + (long)WaveLane.FramesPerSlice - 1) / WaveLane.FramesPerSlice);

    // A column holds the slices from column * slices / columns on. With fewer slices than columns, one slice has several.
    private static long LastColumnOfSlice(long slice, long slices, int columns) => ((slice + 1) * columns + slices - 1) / slices - 1;

    private static int FirstColumnOf(long frame, in WaveFacts facts, int columns)
    {
        var slices = SlicesOnScreen(facts);
        var slice = WaveLane.SliceOf(frame);
        var last = LastColumnOfSlice(slice, slices, columns);
        var first = Math.Min(last, (slice * columns + slices - 1) / slices);
        var right = LastColumnOfSlice(WaveLane.SliceOf(facts.End - 1), slices, columns);
        return (int)Math.Max(first - right + columns - 1, -1);
    }
}
