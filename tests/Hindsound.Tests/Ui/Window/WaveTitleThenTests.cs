using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The title of the PC side while the line of a change shows: the past from one other app is named before the app of now, a past
/// from the whole PC keeps "Whole PC", and the app alone comes back once the line has left the wave.
/// </summary>
public class WaveTitleThenTests
{
    private const string Discord = "Discord.exe";
    private const string Spotify = "Spotify.exe";
    private const int Rate = NowRig.Rate;
    private const int Columns = 60;
    private const double Scale = 1.0;
    private const uint Accent = 0x7CFF3B;
    private static readonly int MemorySeconds = NowRig.Memory / Rate;

    private static AudioEngineStatus On(string? app, bool missing = false) =>
        ScreenRig.Capturing() with { SourceApp = app, SourceAppMissing = missing };

    private static SourceChange At(double seconds, string? app, string? before) => new((long)(seconds * Rate), app, before);

    private static WaveMarker LineOf(double untilSeconds, params SourceChange[] changes) =>
        WaveView.MarkerOf(NowRig.FullUntil(null, null, untilSeconds, changes), Columns);

    [Fact]
    public void An_app_after_another_app_names_both_while_the_line_shows()
    {
        var line = LineOf(100, At(95, Spotify, Discord));

        Assert.True(line.Shows);
        Assert.Equal(Discord, line.Past);
        Assert.Equal("Discord, then Spotify", WaveView.SystemLaneTitle(On(Spotify), line));
    }

    [Fact]
    public void Once_the_line_left_the_wave_the_app_alone_comes_back()
    {
        var gone = LineOf(100 + MemorySeconds, At(95, Spotify, Discord));

        Assert.False(gone.Shows);
        Assert.Equal("Spotify", WaveView.SystemLaneTitle(On(Spotify), gone));
    }

    [Fact]
    public void An_app_after_the_whole_PC_keeps_whole_PC_while_the_line_shows()
    {
        var line = LineOf(100, At(95, Spotify, null));

        Assert.True(line.Shows);
        Assert.Null(line.Past);
        Assert.Equal("Whole PC", WaveView.SystemLaneTitle(On(Spotify), line));
    }

    [Fact]
    public void Back_to_the_whole_PC_after_an_app_says_whole_PC_and_the_line_says_where_it_starts()
    {
        var line = LineOf(100, At(95, null, Discord));

        Assert.Equal(Discord, line.Past);
        Assert.Equal("Whole PC", WaveView.SystemLaneTitle(On(null), line));
        Assert.Equal("Whole PC from here", WaveView.MarkerText(line.App));
    }

    [Fact]
    public void Not_running_comes_first_even_with_another_app_before()
    {
        var line = LineOf(100, At(95, Spotify, Discord));

        Assert.Equal("Spotify (not running)", WaveView.SystemLaneTitle(On(Spotify, missing: true), line));
    }

    [Fact]
    public void The_same_app_on_both_sides_of_the_line_is_named_once()
    {
        Assert.Equal("Spotify", WaveView.SystemLaneTitle(On(Spotify), new WaveMarker(50, Spotify, Past: "spotify.EXE")));
    }

    [Fact]
    public void Long_names_are_not_cut_in_the_title()
    {
        var line = new WaveMarker(50, "MoonMinersDeluxe.exe", Past: "SpaceTruckersOnline2.exe");

        Assert.Equal("SpaceTruckersOnline2, then MoonMinersDeluxe", WaveView.SystemLaneTitle(On("MoonMinersDeluxe.exe"), line));
    }

    [Fact]
    public void Two_sources_before_the_line_say_whole_PC_until_the_older_change_leaves_the_memory()
    {
        // Whole PC, then Discord, then Spotify: while the Discord change is held, the grey past is part PC and part Discord.
        SourceChange[] changes = [At(100 - MemorySeconds / 2, Discord, null), At(95, Spotify, Discord)];
        var both = LineOf(100, changes);
        Assert.True(both.Shows);
        Assert.Null(both.Past);
        Assert.Equal("Whole PC", WaveView.SystemLaneTitle(On(Spotify), both));

        var one = LineOf(100 - MemorySeconds / 2 + MemorySeconds + 1, changes);
        Assert.True(one.Shows);
        Assert.Equal("Discord, then Spotify", WaveView.SystemLaneTitle(On(Spotify), one));
    }

    [Fact]
    public void The_title_follows_the_older_change_leaving_the_memory_with_the_same_status()
    {
        // With the memory full the engine sends no new status: only the past of the line tells the title it can name Discord now.
        var wave = new NowWave();
        var canvas = new FakeCanvas();
        var status = On(Spotify);
        SourceChange[] changes = [At(100 - MemorySeconds / 2, Discord, null), At(95, Spotify, Discord)];

        wave.Look(Scale, NowRig.FullUntil(null, null, 100, changes), 30, status);
        wave.PaintLanes(canvas, Accent, status, titles: true);
        Assert.Contains("Whole PC", canvas.Drawn);
        canvas.Drawn.Clear();

        var later = wave.Look(Scale, NowRig.FullUntil(null, null, 100 - MemorySeconds / 2 + MemorySeconds + 1, changes), 30, status);
        wave.PaintLanes(canvas, Accent, status, titles: true);

        Assert.True(wave.Marker.Shows);
        Assert.True((later & WaveChange.Titles) != 0);
        Assert.Contains("Discord, then Spotify", canvas.Drawn);
        Assert.DoesNotContain("Whole PC", canvas.Drawn);
    }

    [Fact]
    public void A_title_wider_than_the_lane_is_cut_at_the_lane()
    {
        var wave = new NowWave();
        var canvas = new FakeCanvas();
        var longer = new string('A', 60) + ".exe";
        var other = new string('B', 60) + ".exe";
        var status = On(longer);

        wave.Look(Scale, NowRig.FullUntil(null, null, 100, At(95, longer, other)), 30, status);
        wave.PaintLanes(canvas, Accent, status, titles: true);

        var title = Assert.Single(canvas.Drawn, text => text.StartsWith("BBBB", StringComparison.Ordinal));
        Assert.EndsWith("...", title);
        Assert.True(canvas.Measure(title, Face.Medium, 13) <= SettingsScreen.Width);
        Assert.StartsWith(new string('B', 60) + ", then AAA", title, StringComparison.Ordinal);
    }
}
