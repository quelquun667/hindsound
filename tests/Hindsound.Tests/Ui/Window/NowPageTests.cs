using Hindsound.Audio;
using Hindsound.Tests.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The Now page on a real bitmap and real tracks, with a host that says what was asked of the window.</summary>
internal sealed unsafe class NowRig : IDisposable
{
    public const int Rate = InternalFormat.SampleRate;
    public const int MemorySeconds = 60;
    public const int Memory = MemorySeconds * Rate;

    public static readonly Box SystemLane = new(SettingsScreen.Left, 138, SettingsScreen.Width, 116);
    public static readonly Box MicLane = new(SettingsScreen.Left, 280, SettingsScreen.Width, 66);

    private readonly ScreenRig _rig = new();

    public NowRig(WaveFacts facts, AudioEngineStatus? status = null, double scale = 1.0, bool directWrite = false)
    {
        var width = (int)Math.Round(SettingsScreen.WindowWidth * scale);
        var height = (int)Math.Round(SettingsScreen.WindowHeight * scale);
        Canvas = directWrite ? new GdiCanvas(width, height, scale) : new GdiCanvas(width, height, scale, null);
        Panel = new NowPanel { Facts = facts };
        Screen = new SettingsScreen(_rig.Settings, _rig.Backend, Host, Canvas);
        Screen.UpdateStatus(status ?? ScreenRig.Capturing());
        Screen.SetNow(Panel);
    }

    public WaveHost Host { get; } = new();

    public NowPanel Panel { get; }

    public GdiCanvas Canvas { get; }

    public SettingsScreen Screen { get; }

    public NowWave Wave => Screen.Wave!;

    public uint Accent => Screen.Editor.AccentRgb;

    public WaveGrid SystemGrid => WaveGrid.Of(SystemLane, Canvas.Scale);

    public WaveGrid MicGrid => WaveGrid.Of(MicLane, Canvas.Scale);

    public static float[] Peaks(int seconds, float peak)
    {
        var peaks = new float[seconds * Rate / WaveLane.FramesPerSlice];
        Array.Fill(peaks, peak);
        return peaks;
    }

    public static SourceTrack Loud(int seconds) => NowSamples.Track(Peaks(seconds, 0.5f));

    /// <summary>What the memory holds: <paramref name="seconds"/> of it, the whole window on the timeline from 0.</summary>
    public static WaveFacts Held(SourceTrack? system, SourceTrack? mic, double seconds, params SourceChange[] changes) =>
        new(system, mic, 0, (long)(seconds * Rate), Memory, changes);

    /// <summary>A full memory, ending at this second.</summary>
    public static WaveFacts FullUntil(SourceTrack? system, SourceTrack? mic, double seconds, params SourceChange[] changes)
    {
        var end = (long)(seconds * Rate);
        return new WaveFacts(system, mic, end - Memory, end, Memory, changes);
    }

    /// <summary>The first paint of the page, after which only what changes counts.</summary>
    public void Settle()
    {
        Screen.Paint(Canvas);
        Screen.WaveTick();
        Host.Reset();
    }

    public uint At(int x, int y) => Canvas.Bits[y * Canvas.Width + x] & 0xFFFFFF;

    public uint[] Picture()
    {
        var copy = new uint[Canvas.Width * Canvas.Height];
        new ReadOnlySpan<uint>(Canvas.Bits, copy.Length).CopyTo(copy);
        return copy;
    }

    /// <summary>Pixels that differ, outside of these areas.</summary>
    public int ChangedOutside(uint[] before, params Box[] areas)
    {
        var picture = Picture();
        var changed = 0;
        for (var y = 0; y < Canvas.Height; y++)
        {
            for (var x = 0; x < Canvas.Width; x++)
            {
                if (picture[y * Canvas.Width + x] != before[y * Canvas.Width + x] && !areas.Any(a => a.Contains(x / (float)Canvas.Scale, y / (float)Canvas.Scale)))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>Pixels of a box close to a colour: enough to tell an amber word from a grey one, whatever the smoothing of the text.</summary>
    public int Near(uint rgb, Box box, int tolerance = 60)
    {
        var count = 0;
        for (var y = (int)box.Y; y < (int)box.Bottom; y++)
        {
            for (var x = (int)box.X; x < (int)box.Right; x++)
            {
                var pixel = At(x, y);
                if (Math.Abs((int)((pixel >> 16) & 0xFF) - (int)((rgb >> 16) & 0xFF)) <= tolerance
                    && Math.Abs((int)((pixel >> 8) & 0xFF) - (int)((rgb >> 8) & 0xFF)) <= tolerance
                    && Math.Abs((int)(pixel & 0xFF) - (int)(rgb & 0xFF)) <= tolerance)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public void Dispose()
    {
        Canvas.Dispose();
        _rig.Dispose();
    }
}

internal sealed class NowPanel : INowPanel
{
    public WaveFacts Facts { get; set; }

    public int Reads { get; private set; }

    public WaveFacts WaveFacts
    {
        get
        {
            Reads++;
            return Facts;
        }
    }
}

internal sealed class WaveHost : IWindowHost
{
    public int Invalidated { get; private set; }

    // Room in advance: the allocation tests count what a tick allocates, not this list growing.
    public List<Box> Areas { get; } = new(4096);

    public bool WaveOn { get; private set; }

    public void Reset()
    {
        Invalidated = 0;
        Areas.Clear();
    }

    public void Invalidate() => Invalidated++;

    public void InvalidateArea(Box area) => Areas.Add(area);

    public void WaveWanted(bool wanted) => WaveOn = wanted;

    public void ShowEdit(Box box, string text, Face face, float size, bool resetText)
    {
    }

    public void HideEdit()
    {
    }

    public bool IsForeground => false;

    public void FocusEdit()
    {
    }

    public void CaptureMouse()
    {
    }

    public void ReleaseMouse()
    {
    }

    public void MeterWanted(bool wanted)
    {
    }

    public string? PickFolder(string startFolder) => null;

    public void CloseWindow()
    {
    }
}

public class NowPageTimerTests
{
    private static readonly AudioEngineStatus Paused = ScreenRig.Capturing(activity: CaptureActivity.Paused);

    [Fact]
    public void It_runs_on_the_page_and_stops_on_another_and_comes_back()
    {
        using var rig = new NowRig(default);
        Assert.True(rig.Host.WaveOn);

        rig.Screen.ShowSection(Section.Audio);
        Assert.False(rig.Host.WaveOn);

        rig.Screen.ShowSection(Section.Now);
        Assert.True(rig.Host.WaveOn);
    }

    [Fact]
    public void Minimized_it_stops_and_coming_back_it_goes_on()
    {
        using var rig = new NowRig(default);

        rig.Screen.WindowMinimized(true);
        Assert.False(rig.Host.WaveOn);

        rig.Screen.WindowMinimized(false);
        Assert.True(rig.Host.WaveOn);
    }

    [Fact]
    public void Coming_back_from_minimized_on_another_page_starts_nothing()
    {
        using var rig = new NowRig(default);
        rig.Screen.ShowSection(Section.Export);

        rig.Screen.WindowMinimized(true);
        rig.Screen.WindowMinimized(false);

        Assert.False(rig.Host.WaveOn);
    }

    [Fact]
    public void No_panel_no_timer_and_nothing_read()
    {
        using var rig = new NowRig(default);
        var reads = rig.Panel.Reads;
        rig.Screen.ShowSection(Section.General);

        rig.Screen.WaveTick();

        Assert.Equal(reads, rig.Panel.Reads);
        rig.Screen.SetNow(null);
        Assert.False(rig.Host.WaveOn);
    }

    [Theory]
    [InlineData(CaptureActivity.Paused)]
    [InlineData(CaptureActivity.Stopped)]
    [InlineData(CaptureActivity.NoDevice)]
    public void When_time_stands_still_it_stops_once_all_is_read(CaptureActivity activity)
    {
        using var rig = new NowRig(NowRig.Held(NowRig.Loud(20), null, 20), ScreenRig.Capturing(activity: activity));
        Assert.True(rig.Host.WaveOn);

        rig.Screen.WaveTick();

        Assert.False(rig.Host.WaveOn);
        Assert.NotEqual(WaveLane.Empty, rig.Wave.SystemBars[^1]);
    }

    [Fact]
    public void A_pass_comes_before_asking_if_all_is_read()
    {
        // Four lanes' worth over the budget of one pass: the past of a long memory takes several ticks.
        var seconds = (int)(WaveLane.PassSamples / NowRig.Rate) + 60;
        var track = new SourceTrack(2, seconds * NowRig.Rate, 0);
        var packet = new float[2 * WaveLane.FramesPerSlice];
        for (var i = 0; i < seconds * 10; i++)
        {
            track.Append((long)i * WaveLane.FramesPerSlice, packet, exact: true);
        }

        var facts = new WaveFacts(track, null, 0, (long)seconds * NowRig.Rate, seconds * NowRig.Rate, []);
        using var rig = new NowRig(facts, Paused);

        rig.Screen.WaveTick();
        Assert.True(rig.Host.WaveOn);

        rig.Screen.WaveTick();
        Assert.True(rig.Host.WaveOn);

        for (var i = 0; i < 10 && rig.Host.WaveOn; i++)
        {
            rig.Screen.WaveTick();
        }

        Assert.False(rig.Host.WaveOn);
    }

    [Fact]
    public void A_capture_that_goes_on_never_stops_it_even_with_all_read()
    {
        using var rig = new NowRig(NowRig.Held(NowRig.Loud(5), null, 5));

        for (var i = 0; i < 5; i++)
        {
            rig.Screen.WaveTick();
        }

        Assert.True(rig.Wave.CaughtUp);
        Assert.True(rig.Host.WaveOn);
    }

    [Fact]
    public void A_change_of_status_starts_it_again()
    {
        using var rig = new NowRig(NowRig.Held(NowRig.Loud(5), null, 5), Paused);
        rig.Screen.WaveTick();
        Assert.False(rig.Host.WaveOn);

        rig.Screen.UpdateStatus(ScreenRig.Capturing());

        Assert.True(rig.Host.WaveOn);
    }

    [Fact]
    public void Still_paused_after_a_status_it_stops_again_after_one_tick()
    {
        using var rig = new NowRig(NowRig.Held(NowRig.Loud(5), null, 5), Paused);
        rig.Screen.WaveTick();

        rig.Screen.UpdateStatus(Paused with { Buffered = TimeSpan.FromSeconds(5) });
        Assert.True(rig.Host.WaveOn);

        rig.Screen.WaveTick();
        Assert.False(rig.Host.WaveOn);
    }

    [Fact]
    public void Coming_back_to_the_page_looks_again_even_after_stopping()
    {
        using var rig = new NowRig(NowRig.Held(NowRig.Loud(5), null, 5), Paused);
        rig.Screen.WaveTick();
        rig.Screen.ShowSection(Section.Durations);

        rig.Screen.ShowSection(Section.Now);

        Assert.True(rig.Host.WaveOn);
    }
}

public class NowPageTickTests
{
    private static NowRig Rig(double seconds = 20, params SourceChange[] changes) =>
        new(NowRig.Held(NowRig.Loud(10), null, seconds, changes));

    [Fact]
    public void A_tick_with_no_change_asks_nothing_of_the_window()
    {
        using var rig = Rig();
        rig.Settle();
        var before = rig.Picture();

        rig.Screen.WaveTick();

        Assert.Equal(0, rig.Host.Invalidated);
        Assert.Empty(rig.Host.Areas);
        Assert.Equal(0, rig.ChangedOutside(before));
    }

    [Fact]
    public void A_tick_with_a_change_draws_only_its_area_and_never_a_full_paint()
    {
        using var rig = Rig();
        rig.Settle();
        var before = rig.Picture();

        rig.Panel.Facts = NowRig.Held(rig.Panel.Facts.System, null, 26);
        rig.Screen.WaveTick();

        Assert.Equal(0, rig.Host.Invalidated);
        Assert.Contains(NowWave.BarsArea, rig.Host.Areas);
        Assert.DoesNotContain(NowWave.WaveArea, rig.Host.Areas);
        Assert.NotEqual(0, rig.ChangedOutside(before, NowWave.SaveArea));
        Assert.Equal(0, rig.ChangedOutside(before, NowWave.BarsArea, NowWave.SaveArea));
    }

    [Fact]
    public void The_partial_drawing_and_a_full_paint_give_the_same_picture()
    {
        using var rig = Rig();
        rig.Settle();

        rig.Panel.Facts = NowRig.Held(rig.Panel.Facts.System, null, 26);
        rig.Screen.WaveTick();
        var partial = rig.Picture();
        rig.Screen.Paint(rig.Canvas);

        Assert.Equal(0, rig.ChangedOutside(partial));
    }

    [Fact]
    public void A_tick_with_the_list_open_leaves_the_bitmap_alone_and_its_closing_repaints_all()
    {
        using var rig = Rig();
        rig.Settle();
        rig.Screen.Key(Vk.Tab, 0);
        for (var i = 0; i < 6 && rig.Screen.FocusId != "now.record"; i++)
        {
            rig.Screen.Key(Vk.Tab, 0);
        }

        rig.Screen.Key(Vk.Space, 0);
        Assert.True(rig.Screen.PopupOpen);
        rig.Screen.Paint(rig.Canvas);
        var open = rig.Picture();
        rig.Host.Reset();

        rig.Panel.Facts = NowRig.Held(rig.Panel.Facts.System, null, 26);
        rig.Screen.WaveTick();

        Assert.Equal(0, rig.ChangedOutside(open));
        Assert.Empty(rig.Host.Areas);
        Assert.Equal(0, rig.Host.Invalidated);
        Assert.Equal("Save: last 0:26 of 0:30", rig.Wave.SaveText);

        rig.Screen.Key(Vk.Escape, 0);

        Assert.False(rig.Screen.PopupOpen);
        Assert.NotEqual(0, rig.Host.Invalidated);
    }

    [Fact]
    public void Only_the_seconds_moved_only_the_line_is_drawn()
    {
        // No track: every bar is 0 and stays 0, and on a 5:00 memory 12.1 s to 13.1 s stays on the same column.
        const int memory = 300 * NowRig.Rate;
        using var rig = new NowRig(new WaveFacts(null, null, 0, (long)(12.1 * NowRig.Rate), memory, []));
        rig.Settle();
        var before = rig.Picture();
        var columns = (rig.Wave.ZoneColumn, rig.Wave.Marker);

        rig.Panel.Facts = new WaveFacts(null, null, 0, (long)(13.1 * NowRig.Rate), memory, []);
        rig.Screen.WaveTick();

        Assert.Equal(columns, (rig.Wave.ZoneColumn, rig.Wave.Marker));
        Assert.Equal([NowWave.SaveArea], rig.Host.Areas);
        Assert.Equal(0, rig.Host.Invalidated);
        Assert.Equal("Save: last 0:13 of 0:30", rig.Wave.SaveText);
        Assert.Equal(0, rig.ChangedOutside(before, NowWave.SaveArea));
        Assert.NotEqual(0, rig.ChangedOutside(before));
    }

    [Fact]
    public void The_words_are_not_built_again_while_the_seconds_stay()
    {
        using var rig = new NowRig(NowRig.Held(null, null, 12.1));
        rig.Settle();
        var text = rig.Wave.SaveText;

        rig.Panel.Facts = NowRig.Held(null, null, 12.3);
        rig.Screen.WaveTick();

        Assert.Same(text, rig.Wave.SaveText);
    }

    [Fact]
    public void With_the_memory_full_the_words_follow_time_alone()
    {
        var silent = new SourceChange(95 * NowRig.Rate, "Discord.exe");
        using var rig = new NowRig(NowRig.FullUntil(null, null, 100, silent));
        rig.Settle();
        Assert.Equal("Save: last 0:30 (0:25 from before Discord)", rig.Wave.SaveText);

        rig.Panel.Facts = NowRig.FullUntil(null, null, 110, silent);
        rig.Screen.WaveTick();
        Assert.Equal("Save: last 0:30 (0:15 from before Discord)", rig.Wave.SaveText);

        rig.Panel.Facts = NowRig.FullUntil(null, null, 126, silent);
        rig.Screen.WaveTick();
        Assert.Equal("Save: last 0:30", rig.Wave.SaveText);
    }

    [Fact]
    public void On_a_mute_PC_the_line_and_the_saved_part_slide_with_all_the_bars_at_0()
    {
        var change = new SourceChange(95 * NowRig.Rate, "Discord.exe");
        using var rig = new NowRig(NowRig.FullUntil(null, null, 100, change));
        rig.Settle();
        var marker = rig.Wave.Marker.Column;
        Assert.All(rig.Wave.SystemBars.ToArray(), b => Assert.Equal(0, b));

        rig.Panel.Facts = NowRig.FullUntil(null, null, 104, change);
        rig.Screen.WaveTick();

        Assert.True(rig.Wave.Marker.Column < marker);
        Assert.All(rig.Wave.SystemBars.ToArray(), b => Assert.Equal(0, b));
        Assert.Contains(NowWave.WaveArea, rig.Host.Areas);
    }

    [Fact]
    public void A_change_of_scale_gives_a_new_grid_and_a_full_paint()
    {
        using var rig = Rig();
        rig.Settle();
        Assert.Equal(87, rig.Wave.Columns);

        using var bigger = new GdiCanvas(1140, 732, 1.5, null);
        rig.Screen.SetCanvas(bigger);

        Assert.NotEqual(0, rig.Host.Invalidated);
        rig.Host.Reset();
        rig.Screen.Paint(bigger);
        Assert.Equal(78, rig.Wave.Columns);

        rig.Screen.WaveTick();
        Assert.Empty(rig.Host.Areas);
    }

    [Fact]
    public void A_memory_of_one_second_draws_without_trouble()
    {
        var track = NowRig.Loud(1);
        using var rig = new NowRig(new WaveFacts(track, null, 0, NowRig.Rate, NowRig.Rate, []));
        rig.Settle();

        rig.Screen.WaveTick();

        Assert.Contains(rig.Wave.SystemBars.ToArray(), b => b is not 0 and not WaveLane.Empty);
        Assert.Equal("Save: last 0:01 of 0:30", rig.Wave.SaveText);
    }

    [Fact]
    public void The_list_on_the_page_is_the_setting_of_the_audio_page()
    {
        using var rig = Rig();
        rig.Screen.SetChoices([], [], [new AudioAppChoice("Discord.exe", "Discord")]);
        for (var i = 0; i < 6 && rig.Screen.FocusId != "now.record"; i++)
        {
            rig.Screen.Key(Vk.Tab, 0);
        }

        rig.Screen.Key(Vk.Space, 0);
        rig.Screen.Key(Vk.End, 0);
        rig.Screen.Key(Vk.Enter, 0);

        Assert.Equal("Discord.exe", rig.Screen.Editor.Settings.Audio.SourceApp);
        rig.Screen.ShowSection(Section.Audio);
        Assert.Equal(1, Assert.IsType<Dropdown>(rig.Screen.WidgetById("audio.record")).SelectedIndex);
    }
}

public class NowPagePictureTests
{
    private static uint Dim(NowRig rig) => Theme.Mix(rig.Accent, Theme.Panel, 0.42f);

    private static uint ZoneGround(NowRig rig) => Theme.Mix(rig.Accent, Theme.Panel, 0.07f);

    private static NowRig Painted(WaveFacts facts, AudioEngineStatus? status = null, double scale = 1.0)
    {
        var rig = new NowRig(facts, status, scale);
        rig.Settle();
        return rig;
    }

    private static uint BarAt(NowRig rig, in WaveGrid grid, int column) => rig.At(grid.X(column) + grid.Bar / 2, grid.Mid - 2);

    private static uint HaloAt(NowRig rig, in WaveGrid grid, int column) => rig.At(grid.X(column) - 1, grid.Mid - 2);

    [Fact]
    public void Both_lanes_show_the_saved_part_bright_with_a_halo_and_the_rest_dim_without()
    {
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), NowRig.Loud(60), 60));
        var grid = rig.SystemGrid;
        var mic = rig.MicGrid;

        Assert.Equal(rig.Accent, BarAt(rig, grid, 86));
        Assert.Equal(rig.Accent, BarAt(rig, mic, 86));
        Assert.Equal(Dim(rig), BarAt(rig, grid, 5));
        Assert.Equal(Dim(rig), BarAt(rig, mic, 5));
        Assert.Equal(Theme.Mix(rig.Accent, ZoneGround(rig), 0.40f), HaloAt(rig, grid, 86));
        Assert.Equal(Theme.Panel, HaloAt(rig, grid, 5));
    }

    [Fact]
    public void The_two_greys_say_the_past_from_before_the_change_in_and_out_of_the_saved_part()
    {
        var change = new SourceChange(50 * NowRig.Rate, "Discord.exe");
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), NowRig.Loud(60), 60, change));
        var grid = rig.SystemGrid;
        var zone = rig.Wave.ZoneColumn;
        var line = rig.Wave.Marker.Column;
        Assert.InRange(line, zone + 2, 84);

        Assert.Equal(Theme.Mix(Theme.Text, Theme.Panel, 0.24f), BarAt(rig, grid, 5));
        Assert.Equal(Theme.Mix(Theme.Text, Theme.Panel, 0.62f), BarAt(rig, grid, zone + 1));
        Assert.Equal(rig.Accent, BarAt(rig, grid, 86));

        // The microphone has no mixed past.
        Assert.Equal(Dim(rig), BarAt(rig, rig.MicGrid, 5));
        Assert.Equal(rig.Accent, BarAt(rig, rig.MicGrid, zone + 1));
    }

    [Fact]
    public void Paused_the_saved_part_is_dimmed_with_no_halo_and_the_axis_says_so_in_amber()
    {
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), NowRig.Loud(60), 60), ScreenRig.Capturing(activity: CaptureActivity.Paused));
        var grid = rig.SystemGrid;

        Assert.Equal(Theme.Mix(rig.Accent, Theme.Panel, 0.72f), BarAt(rig, grid, 86));
        Assert.Equal(ZoneGround(rig), HaloAt(rig, grid, 86));
        Assert.Equal(Theme.Mix(rig.Accent, Theme.Base, 0.72f), rig.At(grid.End - 2, 349));
        Assert.NotEqual(0, rig.Near(Theme.Amber, new Box(680, 354, 48, 16)));
    }

    [Fact]
    public void Not_paused_the_axis_stays_out_of_the_amber()
    {
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), NowRig.Loud(60), 60));

        Assert.Equal(0, rig.Near(Theme.Amber, new Box(680, 354, 48, 16)));
        Assert.Equal(rig.Accent, rig.At(rig.SystemGrid.End - 2, 349));
    }

    [Fact]
    public void Mic_off_says_so_in_the_middle_with_no_base_line_and_keeps_the_saved_part()
    {
        var status = ScreenRig.Capturing(false, micEnabled: false);
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), null, 60), status);
        var mic = rig.MicGrid;

        Assert.Equal(Theme.Base, rig.At(mic.X(5), mic.Mid));
        Assert.Equal(ZoneGround(rig), rig.At(mic.X(86), mic.Mid));
        Assert.NotEqual(0, rig.Near(Theme.Muted, new Box(NowRig.MicLane.MidX - 30, mic.Mid - 8, 60, 16)));
    }

    [Fact]
    public void Mic_on_with_nothing_captured_yet_is_a_flat_line()
    {
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), null, 60));
        var mic = rig.MicGrid;

        Assert.Equal(Theme.Line, rig.At(mic.X(5), mic.Mid));
    }

    [Fact]
    public void A_lost_mic_keeps_its_past_and_goes_amber_in_its_title()
    {
        var lost = NowRig.Loud(20);
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), lost, 60), ScreenRig.Capturing(false));
        var mic = rig.MicGrid;

        Assert.NotEqual(0, rig.Near(Theme.Amber, new Box(200, 262, 60, 16)));
        Assert.Equal(Dim(rig), BarAt(rig, mic, 5));
        Assert.Equal(Theme.Line, rig.At(mic.X(86), mic.Mid));
        Assert.Equal(0, rig.Wave.MicBars[86]);
    }

    [Fact]
    public void A_working_mic_is_not_amber()
    {
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), NowRig.Loud(60), 60));

        Assert.Equal(0, rig.Near(Theme.Amber, new Box(200, 262, 60, 16)));
    }

    [Fact]
    public void Filling_the_part_not_held_has_dots_and_the_word_and_is_not_a_line()
    {
        using var rig = Painted(NowRig.Held(NowRig.Loud(10), NowRig.Loud(10), 10));
        var grid = rig.SystemGrid;
        var mid = grid.Mid;
        var lit = 0;
        var dark = 0;
        for (var x = grid.X(0); x < grid.X(10); x++)
        {
            if (rig.At(x, mid) == Theme.Line)
            {
                lit++;
            }
            else if (rig.At(x, mid) == Theme.Base)
            {
                dark++;
            }
        }

        Assert.True(lit > 0 && dark > 0, $"{lit} on, {dark} off");
        Assert.NotEqual(0, rig.Near(Theme.Faded, new Box(grid.X(30), mid - 8, grid.X(60) - grid.X(30), 16), 30));

        Assert.Equal(WaveLane.Empty, rig.Wave.SystemBars[0]);
    }

    [Fact]
    public void An_app_that_closed_is_a_base_line_and_not_a_hole()
    {
        using var rig = Painted(NowRig.Held(NowRig.Loud(30), NowRig.Loud(60), 60));
        var grid = rig.SystemGrid;

        for (var column = 60; column < 87; column++)
        {
            Assert.Equal(0, rig.Wave.SystemBars[column]);
            Assert.Equal(Theme.Line, rig.At(grid.X(column), grid.Mid));
            Assert.Equal(Theme.Line, rig.At(grid.X(column) - 1, grid.Mid));
        }
    }

    [Fact]
    public void No_device_says_so_in_the_PC_lane_with_no_line_and_no_save_words()
    {
        var status = ScreenRig.Capturing(false, CaptureActivity.NoDevice) with { SystemCapturing = false, Buffered = TimeSpan.Zero };
        using var rig = Painted(NowRig.Held(null, null, 0), status);
        var grid = rig.SystemGrid;

        Assert.Equal(Theme.Base, rig.At(grid.X(5), grid.Mid));
        Assert.NotEqual(0, rig.Near(Theme.Muted, new Box(NowRig.SystemLane.MidX - 40, grid.Mid - 8, 80, 16)));
        Assert.Equal(string.Empty, rig.Wave.SaveText);
        Assert.Equal(Theme.Base, rig.At(720, 386));
        Assert.Equal(string.Empty, StatusText.From(status, 300).Mic);
    }

    [Fact]
    public void The_device_lost_after_keeping_something_still_shows_it_and_says_it_can_be_saved()
    {
        var status = ScreenRig.Capturing(false, CaptureActivity.NoDevice) with { SystemCapturing = false };
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), null, 60), status);

        Assert.Equal(rig.Accent, BarAt(rig, rig.SystemGrid, 86));
        Assert.Equal("Save: last 0:30", rig.Wave.SaveText);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void At_every_scale_the_page_draws_on_the_grid(double scale)
    {
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), NowRig.Loud(60), 60), null, scale);
        var grid = rig.SystemGrid;

        Assert.Equal(grid.Columns, rig.Wave.Columns);
        Assert.Equal(rig.Accent, BarAt(rig, grid, grid.Columns - 1));
        Assert.Equal(rig.Accent, rig.At(grid.X(grid.Columns - 1), grid.Mid - grid.Half / 4));
    }

    [Fact]
    public void The_line_of_a_change_runs_up_to_the_row_of_the_title_when_it_is_clear_of_it()
    {
        var change = new SourceChange(50 * NowRig.Rate, "Discord.exe");
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), NowRig.Loud(60), 60, change));
        var grid = rig.SystemGrid;
        var x = grid.X(rig.Wave.Marker.Column) - (grid.Gap + 1) / 2;

        Assert.Equal(Theme.Text, rig.At(x, 128));
        Assert.Equal(Theme.Text, rig.At(x, grid.Mid - 20));
    }

    [Fact]
    public void Near_the_title_the_line_stops_at_the_top_of_the_lane_and_the_words_go_on_its_right()
    {
        var change = new SourceChange(1 * NowRig.Rate, null);
        using var rig = Painted(NowRig.Held(NowRig.Loud(60), NowRig.Loud(60), 60, change));
        var grid = rig.SystemGrid;
        var x = grid.X(rig.Wave.Marker.Column) - (grid.Gap + 1) / 2;
        Assert.True(x < 240);

        Assert.Equal(Theme.Base, rig.At(x, 136));
        Assert.Equal(Theme.Text, rig.At(x, 170));
        Assert.NotEqual(0, rig.Near(Theme.Text, new Box(270, 122, 100, 12)));
    }
}

[Collection(nameof(TimingSensitive))]
public class NowPageAllocationTests
{
    private static long AllocatedBy(Action action)
    {
        action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void A_tick_that_finds_nothing_allocates_nothing()
    {
        using var rig = new NowRig(NowRig.FullUntil(NowRig.Loud(60), NowRig.Loud(60), 60, new SourceChange(50 * NowRig.Rate, "Discord.exe")), null, 1.0, directWrite: true);
        rig.Settle();

        var bytes = AllocatedBy(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                rig.Screen.WaveTick();
            }
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void A_tick_that_draws_the_lanes_allocates_nothing_while_the_words_stay()
    {
        var system = NowRig.Loud(60);
        var mic = NowRig.Loud(60);
        var early = NowRig.FullUntil(system, mic, 60);
        var late = NowRig.FullUntil(system, mic, 64);
        using var rig = new NowRig(early, null, 1.0, directWrite: true);
        rig.Settle();
        var tick = 0;

        var bytes = AllocatedBy(() =>
        {
            for (var i = 0; i < 40; i++)
            {
                rig.Panel.Facts = ++tick % 2 == 0 ? early : late;
                rig.Screen.WaveTick();
            }
        });

        Assert.NotEmpty(rig.Host.Areas);
        Assert.Equal(0, bytes);
    }
}

public sealed class NowWindowTests : IDisposable
{
    private readonly ScreenRig _rig = new();
    private readonly SettingsWindow _window;
    private readonly NowPanel _panel = new() { Facts = NowRig.Held(NowRig.Loud(5), null, 5) };

    public NowWindowTests()
    {
        _window = new SettingsWindow(_rig.Settings, _rig.Backend, () => 0f, firstRun: false, offscreen: true, () => { });
        _window.Screen.SetNow(_panel);
    }

    public void Dispose()
    {
        _window.Dispose();
        _rig.Dispose();
    }

    private void SizeMessage(nuint kind) => WindowNative.SendMessageW(_window.Hwnd, WindowNative.WM_SIZE, kind, 0);

    [Fact]
    public void Windows_saying_minimized_stops_the_wave_and_saying_restored_starts_it()
    {
        Assert.True(_window.Screen.WantsWave);

        SizeMessage(WindowNative.SIZE_MINIMIZED);
        Assert.False(_window.Screen.WantsWave);

        SizeMessage(0);
        Assert.True(_window.Screen.WantsWave);
    }

    [Fact]
    public void Its_timer_message_makes_a_tick_and_the_one_of_the_meter_does_not()
    {
        const nuint meter = 1, wave = 2;
        Assert.Null(_window.Screen.Wave);

        WindowNative.SendMessageW(_window.Hwnd, Hindsound.Ui.Native.WM_TIMER, meter, 0);
        Assert.Null(_window.Screen.Wave);

        WindowNative.SendMessageW(_window.Hwnd, Hindsound.Ui.Native.WM_TIMER, wave, 0);
        Assert.NotNull(_window.Screen.Wave);
        Assert.NotEqual(WaveLane.Empty, _window.Screen.Wave!.SystemBars[^1]);
    }

    [Fact]
    public void Moving_to_a_screen_of_another_scale_draws_on_a_new_grid()
    {
        _window.ProbeDpiChange(144);
        WindowNative.SendMessageW(_window.Hwnd, Hindsound.Ui.Native.WM_TIMER, 2, 0);

        Assert.Equal(78, _window.Screen.Wave!.Columns);
    }
}
