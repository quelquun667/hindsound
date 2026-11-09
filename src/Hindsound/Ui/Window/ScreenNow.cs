using Hindsound.Audio;

namespace Hindsound.Ui.Window;

/// <summary>The window's side of the Now page. Kept out of ISettingsBackend, which test doubles implement.</summary>
internal interface INowPanel
{
    /// <summary>What the wave draws from. Read at each tick: it takes no lock and allocates nothing.</summary>
    WaveFacts WaveFacts { get; }
}

/// <summary>The Now page: what the memory holds, as it comes in.</summary>
internal sealed partial class SettingsScreen
{
    private const float RecordTop = 82, RecordLeft = Left + 60;

    private INowPanel? _now;
    private NowWave? _wave;
    private bool _minimized;
    private bool _waveAsleep;

    // Worked out once per build of the page: reading it from the settings parses text, and a tick allocates nothing.
    private uint _waveAccent;

    /// <summary>
    /// The Now page draws from this panel, and the window opens on it. Null, the default, leaves the side bar at the pages of
    /// the settings and the window on Hotkeys.
    /// </summary>
    public void SetNow(INowPanel? panel)
    {
        var arrives = _now is null && panel is not null;
        _now = panel;
        if (arrives && !IsCard)
        {
            ShowSection(Section.Now);
        }
        else if (panel is null && Section == Section.Now)
        {
            ShowSection(Section.Hotkeys);
        }
        else
        {
            Refresh();
        }
    }

    /// <summary>The second timer runs: on Now, with the window not minimized, unless time stands still and all is read.</summary>
    public bool WantsWave => _now is not null && Section == Section.Now && !_minimized && !_waveAsleep;

    /// <summary>The picture of the wave, for what wants to look at it. Null until the page has been shown.</summary>
    internal NowWave? Wave => _wave;

    private NavRows Rows => _now is null ? NavRows.SettingsOnly : NavRows.WithViews;

    /// <summary>Where the end of the first-launch card leaves the user.</summary>
    private Section FirstSection => _now is null ? Section.Hotkeys : Section.Now;

    // Time goes on while a source delivers nothing, so this is the only state in which the picture can stay still for good.
    private bool TimeStandsStill => _status?.Activity is CaptureActivity.Paused or CaptureActivity.Stopped or CaptureActivity.NoDevice;

    /// <summary>The window is minimized: nobody sees it, so it draws nothing until it comes back.</summary>
    public bool Minimized => _minimized;

    /// <summary>
    /// The window was minimized or came back. Nobody sees a minimized wave: the next tick after coming back catches up. What was not
    /// drawn meanwhile is drawn once on the way back, which this asks for as Windows may not.
    /// </summary>
    public void WindowMinimized(bool minimized)
    {
        if (minimized != _minimized)
        {
            _minimized = minimized;
            _host.WaveWanted(WantsWave);
            if (!minimized)
            {
                _host.Invalidate();
            }
        }
    }

    /// <summary>
    /// A tenth of a second has passed on the Now page. It reads a little of the memory, looks at what changed, and repaints only
    /// that: a full paint takes 34 to 45 ms, and at this pace that would be 40 % of a core for a picture that barely moves.
    /// </summary>
    public void WaveTick()
    {
        if (_now is not { } panel || Section != Section.Now)
        {
            return;
        }

        var wave = _wave ??= new NowWave();
        var facts = panel.WaveFacts;
        wave.Follow(facts);
        var change = wave.Look(_canvas.Scale, facts, Editor.Settings.ClipSeconds, _status);

        // After the pass, always: a lane that has not started says it has nothing left to read.
        if (TimeStandsStill && wave.CaughtUp)
        {
            _waveAsleep = true;
            _host.WaveWanted(false);
        }

        // Everything goes through one bitmap and the open list is drawn in it: drawing here would go over it. Its closing repaints all.
        if (PopupOpen || change == WaveChange.None)
        {
            return;
        }

        _canvas.SetOrigin(0);
        if ((change & WaveChange.Lanes) != 0)
        {
            var titles = (change & WaveChange.Titles) != 0;
            wave.PaintLanes(_canvas, _waveAccent, _status, titles);
            _host.InvalidateArea(titles ? NowWave.WaveArea : NowWave.BarsArea);
        }

        if ((change & WaveChange.SaveLine) != 0)
        {
            wave.PaintSaveLine(_canvas);
            _host.InvalidateArea(NowWave.SaveArea);
        }
    }

    // A change of status can set time going again, and any other news can change what the bars of a still picture mean.
    private void WakeWave() => _waveAsleep = false;

    private void BuildNow()
    {
        // Without this, it would keep the scrolling room of the page before it.
        _contentBottom = 0;
        _waveAccent = Editor.AccentRgb;
        RowLabel("Record", RecordTop);
        List("now.record", new Box(RecordLeft, RecordTop, FieldWidth, RowHeight), () => SettingsEditor.RecordChoices(_apps, Editor.Settings.Audio.SourceApp, _status is { SourceApp: not null, SourceAppMissing: false }), () => Editor.Settings.Audio.SourceApp ?? string.Empty, Editor.SetSourceApp);
        Add(new Painted((c, ui) =>
        {
            if (_now is { } panel)
            {
                (_wave ??= new NowWave()).PaintAll(c, ui.Accent, panel.WaveFacts, Editor.Settings.ClipSeconds, Editor.Settings.BufferSeconds, _status);
            }
        }));
    }
}
