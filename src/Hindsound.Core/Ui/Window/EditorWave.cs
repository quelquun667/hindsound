using System.Globalization;
using System.Runtime.CompilerServices;
using Hindsound.App;
using Hindsound.Audio;

namespace Hindsound.Ui.Window;

/// <summary>
/// Where a time of the clip lies on the wave of the editor, and back. The one rule for the bars, the handles, the play head and the
/// mouse: they all go through here, so a handle sits on the bar of its time.
/// </summary>
/// <param name="X0">Where 0:00 is, in design units.</param>
/// <param name="X1">Where the end of the clip is.</param>
/// <param name="DurationMs">The length of the clip.</param>
internal readonly record struct EditorTimeline(float X0, float X1, long DurationMs)
{
    public static EditorTimeline Of(Box panel, int lanes, long durationMs, double scale)
    {
        var grid = EditorWave.Grid(panel, lanes, 0, scale);
        return new EditorTimeline((float)(grid.Start / scale), (float)(grid.End / scale), durationMs);
    }

    public float XOf(long ms) => DurationMs <= 0 ? X0 : X0 + (float)((double)Math.Clamp(ms, 0, DurationMs) / DurationMs * (X1 - X0));

    public long MsAt(float x) => DurationMs <= 0 || X1 <= X0 ? 0 : (long)Math.Round(Math.Clamp((x - X0) / (X1 - X0), 0, 1) * DurationMs);
}

/// <summary>What the wave of the editor shows, read at paint time.</summary>
/// <param name="Peaks">One list of bars per file, 0 to 1.</param>
/// <param name="GainsDb">The volume set for each file.</param>
/// <param name="StartMs">The start of the part kept.</param>
/// <param name="EndMs">Its end.</param>
/// <param name="DurationMs">The length of the clip.</param>
/// <param name="PlayheadMs">Where the playback is, or null when nothing plays.</param>
/// <param name="SoundboardLimit">Mark where 5.2 s after the start ends: the part kept is longer than the soundboard takes.</param>
/// <param name="CeilingDb">Where a boosted peak is held.</param>
/// <param name="Machine">What the system calls the computer, for the name of the lane of its sound.</param>
internal sealed record EditorWaveView(
    IReadOnlyList<IReadOnlyList<float>> Peaks,
    IReadOnlyList<double> GainsDb,
    long StartMs,
    long EndMs,
    long DurationMs,
    long? PlayheadMs,
    bool SoundboardLimit,
    double CeilingDb,
    string Machine = "PC");

/// <summary>The wave of the editor: one lane per file, the part kept lit, the play head, the axis under it.</summary>
internal static class EditorWave
{
    public const float Top = 112, Height = 128, Gutter = 68, GripWidth = 18, GripHeight = 48, AxisGap = 11;

    /// <summary>The name of a lane of a clip in two files, in the order of its files: the sound of the computer, then the microphone.</summary>
    public static string LaneName(int index, string machine) => index switch { 0 => "Whole " + machine, 1 => "Mic", _ => "" };

    /// <summary>
    /// How far into the panel the name of a lane starts: 12 px, or further left for a name too wide to end before the wave, which
    /// begins a gutter in. "Whole PC" keeps its 12; "Whole Mac" is wider and leaves 3 px before the wave.
    /// </summary>
    public static float LaneNameOffset(float nameWidth) => Math.Clamp(Gutter - 3 - nameWidth, 4, 12);

    public static Box Panel => new(SettingsScreen.Left, Top, SettingsScreen.Width, Height);

    /// <summary>What a tick of the play head draws again: the wave, its handles and its axis, with room for their glow.</summary>
    public static Box Area => new(Panel.X - 10, Panel.Y - 6, Panel.W + 20, Panel.H + 6 + AxisGap + 11);

    public static WaveGrid Grid(Box panel, int lanes, int lane, double scale)
    {
        lanes = Math.Max(1, lanes);
        var gutter = lanes > 1 ? Gutter : 0;
        var laneHeight = panel.H / lanes;
        return WaveGrid.Of(new Box(panel.X + gutter, panel.Y + lane * laneHeight, panel.W - gutter, laneHeight), scale);
    }

    public static string AxisClock(long ms) => string.Create(CultureInfo.InvariantCulture, $"{ms / 60_000}:{ms / 1000 % 60:00}");

    public static void Paint(ICanvas c, UiState ui, EditorWaveView view)
    {
        var s = c.Scale;
        var panel = Panel;
        var accent = ui.Accent;
        var zoneGround = Theme.Mix(accent, Theme.Base, 0.08f);
        var dim = Theme.Mix(accent, Theme.Base, 0.36f);
        c.Frame(panel, 4, Theme.Base, Theme.Line);

        var count = Math.Max(1, view.Peaks.Count);
        var timeline = EditorTimeline.Of(panel, count, view.DurationMs, s);
        var from = timeline.XOf(view.StartMs);
        var to = timeline.XOf(view.EndMs);
        var held = Math.Pow(10, view.CeilingDb / 20);
        var laneHeight = panel.H / count;

        // The ground of the part kept is a little lighter than the rest.
        c.FillRect(new Box(from, panel.Y + 1, to - from, panel.H - 2), zoneGround);
        for (var i = 0; i < view.Peaks.Count; i++)
        {
            var grid = Grid(panel, count, i, s);
            var mid = grid.Mid / (float)s;
            if (count > 1)
            {
                var name = LaneName(i, view.Machine);
                c.TextMid(name, Face.Body, 12, panel.X + LaneNameOffset(c.Measure(name, Face.Body, 12)), mid, Theme.Muted);
                if (i > 0)
                {
                    c.FillRect(new Box(panel.X + 1, panel.Y + i * laneHeight, panel.W - 2, 1), Theme.Line);
                }
            }

            var gainDb = i < view.GainsDb.Count ? view.GainsDb[i] : 0;
            var gain = Math.Pow(10, gainDb / 20);
            var peaks = view.Peaks[i];
            var levels = new byte[grid.Columns];
            var columns = new BarColumn[grid.Columns];
            for (var k = 0; k < grid.Columns; k++)
            {
                var a = (int)((long)k * peaks.Count / grid.Columns);
                var b = Math.Max(a + 1, (int)((long)(k + 1) * peaks.Count / grid.Columns));
                var peak = 0.0;
                for (var j = a; j < Math.Min(b, peaks.Count); j++)
                {
                    peak = Math.Max(peak, peaks[j]);
                }

                peak = Math.Min(peak * gain, held);
                levels[k] = peak < 0.01 ? (byte)0 : (byte)Math.Round(Math.Sqrt(peak) * WaveLane.FullLevel);
                var centre = (grid.X(k) + grid.Bar / 2.0) / s;
                columns[k] = centre >= from && centre <= to
                    ? new BarColumn(accent, Theme.Mix(accent, zoneGround, 0.40f))
                    : new BarColumn(dim);
            }

            c.Bars(grid, levels, columns, Theme.Line);
            if (gainDb > 0)
            {
                // Where a boosted peak stops, drawn as a dotted line above and below the base line.
                var reach = (float)(Math.Sqrt(held) * grid.Half / s);
                var dash = Theme.Mix(Theme.Text, Theme.Base, 0.30f);
                for (var x = panel.X + (count > 1 ? Gutter : 0) + 6; x < panel.Right - 10; x += 8)
                {
                    c.FillRect(new Box(x, mid - reach - 0.5f, 3, 1), dash);
                    c.FillRect(new Box(x, mid + reach - 0.5f, 3, 1), dash);
                }
            }
        }

        if (view.SoundboardLimit)
        {
            var limit = timeline.XOf(view.StartMs + EditorSelection.SoundboardMs);
            for (var y = panel.Y + 2; y < panel.Bottom - 4; y += 7)
            {
                c.FillRect(new Box(limit - 0.5f, y, 1, 4), Theme.Amber);
            }

            c.FillPolygon([(limit - 5, panel.Y), (limit + 5, panel.Y), (limit, panel.Y + 7)], Theme.Amber);
        }

        PaintLine(c, from, panel, accent);
        PaintLine(c, to, panel, accent);
        if (view.PlayheadMs is { } head)
        {
            var x = timeline.XOf(head);
            c.FillRect(new Box(x - 0.5f, panel.Y + 7, 1, panel.H - 8), Theme.Text);
            c.FillPolygon([(x - 5, panel.Y + 1), (x + 5, panel.Y + 1), (x, panel.Y + 8)], Theme.Text);
        }

        var axis = panel.Bottom + AxisGap;
        var gutter = count > 1 ? Gutter : 0;
        c.TextMid("0:00", Face.Mono, 12, panel.X, axis, Theme.Muted);
        c.TextCentered(AxisClock(view.DurationMs / 2), Face.Mono, 12, new Box(panel.X + gutter, axis - 10, panel.W - gutter, 20), Theme.Muted);
        c.TextRight(AxisClock(view.DurationMs), Face.Mono, 12, panel.Right, axis, Theme.Muted);
    }

    // A line over the whole wave, under its tab.
    private static void PaintLine(ICanvas c, float x, Box panel, uint color)
    {
        var line = new Box(x - 1, panel.Y + 1, 2, panel.H - 2);
        c.Glow(line, 4, 0.35f, color);
        c.FillRect(line, color);
    }
}

/// <summary>
/// A handle of the selection: a tab on its line that a mouse hits easily, and a stop of the keyboard. The arrows reach it through
/// the editor's own keys, which see Shift; the tabs stay outside the part kept, the start's to the left, the end's to the right.
/// </summary>
/// <param name="end">Which end it moves.</param>
/// <param name="timeline">The one rule of times and places, as the page lies now.</param>
/// <param name="at">Where this end is.</param>
/// <param name="move">Asks for this end here: the selection applies its rules.</param>
/// <param name="grab">Where the mouse took the tab, kept by the screen so a rebuild in the middle of a drag does not make it jump.</param>
internal sealed class EditorHandle(EditorEnd end, Func<EditorTimeline> timeline, Func<long> at, Action<long> move, StrongBox<float> grab) : Widget
{
    public EditorEnd End => end;

    public override bool CanFocus => Enabled;

    /// <summary>Where the mouse takes this handle when its line is at <paramref name="x"/>: the tab, and a little inside the part kept.</summary>
    public static Box HitBox(EditorEnd end, float x)
    {
        var panel = EditorWave.Panel;
        return end == EditorEnd.Start
            ? new Box(x - EditorWave.GripWidth, panel.Y, EditorWave.GripWidth + 6, panel.H)
            : new Box(x - 6, panel.Y, EditorWave.GripWidth + 6, panel.H);
    }

    private Box Tab
    {
        get
        {
            var panel = EditorWave.Panel;
            var x = timeline().XOf(at());
            return new Box(end == EditorEnd.Start ? x + 1 - EditorWave.GripWidth : x - 1, panel.MidY - EditorWave.GripHeight / 2, EditorWave.GripWidth, EditorWave.GripHeight);
        }
    }

    public override void OnPress(float x, float y) => grab.Value = x - timeline().XOf(at());

    public override void OnDrag(float x, float y)
    {
        move(timeline().MsAt(x - grab.Value));
        Bounds = HitBox(end, timeline().XOf(at()));
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        var tab = Tab;
        c.Glow(tab, 4, 0.35f, ui.Accent);
        c.FillRound(tab, 4, ui.Accent);
        c.Stroke(tab.MidX - 2.5f, tab.MidY - 9, tab.MidX - 2.5f, tab.MidY + 9, 1.5f, ui.OnAccent);
        c.Stroke(tab.MidX + 2.5f, tab.MidY - 9, tab.MidX + 2.5f, tab.MidY + 9, 1.5f, ui.OnAccent);
        if (ui.Focused(this))
        {
            ui.FocusOutline(c, tab, 4);
        }
    }
}

/// <summary>Takes the mouse where the dialog leaves the window only to be seen: the band and the side bar.</summary>
internal sealed class EditorShield : Widget
{
    public override void Paint(ICanvas c, UiState ui)
    {
    }
}
