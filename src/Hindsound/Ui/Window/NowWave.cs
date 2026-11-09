using Hindsound.Audio;

namespace Hindsound.Ui.Window;

/// <summary>What a look at the engine found different from what is on screen.</summary>
[Flags]
internal enum WaveChange
{
    None = 0,

    /// <summary>A bar, the kept part or the saved part. Nothing to write again: the commonest change by far.</summary>
    Lanes = 1,

    /// <summary>A title, or the line of a change of source and its words.</summary>
    Titles = 2,

    /// <summary>The line under the wave says something else.</summary>
    SaveLine = 4,
}

/// <summary>
/// The two lanes of the Now page: their summaries, the bars of the last look, and how they are drawn. It belongs to the screen, so
/// it goes with the window: closed, there is no array, no timer and no reading of the memory.
/// <para>
/// Everything a tick draws goes through here, and so does a full paint: the two cannot end up showing different things. At 10 looks a
/// second, most of them find nothing new: a bar is about 3.4 s of a 5:00 memory.
/// </para>
/// </summary>
internal sealed class NowWave
{
    private const float SystemTitleY = 128, SystemTop = 138, SystemHeight = 116, MicTitleY = 270, MicTop = 280, MicHeight = 66;
    private const float AxisY = 362, SaveY = 386, ZoneLineGap = 3, ZoneLineHeight = 2;

    // The share of each colour: a mix once per paint, no alpha anywhere, as the canvas blends nothing.
    private const float DimShare = 0.42f, ZoneGroundShare = 0.07f, HaloShare = 0.40f, GrayLightShare = 0.62f, FrozenGrayLightShare = 0.45f,
        GrayDarkShare = 0.24f, GrayHaloShare = 0.20f, FrozenBrightShare = 0.72f, ZoneLineSoftShare = 0.30f;

    private const string Filling = "Filling...";

    private static readonly Box SystemLane = new(SettingsScreen.Left, SystemTop, SettingsScreen.Width, SystemHeight);
    private static readonly Box MicLane = new(SettingsScreen.Left, MicTop, SettingsScreen.Width, MicHeight);

    private readonly WaveLane _systemLane = new(), _micLane = new();
    private double _scale;
    private WaveGrid _systemGrid, _micGrid;
    private byte[] _systemBars = [], _micBars = [], _nextSystem = [], _nextMic = [];
    private BarColumn[] _columns = [];
    private int _held, _zone;
    private WaveMarker _marker = WaveMarker.None;
    private bool _followed;

    private (int Kept, int Before) _seconds = (-1, -1);
    private int _saveClipSeconds;
    private bool _saveShown;
    private IReadOnlyList<SourceChange>? _saveChanges;
    private string _saveText = string.Empty;

    private AudioEngineStatus? _titleStatus;
    private bool _titleKnown, _titleMixed;
    private string _systemTitle = string.Empty;
    private string? _noCapture;
    private string? _markerText;
    private string? _markerApp;

    /// <summary>All a tick repaints for the bars: the two lanes and the line of the saved part. No words but those inside a lane.</summary>
    public static Box BarsArea { get; } = new(SettingsScreen.Left, SystemTop, SettingsScreen.Width, MicTop + MicHeight + 8 - SystemTop);

    /// <summary>The same with the titles over the lanes, and the line of a change with its words.</summary>
    public static Box WaveArea { get; } = new(SettingsScreen.Left, SystemTitleY - 10, SettingsScreen.Width, MicTop + MicHeight + 8 - (SystemTitleY - 10));

    /// <summary>All a tick repaints for the line under the wave.</summary>
    public static Box SaveArea { get; } = new(SettingsScreen.Left, SaveY - 10, SettingsScreen.Width, 20);

    /// <summary>How many bars each lane has at the present scale.</summary>
    public int Columns => _systemGrid.Columns;

    public ReadOnlySpan<byte> SystemBars => _systemBars;

    public ReadOnlySpan<byte> MicBars => _micBars;

    public int ZoneColumn => _zone;

    public WaveMarker Marker => _marker;

    /// <summary>What the line under the wave says. Built only when its seconds change.</summary>
    public string SaveText => _saveText;

    /// <summary>Nothing is left to read for now. Worth asking only after a pass: a lane that has not started says yes.</summary>
    public bool CaughtUp => _systemLane.IsCaughtUp && _micLane.IsCaughtUp;

    /// <summary>One pass over the memory. A pass is bounded, so a long memory fills in over a few ticks, from right to left.</summary>
    public void Follow(in WaveFacts facts)
    {
        WaveLane.CatchUp(facts, _systemLane, _micLane);
        _followed = true;
    }

    /// <summary>Works out the picture and says what changed. Draws nothing, and allocates nothing unless the words change.</summary>
    public WaveChange Look(double scale, in WaveFacts facts, int clipSeconds, AudioEngineStatus? status)
    {
        var change = UseScale(scale) ? WaveChange.Lanes | WaveChange.Titles : WaveChange.None;
        WaveView.Compose(_systemLane, facts, _nextSystem);
        WaveView.Compose(_micLane, facts, _nextMic);
        if (!_nextSystem.AsSpan().SequenceEqual(_systemBars))
        {
            (_systemBars, _nextSystem) = (_nextSystem, _systemBars);
            change |= WaveChange.Lanes;
        }

        if (!_nextMic.AsSpan().SequenceEqual(_micBars))
        {
            (_micBars, _nextMic) = (_nextMic, _micBars);
            change |= WaveChange.Lanes;
        }

        // Equal bars do not mean an equal picture: on a mute PC every bar is 0, while the line and the saved part slide.
        var columns = Columns;
        var held = WaveView.HeldFrom(facts, columns);
        var zone = WaveView.ZoneOf(clipSeconds, facts, columns);
        var marker = WaveView.MarkerOf(facts, columns);
        if (held != _held || zone != _zone)
        {
            _held = held;
            _zone = zone;
            change |= WaveChange.Lanes;
        }

        if (marker.Column != _marker.Column || marker.Mixed != _marker.Mixed || !ReferenceEquals(marker.App, _marker.App))
        {
            _marker = marker;
            change |= WaveChange.Lanes | WaveChange.Titles;
        }

        // Only on the first look after this status, or when the line comes or goes: this is what cuts the name of the exe out.
        if (!_titleKnown || !ReferenceEquals(status, _titleStatus) || marker.Shows != _titleMixed)
        {
            _titleKnown = true;
            _titleStatus = status;
            _titleMixed = marker.Shows;
            _systemTitle = WaveView.SystemLaneTitle(status, marker);
            _noCapture = WaveView.NoCaptureText(status);
            change |= WaveChange.Lanes | WaveChange.Titles;
        }

        if (marker.Shows && (_markerText is null || !ReferenceEquals(marker.App, _markerApp)))
        {
            _markerApp = marker.App;
            _markerText = WaveView.MarkerText(marker.App);
        }

        var seconds = WaveView.SaveSeconds(clipSeconds, facts);
        var shown = _noCapture is null || seconds.Kept > 0;
        if (seconds != _seconds || clipSeconds != _saveClipSeconds || shown != _saveShown || !ReferenceEquals(facts.Changes, _saveChanges))
        {
            var before = _saveText;
            _seconds = seconds;
            _saveClipSeconds = clipSeconds;
            _saveShown = shown;
            _saveChanges = facts.Changes;
            _saveText = shown ? WaveView.SaveLine(clipSeconds, facts) : string.Empty;
            if (_saveText != before)
            {
                change |= WaveChange.SaveLine;
            }
        }

        return change;
    }

    /// <summary>Everything, for a full paint of the page. The first one reads first, so the window does not open on empty lanes.</summary>
    public void PaintAll(ICanvas c, uint accent, in WaveFacts facts, int clipSeconds, int bufferSeconds, AudioEngineStatus? status)
    {
        if (!_followed)
        {
            Follow(facts);
        }

        Look(c.Scale, facts, clipSeconds, status);
        PaintLanes(c, accent, status, titles: true);
        PaintSaveLine(c);

        var activity = status?.Activity ?? CaptureActivity.Capturing;
        var axis = WaveView.AxisLabels(bufferSeconds, activity);
        var (left, width) = (SettingsScreen.Left, SettingsScreen.Width);
        c.TextMid(axis.Left, Face.Mono, 12, left, AxisY, Theme.Muted);
        c.TextCentered(axis.Middle, Face.Mono, 12, new Box(left, AxisY - 10, width, 20), Theme.Muted);
        c.TextRight(axis.Right, Face.Mono, 12, left + width, AxisY, activity == CaptureActivity.Paused ? Theme.Amber : Theme.Text);
    }

    /// <summary>The line under the wave and nothing else: what a tick does when only its seconds moved.</summary>
    public void PaintSaveLine(ICanvas c)
    {
        c.FillRect(SaveArea, Theme.Base);
        if (_saveText.Length > 0)
        {
            c.TextRight(c.Fit(_saveText, Face.Body, 14, SettingsScreen.Width), Face.Body, 14, SettingsScreen.Right, SaveY, Theme.Text);
        }
    }

    /// <summary><see cref="WaveArea"/> from the last look, or only <see cref="BarsArea"/> and what is under its words.</summary>
    public void PaintLanes(ICanvas c, uint accent, AudioEngineStatus? status, bool titles)
    {
        if (titles)
        {
            c.FillRect(WaveArea, Theme.Base);
        }

        var frozen = status?.Activity == CaptureActivity.Paused;
        var ground = Theme.Panel;
        var zoneGround = Theme.Mix(accent, ground, ZoneGroundShare);
        var look = new LaneColors(
            Theme.Mix(accent, ground, DimShare),
            frozen ? Theme.Mix(accent, ground, FrozenBrightShare) : accent,
            Theme.Mix(accent, zoneGround, HaloShare),
            Theme.Mix(Theme.Text, ground, frozen ? FrozenGrayLightShare : GrayLightShare),
            Theme.Mix(Theme.Text, ground, GrayDarkShare),
            Theme.Mix(Theme.Text, zoneGround, GrayHaloShare),
            frozen,
            zoneGround);

        if (titles)
        {
            c.TextMid(_systemTitle, Face.Medium, 13, SettingsScreen.Left, SystemTitleY, Theme.Muted);
            var micTitle = WaveView.MicLaneTitle(status);
            c.TextMid(micTitle, Face.Medium, 13, SettingsScreen.Left, MicTitleY, micTitle == WaveView.MicLost ? Theme.Amber : Theme.Muted);
        }

        var systemWords = _titleStatus is null || _systemLane.HasTrack && _held < Columns ? null : _noCapture;
        PaintLane(c, SystemLane, _systemGrid, _systemBars, _systemLane.HasTrack, _marker, look, systemWords, fills: true);
        PaintMarker(c, titles);

        PaintLane(c, MicLane, _micGrid, _micBars, _micLane.HasTrack, WaveMarker.None, look, WaveView.MicLaneText(status), fills: false);
        PaintZoneLine(c, accent, frozen);
    }

    // True when the columns had to be worked out again, which is a new picture.
    private bool UseScale(double scale)
    {
        if (scale == _scale)
        {
            return false;
        }

        _scale = scale;
        _systemGrid = WaveGrid.Of(SystemLane, scale);
        _micGrid = WaveGrid.Of(MicLane, scale);
        var columns = _systemGrid.Columns;
        _systemBars = new byte[columns];
        _micBars = new byte[columns];
        _nextSystem = new byte[columns];
        _nextMic = new byte[columns];
        _columns = new BarColumn[columns];
        return true;
    }

    private void PaintLane(ICanvas c, Box lane, in WaveGrid grid, byte[] bars, bool hasTrack, WaveMarker marker, in LaneColors look, string? words, bool fills)
    {
        // The frame's corners are blended: over themselves they would come out darker at each repaint.
        c.FillRect(lane, Theme.Base);
        c.Frame(lane, 4, Theme.Base, Theme.Line);
        var s = (float)_scale;
        var inner = lane.Inflate(-1);
        var innerRight = WaveGrid.Px(inner.Right, _scale);
        var innerLeft = WaveGrid.Px(inner.X, _scale);
        var columns = grid.Columns;
        var held = hasTrack ? _held : columns;

        Tint(c, grid, inner, innerLeft, innerRight, held, Theme.Panel, s);
        Tint(c, grid, inner, innerLeft, innerRight, _zone, look.ZoneGround, s);

        if (words is not null)
        {
            c.TextCentered(words, Face.Medium, 13, lane, Theme.Muted);
            return;
        }

        // Nothing kept here yet: dots, not a line, so it does not read as silence.
        var keptFrom = hasTrack ? Edge(grid, held, innerLeft + WaveGrid.Px(3, _scale), innerRight) : innerRight;
        if (hasTrack && held > 0)
        {
            var dot = WaveGrid.Px(2, _scale);
            var step = WaveGrid.Px(6, _scale);
            var stop = keptFrom - WaveGrid.Px(3, _scale);
            for (var x = innerLeft + WaveGrid.Px(5, _scale); x + dot < stop; x += step)
            {
                c.FillRect(new Box(x / s, grid.Mid / s, dot / s, grid.Line / s), Theme.Line);
            }

            var space = keptFrom / s - inner.X;
            if (fills && space > c.Measure(Filling, Face.Body, 12) + 40)
            {
                var width = c.Measure(Filling, Face.Body, 12);
                var box = new Box(inner.X + (space - width) / 2 - 8, lane.MidY - 10, width + 16, 20);
                c.FillRect(box, Theme.Base);
                c.TextCentered(Filling, Face.Body, 12, box, Theme.Faded);
            }
        }

        // The base line runs under the bars too, from where the past has been read so far.
        var first = 0;
        while (first < columns && bars[first] == WaveLane.Empty)
        {
            first++;
        }

        if (first < columns)
        {
            var from = first == 0 ? innerLeft + WaveGrid.Px(3, _scale) : grid.X(first) - (grid.Gap + 1) / 2;
            c.FillRect(new Box(from / s, grid.Mid / s, (grid.End - from) / s, grid.Line / s), Theme.Line);
        }

        for (var column = 0; column < columns; column++)
        {
            _columns[column] = look.Of(WaveView.ToneOf(column, _zone, marker));
        }

        c.Bars(grid, bars, _columns, Theme.Line);
    }

    // From a column to the right edge of the inside of the lane: the kept part, then the saved part over it.
    private static void Tint(ICanvas c, in WaveGrid grid, Box inner, int innerLeft, int innerRight, int column, uint rgb, float s)
    {
        if (column >= grid.Columns)
        {
            return;
        }

        var from = column <= 0 ? innerLeft : grid.X(column) - (grid.Gap + 1) / 2;
        c.FillRound(new Box(from / s, inner.Y, (innerRight - from) / s, inner.H), 3, rgb);
        if (column > 0)
        {
            c.FillRect(new Box(from / s, inner.Y, 4, inner.H), rgb);
        }
    }

    private static int Edge(in WaveGrid grid, int column, int first, int none) =>
        column <= 0 ? first : column >= grid.Columns ? none : grid.X(column) - (grid.Gap + 1) / 2;

    // The line never crosses the title of the lane, and the words go on whichever side has room.
    private void PaintMarker(ICanvas c, bool withWords)
    {
        if (!_marker.Shows || _markerText is not { } text)
        {
            return;
        }

        var s = (float)_scale;
        var grid = _systemGrid;
        var x = (_marker.FirstNewColumn >= grid.Columns ? grid.End : grid.X(_marker.FirstNewColumn) - (grid.Gap + 1) / 2) / s;
        var titleRight = SettingsScreen.Left + c.Measure(_systemTitle, Face.Medium, 13) + 12;
        var width = c.Measure(text, Face.Body, 13);
        var top = x < titleRight ? SystemLane.Y : SystemTitleY - 1;
        c.FillRect(new Box(x, top, 1 / s, SystemLane.Bottom - top), Theme.Text);
        if (!withWords)
        {
            return;
        }

        if (x - 6 - width >= titleRight)
        {
            c.TextRight(text, Face.Body, 13, x - 6, SystemTitleY, Theme.Text);
            return;
        }

        var start = MathF.Max(x + 6, titleRight);
        var room = SettingsScreen.Right - start;
        c.TextMid(width <= room ? text : c.Fit(text, Face.Body, 13, room), Face.Body, 13, start, SystemTitleY, Theme.Text);
    }

    // The saved part, under both lanes. A soft row each side in place of the mock's glow, which cost a blur at each move of the line.
    private void PaintZoneLine(ICanvas c, uint accent, bool frozen)
    {
        c.FillRect(new Box(SettingsScreen.Left, MicLane.Bottom, SettingsScreen.Width, BarsArea.Bottom - MicLane.Bottom), Theme.Base);
        if (_zone >= Columns)
        {
            return;
        }

        var s = (float)_scale;
        var grid = _micGrid;
        var from = (_zone <= 0 ? grid.X(0) : grid.X(_zone) - (grid.Gap + 1) / 2) / s;
        var line = new Box(from, MicLane.Bottom + ZoneLineGap, grid.End / s - from, ZoneLineHeight);
        if (frozen)
        {
            c.FillRect(line, Theme.Mix(accent, Theme.Base, FrozenBrightShare));
            return;
        }

        var soft = Theme.Mix(accent, Theme.Base, ZoneLineSoftShare);
        c.FillRect(new Box(line.X, line.Y - 1, line.W, line.H + 2), soft);
        c.FillRect(line, accent);
    }

    private readonly record struct LaneColors(uint Dim, uint Bright, uint Halo, uint GrayLight, uint GrayDark, uint GrayHalo, bool Frozen, uint ZoneGround)
    {
        public BarColumn Of(WaveTone tone) => tone switch
        {
            WaveTone.Accent => Frozen ? new BarColumn(Bright) : new BarColumn(Bright, Halo),
            WaveTone.GrayLight => Frozen ? new BarColumn(GrayLight) : new BarColumn(GrayLight, GrayHalo),
            WaveTone.GrayDark => new BarColumn(GrayDark),
            _ => new BarColumn(Dim),
        };
    }
}
