using Hindsound.Audio;
using Hindsound.Tests.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class WaveViewComposeTests
{
    private const int Rate = InternalFormat.SampleRate;
    private const int Slice = WaveLane.FramesPerSlice;
    private const int FiveMinutes = 300 * Rate;

    private static (WaveLane Lane, WaveFacts Facts, SourceTrack Track) Held(int bufferFrames, int heldFrames, int seed = 1)
    {
        var track = new SourceTrack(1, bufferFrames, 0);
        WaveRig.Write(track, heldFrames, seed);
        var facts = WaveRig.FactsOf(track, bufferFrames);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, facts);
        return (lane, facts, track);
    }

    private static byte[] Bars(WaveLane lane, in WaveFacts facts, int columns)
    {
        var bars = new byte[columns];
        WaveView.Compose(lane, facts, bars);
        return bars;
    }

    [Fact]
    public void Twelve_seconds_on_five_minutes_fill_the_right_and_leave_the_left_empty()
    {
        var (lane, facts, _) = Held(FiveMinutes, 12 * Rate);

        var bars = Bars(lane, facts, 240);

        // 240 columns on 5:00 is 1.25 s a bar, so 10 bars, or 11 when the edges fall inside two more.
        var filled = bars.Count(bar => bar != WaveLane.Empty);
        Assert.InRange(filled, 10, 11);
        Assert.All(bars[..(240 - filled)], bar => Assert.Equal(WaveLane.Empty, bar));
        Assert.All(bars[(240 - filled)..], bar => Assert.NotEqual(WaveLane.Empty, bar));
    }

    [Fact]
    public void A_bar_is_the_loudest_of_its_slices()
    {
        // Three columns over 30 slices: ten slices each, and the newest column holds slices 20 to 29.
        var (lane, facts, track) = Held(3 * Rate, 3 * Rate);

        var bars = Bars(lane, facts, 3);

        for (var column = 0; column < 3; column++)
        {
            var expected = 0;
            for (var slice = column * 10; slice < (column + 1) * 10; slice++)
            {
                expected = Math.Max(expected, WaveRig.Raw(track, slice));
            }

            Assert.Equal(WaveView.Height((byte)expected), bars[column]);
        }
    }

    [Fact]
    public void Bars_stay_the_same_as_time_goes_on_and_only_move_left_as_a_whole()
    {
        const int Columns = 180;
        var track = new SourceTrack(1, 30 * Rate, 0);
        var lane = new WaveLane();
        var other = new WaveLane();
        WaveRig.Write(track, 40 * Rate, 1);
        var facts = WaveRig.FactsOf(track, 30 * Rate);
        WaveRig.ReadAll(lane, facts);
        var before = Bars(lane, facts, Columns);
        var moves = 0;

        for (var step = 0; step < 400; step++)
        {
            WaveRig.Write(track, Rate / 50, step + 2);
            facts = WaveRig.FactsOf(track, 30 * Rate);
            WaveLane.CatchUp(facts, lane, other);
            var now = Bars(lane, facts, Columns);

            // Same place, or one to the left. The bar on the far left can lose a slice; the newest can grow.
            var moved = now.AsSpan(1, Columns - 3).SequenceEqual(before.AsSpan(2, Columns - 3));
            Assert.True(moved || now.AsSpan(1, Columns - 3).SequenceEqual(before.AsSpan(1, Columns - 3)), $"Bars changed at step {step}");
            moves += moved && !now.AsSpan(1, Columns - 3).SequenceEqual(before.AsSpan(1, Columns - 3)) ? 1 : 0;
            before = now;
        }

        Assert.True(moves > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(180)]
    [InlineData(1000)]
    public void A_memory_of_one_second_works_at_any_width(int columns)
    {
        var (lane, facts, _) = Held(Rate, 5 * Rate);

        var bars = Bars(lane, facts, columns);

        Assert.DoesNotContain(WaveLane.Empty, bars[^1..]);
    }

    [Fact]
    public void With_more_columns_than_slices_a_slice_spreads_over_steps()
    {
        var (lane, facts, track) = Held(Rate, 5 * Rate);

        var bars = Bars(lane, facts, 180);

        // Ten slices on 180 columns: 18 columns each, the newest slice on the right.
        Assert.All(bars[^18..], bar => Assert.Equal(WaveView.Height(WaveRig.Raw(track, 49)), bar));
        Assert.Equal(WaveView.Height(WaveRig.Raw(track, 48)), bars[^19]);
    }

    [Fact]
    public void Half_a_second_held_on_one_second_leaves_the_left_empty()
    {
        var (lane, facts, _) = Held(Rate, Rate / 2);

        var bars = Bars(lane, facts, 100);

        Assert.Equal(WaveLane.Empty, bars[0]);
        Assert.Equal(WaveLane.Empty, bars[49]);
        Assert.NotEqual(WaveLane.Empty, bars[50]);
        Assert.NotEqual(WaveLane.Empty, bars[99]);
    }

    [Fact]
    public void No_columns_is_no_work()
    {
        var (lane, facts, _) = Held(Rate, Rate);

        WaveView.Compose(lane, facts, []);
        Assert.Equal(0, WaveView.ZoneOf(1, facts, 0));
        Assert.False(WaveView.MarkerOf(facts, 0).Shows);
    }

    [Fact]
    public void A_lane_with_no_track_keeps_its_place_as_a_flat_line()
    {
        var bars = Bars(new WaveLane(), default, 50);

        Assert.All(bars, bar => Assert.Equal(0, bar));
    }

    [Fact]
    public void A_track_with_nothing_yet_shows_nothing()
    {
        var track = new SourceTrack(1, Rate, 500);
        var facts = WaveRig.FactsOf(track, Rate);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, facts);

        Assert.All(Bars(lane, facts, 50), bar => Assert.Equal(WaveLane.Empty, bar));
    }

    [Fact]
    public void Before_a_mic_turned_on_again_is_empty_and_not_a_flat_line()
    {
        var mic = new SourceTrack(1, 60 * Rate, 30 * Rate);
        WaveRig.Write(mic, 10 * Rate, 1);
        var system = new SourceTrack(2, 60 * Rate, 0);
        WaveRig.Write(system, 40 * Rate, 2);
        var facts = WaveRig.FactsOf(system, 60 * Rate, mic);
        var lane = new WaveLane();
        var systemLane = new WaveLane();
        WaveLane.CatchUp(facts, systemLane, lane);

        var bars = Bars(lane, facts, 60);

        Assert.All(bars[..50], bar => Assert.Equal(WaveLane.Empty, bar));
        Assert.All(bars[50..], bar => Assert.NotEqual(WaveLane.Empty, bar));
        Assert.All(Bars(systemLane, facts, 60)[20..], bar => Assert.NotEqual(WaveLane.Empty, bar));
    }

    [Fact]
    public void The_height_function_is_what_the_bars_go_through()
    {
        var (lane, facts, track) = Held(Rate, Rate);

        var bars = Bars(lane, facts, 10);

        Assert.Equal(WaveView.Height(WaveRig.Raw(track, 9)), bars[9]);
        Assert.Equal(WaveLane.FullLevel, WaveView.Height(WaveLane.FullLevel));
        Assert.Equal(0, WaveView.Height(0));
    }
}

[Collection(nameof(TimingSensitive))]
public class WaveViewEngineTests
{
    private const int Rate = InternalFormat.SampleRate;
    private const string App = "Discord.exe";

    [Fact]
    public async Task An_application_closed_gives_a_flat_line_and_not_a_hole()
    {
        using var rig = new AudioTestRig(bufferSeconds: 60, micEnabled: false, sourceApp: App, processes: new FakeProcesses());
        await rig.Engine.StartAsync();
        var lane = new WaveLane();
        var mic = new WaveLane();

        rig.Clock.Advance(6);
        var facts = rig.Engine.WaveFacts;
        WaveLane.CatchUp(facts, lane, mic);
        var bars = new byte[60];
        WaveView.Compose(lane, facts, bars);

        // 6 s held of 60 s: one column a second, the six on the right flat, and the rest not held at all.
        Assert.Equal(0, facts.System!.EndFrame);
        Assert.All(bars[..54], bar => Assert.Equal(WaveLane.Empty, bar));
        Assert.All(bars[54..], bar => Assert.Equal(0, bar));

        // The mic's lane, with no track, stays in place too.
        WaveView.Compose(mic, facts, bars);
        Assert.All(bars, bar => Assert.Equal(0, bar));
    }

    [Fact]
    public async Task A_mute_PC_gives_a_flat_line_after_the_last_sound()
    {
        using var rig = new AudioTestRig(bufferSeconds: 60, micEnabled: false);
        await rig.Engine.StartAsync();
        rig.Run(4, mic: false);
        rig.Clock.Advance(5);
        var lane = new WaveLane();
        var mic = new WaveLane();

        var facts = rig.Engine.WaveFacts;
        WaveLane.CatchUp(facts, lane, mic);
        var bars = new byte[60];
        WaveView.Compose(lane, facts, bars);

        Assert.All(bars[^5..], bar => Assert.Equal(0, bar));
        Assert.All(bars[^9..^5], bar => Assert.True(bar > 0 && bar != WaveLane.Empty));
        Assert.All(bars[..^10], bar => Assert.Equal(WaveLane.Empty, bar));
    }

    [Fact]
    public async Task Paused_the_wave_stays_as_it_was()
    {
        using var rig = new AudioTestRig(bufferSeconds: 60);
        await rig.Engine.StartAsync();
        rig.Run(4);
        await rig.Engine.PauseAsync();
        var system = new WaveLane();
        var mic = new WaveLane();

        var facts = rig.Engine.WaveFacts;
        WaveLane.CatchUp(facts, system, mic);
        var bars = new byte[60];
        WaveView.Compose(system, facts, bars);
        Assert.True(system.IsCaughtUp);

        rig.Clock.Advance(10);
        var later = rig.Engine.WaveFacts;
        WaveLane.CatchUp(later, system, mic);
        var barsLater = new byte[60];
        WaveView.Compose(system, later, barsLater);

        Assert.Equal(bars, barsLater);
    }

    [Fact]
    public async Task A_zone_is_what_a_clip_of_that_length_would_hold()
    {
        using var rig = new AudioTestRig(bufferSeconds: 60);
        await rig.Engine.StartAsync();
        rig.Run(40);

        var facts = rig.Engine.WaveFacts;
        var snapshot = rig.Engine.Snapshot(TimeSpan.FromSeconds(30));

        Assert.Equal(30 * Rate, snapshot.Mic.Length);
        Assert.Equal("Save: last 0:30", WaveView.SaveLine(30, facts));

        // 60 columns on 60 s: the 30 most recent.
        Assert.Equal(30, WaveView.ZoneOf(30, facts, 60));
    }

    [Fact]
    public async Task A_change_of_app_turns_the_past_grey_with_a_line_where_it_happened()
    {
        using var rig = new AudioTestRig(bufferSeconds: 60, processes: new FakeProcesses(new ProcessEntry(50, 1, App)));
        await rig.Engine.StartAsync();
        rig.Run(50);
        await rig.Engine.SetSourceAppAsync(App);
        rig.Run(5);

        var facts = rig.Engine.WaveFacts;
        var marker = WaveView.MarkerOf(facts, 60);
        var zone = WaveView.ZoneOf(30, facts, 60);

        Assert.Equal(App, marker.App);
        Assert.Equal(55, marker.Column);
        Assert.Equal(WaveTone.GrayLight, WaveView.ToneOf(54, zone, marker));
        Assert.Equal(30, zone);
        Assert.Equal(WaveTone.GrayDark, WaveView.ToneOf(10, zone, marker));
        Assert.Equal(WaveTone.GrayLight, WaveView.ToneOf(40, zone, marker));
        Assert.Equal(WaveTone.Accent, WaveView.ToneOf(55, zone, marker));
        Assert.Equal("Discord only from here", WaveView.MarkerText(marker.App));
        Assert.Equal("Save: last 0:30 (0:25 from before Discord)", WaveView.SaveLine(30, facts));
    }

    [Fact]
    public void Composing_allocates_nothing()
    {
        var track = new SourceTrack(1, 10 * Rate, 0);
        WaveRig.Write(track, 10 * Rate, 1);
        var facts = WaveRig.FactsOf(track, 10 * Rate, changes: [new SourceChange(5 * Rate, App)]);
        var lane = new WaveLane();
        WaveRig.ReadAll(lane, facts);
        var bars = new byte[180];
        WaveView.Compose(lane, facts, bars);
        _ = WaveView.ZoneOf(30, facts, 180);
        _ = WaveView.MarkerOf(facts, 180);

        Action draws = () =>
        {
            for (var i = 0; i < 200; i++)
            {
                WaveView.Compose(lane, facts, bars);
                _ = WaveView.ZoneOf(30, facts, 180);
                _ = WaveView.ToneOf(i, 10, WaveView.MarkerOf(facts, 180));
            }
        };

        Assert.Equal(0, WaveRig.LeastAllocated(draws));
    }
}

public class WaveViewZoneTests
{
    private const int Rate = InternalFormat.SampleRate;

    private static WaveFacts Facts(int bufferSeconds, double heldSeconds, double endSeconds = 1000, params SourceChange[] changes)
    {
        var end = (long)(endSeconds * Rate);
        return new WaveFacts(null, null, end - (long)(heldSeconds * Rate), end, bufferSeconds * Rate, changes);
    }

    [Fact]
    public void Thirty_seconds_on_a_full_five_minutes()
    {
        Assert.Equal(270, WaveView.ZoneOf(30, Facts(300, 300), 300));
    }

    [Fact]
    public void Thirty_seconds_with_only_twelve_held_is_the_twelve()
    {
        Assert.Equal(288, WaveView.ZoneOf(30, Facts(300, 12), 300));
    }

    [Fact]
    public void A_clip_longer_than_the_memory_stops_at_the_memory()
    {
        Assert.Equal(0, WaveView.ZoneOf(300, Facts(60, 60), 60));
        Assert.Equal(40, WaveView.ZoneOf(300, Facts(60, 20), 60));
    }

    [Fact]
    public void Nothing_held_is_no_zone()
    {
        Assert.Equal(60, WaveView.ZoneOf(30, Facts(60, 0), 60));
    }

    [Fact]
    public void It_works_with_fewer_slices_than_columns()
    {
        Assert.InRange(WaveView.ZoneOf(1, Facts(1, 0.5), 180), 80, 100);
        Assert.Equal(0, WaveView.ZoneOf(30, Facts(1, 1), 180));
    }

    [Fact]
    public void The_line_shows_only_while_its_frame_is_in_the_window()
    {
        Assert.Equal(new WaveMarker(20, "Game.exe"), WaveView.MarkerOf(Facts(60, 60, 1000, new SourceChange(960 * Rate, "Game.exe")), 60));
        Assert.Equal(WaveMarker.None, WaveView.MarkerOf(Facts(60, 60, 1000, new SourceChange(939 * Rate, "Game.exe")), 60));
        Assert.Equal(WaveMarker.None, WaveView.MarkerOf(Facts(60, 60, 1000, new SourceChange(940 * Rate, "Game.exe")), 60));
        Assert.Equal(WaveMarker.None, WaveView.MarkerOf(Facts(60, 60), 60));
        Assert.Equal(WaveMarker.None, WaveView.MarkerOf(default, 60));
    }

    [Fact]
    public void The_last_change_is_the_one_that_counts()
    {
        var facts = Facts(60, 60, 1000, new SourceChange(950 * Rate, "Game.exe"), new SourceChange(980 * Rate, null));

        Assert.Equal(new WaveMarker(40, null), WaveView.MarkerOf(facts, 60));
    }

    [Fact]
    public void A_change_with_nothing_after_it_yet_stands_on_the_right_edge()
    {
        var marker = WaveView.MarkerOf(Facts(60, 60, 1000, new SourceChange(1000 * Rate, "Game.exe")), 60);

        Assert.Equal(59, marker.Column);
        Assert.Equal(WaveTone.GrayLight, WaveView.ToneOf(58, 30, marker));
    }

    [Fact]
    public void Intensity_and_hue_are_two_separate_things()
    {
        var marker = new WaveMarker(50, null);

        Assert.Equal(WaveTone.GrayDark, WaveView.ToneOf(29, 30, marker));
        Assert.Equal(WaveTone.GrayLight, WaveView.ToneOf(30, 30, marker));
        Assert.Equal(WaveTone.GrayLight, WaveView.ToneOf(49, 30, marker));
        Assert.Equal(WaveTone.Accent, WaveView.ToneOf(50, 30, marker));

        marker = new WaveMarker(10, null);
        Assert.Equal(WaveTone.AccentDim, WaveView.ToneOf(29, 30, marker));
        Assert.Equal(WaveTone.GrayDark, WaveView.ToneOf(9, 30, marker));

        Assert.Equal(WaveTone.AccentDim, WaveView.ToneOf(9, 30, WaveMarker.None));
        Assert.Equal(WaveTone.Accent, WaveView.ToneOf(30, 30, WaveMarker.None));
    }

    [Theory]
    [InlineData(300, CaptureActivity.Capturing, "-5:00", "-2:30", "now")]
    [InlineData(300, CaptureActivity.Paused, "-5:00", "-2:30", "paused")]
    [InlineData(1800, CaptureActivity.NoDevice, "-30:00", "-15:00", "now")]
    [InlineData(61, CaptureActivity.Capturing, "-1:01", "-0:31", "now")]
    [InlineData(1, CaptureActivity.Capturing, "-0:01", "", "now")]
    public void The_axis_says_the_length_of_the_memory(int seconds, CaptureActivity activity, string left, string middle, string right)
    {
        Assert.Equal(new WaveAxis(left, middle, right), WaveView.AxisLabels(seconds, activity));
    }

    private static AudioEngineStatus Status(CaptureActivity activity = CaptureActivity.Capturing, bool micEnabled = true, bool micCapturing = true, string? app = null, bool missing = false) =>
        new(activity, true, micEnabled, micCapturing, null, null, false, TimeSpan.Zero, 0, app, missing);

    [Fact]
    public void The_title_of_the_PC_side_says_what_is_captured()
    {
        Assert.Equal("Whole PC", WaveView.SystemLaneTitle(null, WaveMarker.None));
        Assert.Equal("Whole PC", WaveView.SystemLaneTitle(Status(), WaveMarker.None));
        Assert.Equal("Discord", WaveView.SystemLaneTitle(Status(app: "Discord.exe"), WaveMarker.None));
        Assert.Equal("Discord (not running)", WaveView.SystemLaneTitle(Status(app: "Discord.exe", missing: true), WaveMarker.None));
    }

    [Fact]
    public void The_title_of_the_mic_says_its_state()
    {
        Assert.Equal("Mic", WaveView.MicLaneTitle(null));
        Assert.Equal("Mic", WaveView.MicLaneTitle(Status()));
        Assert.Equal("Mic", WaveView.MicLaneTitle(Status(micEnabled: false, micCapturing: false)));
        Assert.Equal("Mic lost", WaveView.MicLaneTitle(Status(micCapturing: false)));
        Assert.Equal("Mic", WaveView.MicLaneTitle(Status(CaptureActivity.NoDevice, micCapturing: false)));
    }

    [Theory]
    [InlineData(CaptureActivity.Paused)]
    [InlineData(CaptureActivity.Stopped)]
    public void A_pause_closes_the_mic_on_purpose_and_that_is_not_a_lost_mic(CaptureActivity activity)
    {
        Assert.Equal("Mic", WaveView.MicLaneTitle(Status(activity, micCapturing: false)));
        Assert.Equal("Mic", WaveView.MicLaneTitle(Status(activity, micEnabled: false, micCapturing: false)));
    }

    [Fact]
    public void Nothing_to_capture_from_is_said_like_on_the_status_band()
    {
        Assert.Null(WaveView.NoCaptureText(null));
        Assert.Null(WaveView.NoCaptureText(Status()));
        Assert.Null(WaveView.NoCaptureText(Status(CaptureActivity.Paused)));
        Assert.Equal("No audio device", WaveView.NoCaptureText(Status(CaptureActivity.NoDevice)));
        Assert.Equal("Can't capture Discord", WaveView.NoCaptureText(Status(CaptureActivity.NoDevice, app: "Discord.exe")));
    }

    [Fact]
    public void The_save_line_says_the_length_and_what_is_short_of_it()
    {
        Assert.Equal("Save: last 0:30", WaveView.SaveLine(30, Facts(300, 300)));
        Assert.Equal("Save: last 0:30", WaveView.SaveLine(30, Facts(300, 30)));
        Assert.Equal("Save: last 0:12 of 0:30", WaveView.SaveLine(30, Facts(300, 12)));
        Assert.Equal("Save: last 1:00 of 5:00", WaveView.SaveLine(300, Facts(60, 60)));
    }

    [Fact]
    public void The_save_line_says_how_much_comes_from_before_the_change()
    {
        Assert.Equal(
            "Save: last 0:30 (0:25 from before Discord)",
            WaveView.SaveLine(30, Facts(300, 300, 1000, new SourceChange(995 * Rate, "Discord.exe"))));
        Assert.Equal(
            "Save: last 0:30 (0:25 from before Whole PC)",
            WaveView.SaveLine(30, Facts(300, 300, 1000, new SourceChange(995 * Rate, null))));
        Assert.Equal(
            "Save: last 0:12 of 0:30 (0:07 from before Discord)",
            WaveView.SaveLine(30, Facts(300, 12, 1000, new SourceChange(995 * Rate, "Discord.exe"))));
    }

    [Theory]
    [InlineData(960)]
    [InlineData(970)]
    public void A_change_outside_the_saved_part_is_not_mentioned(int frameSeconds)
    {
        Assert.Equal("Save: last 0:30", WaveView.SaveLine(30, Facts(300, 300, 1000, new SourceChange(frameSeconds * Rate, "Discord.exe"))));
    }

    [Fact]
    public void A_change_after_the_end_of_the_window_is_not_mentioned()
    {
        Assert.Equal("Save: last 0:30", WaveView.SaveLine(30, Facts(300, 300, 1000, new SourceChange(1001 * Rate, "Discord.exe"))));
    }

    [Fact]
    public void Paused_after_choosing_an_app_all_that_is_held_is_from_before_it()
    {
        // The change sits exactly on the end: nothing was captured from the new source yet.
        Assert.Equal(
            "Save: last 0:02 of 0:30 (0:02 from before Discord)",
            WaveView.SaveLine(30, Facts(300, 2, 1000, new SourceChange(1000 * Rate, "Discord.exe"))));
        Assert.Equal((2, 2), WaveView.SaveSeconds(30, Facts(300, 2, 1000, new SourceChange(1000 * Rate, "Discord.exe"))));
    }

    [Fact]
    public void Less_than_half_a_second_from_before_is_not_worth_saying()
    {
        Assert.Equal("Save: last 0:30", WaveView.SaveLine(30, Facts(300, 300, 1000, new SourceChange(970 * Rate + Rate / 4, "Discord.exe"))));
    }

    [Fact]
    public void The_line_of_a_change_says_where_the_new_source_starts()
    {
        Assert.Equal("Discord only from here", WaveView.MarkerText("Discord.exe"));
        Assert.Equal("Whole PC from here", WaveView.MarkerText(null));
    }
}
