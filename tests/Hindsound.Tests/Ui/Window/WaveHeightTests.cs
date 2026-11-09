using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

public class WaveHeightTests
{
    private static byte LevelOf(double db) => WaveLane.ToLevel((int)Math.Round(Math.Pow(10, db / 20) * 32767));

    private static double Share(double db) => WaveView.Height(LevelOf(db)) / (double)WaveLane.FullLevel;

    [Theory]
    [InlineData(-1, 0.94)]
    [InlineData(-6, 0.71)]
    [InlineData(-12, 0.50)]
    [InlineData(-30, 0.18)]
    [InlineData(-50, 0.06)]
    public void The_height_is_the_square_root_of_the_peak(double db, double share)
    {
        Assert.InRange(Share(db), share - 0.01, share + 0.01);
    }

    [Theory]
    [InlineData(-50.5)]
    [InlineData(-57)]
    [InlineData(-60)]
    public void Under_the_silence_floor_there_is_no_bar_at_all(double db)
    {
        Assert.Equal(0, Share(db));
    }

    [Fact]
    public void The_two_ends_of_the_scale()
    {
        Assert.Equal(0, WaveView.Height(0));
        Assert.Equal(WaveLane.FullLevel, WaveView.Height(WaveLane.FullLevel));
    }

    [Fact]
    public void Louder_is_never_shorter()
    {
        for (var level = 1; level <= WaveLane.FullLevel; level++)
        {
            Assert.True(WaveView.Height((byte)level) >= WaveView.Height((byte)(level - 1)), $"level {level}");
        }
    }

    [Fact]
    public void Twelve_seconds_on_five_minutes_have_the_height_of_their_peak_on_the_right_and_nothing_on_the_left()
    {
        const int memory = 300 * InternalFormat.SampleRate;
        var track = NowSamples.Track(NowRig.Peaks(12, 0.5f));
        var facts = new WaveFacts(track, null, 0, 12L * InternalFormat.SampleRate, memory, []);
        var lane = new WaveLane();
        WaveLane.CatchUp(facts, lane, new WaveLane());
        var bars = new byte[87];

        WaveView.Compose(lane, facts, bars);

        Assert.InRange(bars[^1] / (double)WaveLane.FullLevel, 0.70, 0.72);
        Assert.InRange(bars[^3] / (double)WaveLane.FullLevel, 0.70, 0.72);
        Assert.Equal(WaveLane.Empty, bars[0]);
        Assert.Equal(WaveLane.Empty, bars[^6]);
    }
}

public class WaveWordsTests
{
    private static AudioEngineStatus Status(string? app = null, bool missing = false, bool micEnabled = true, bool micCapturing = true, CaptureActivity activity = CaptureActivity.Capturing) =>
        new(activity, true, micEnabled, micCapturing, "Speakers", "Headset", true, TimeSpan.FromSeconds(62), 86_688_000, app, missing);

    [Fact]
    public void The_PC_side_stays_whole_PC_while_the_mixed_past_shows_and_says_the_app_once_it_left()
    {
        var line = new WaveMarker(50, "Discord.exe");

        Assert.Equal("Whole PC", WaveView.SystemLaneTitle(Status("Discord.exe"), line));
        Assert.Equal("Discord", WaveView.SystemLaneTitle(Status("Discord.exe"), WaveMarker.None));
        Assert.Equal("Whole PC", WaveView.SystemLaneTitle(Status(), line));
    }

    [Fact]
    public void Not_running_comes_first_whatever_the_past_shows()
    {
        var line = new WaveMarker(50, "Discord.exe");

        Assert.Equal("Discord (not running)", WaveView.SystemLaneTitle(Status("Discord.exe", missing: true), line));
    }

    [Fact]
    public void Mic_off_is_said_in_the_lane_and_the_title_stays_Mic()
    {
        var off = Status(micEnabled: false, micCapturing: false);

        Assert.Equal("Mic", WaveView.MicLaneTitle(off));
        Assert.Equal("Mic off", WaveView.MicLaneText(off));
        Assert.Null(WaveView.MicLaneText(Status()));
        Assert.Null(WaveView.MicLaneText(null));
    }

    [Fact]
    public void With_no_device_at_all_the_mic_is_not_lost()
    {
        Assert.Equal("Mic", WaveView.MicLaneTitle(Status(micCapturing: false, activity: CaptureActivity.NoDevice)));
        Assert.Equal("Mic lost", WaveView.MicLaneTitle(Status(micCapturing: false)));
    }

    [Fact]
    public void Held_from_is_the_first_column_of_what_the_memory_holds()
    {
        const int memory = 60 * InternalFormat.SampleRate;
        Assert.Equal(87, WaveView.HeldFrom(new WaveFacts(null, null, 0, 0, memory, []), 87));
        Assert.Equal(0, WaveView.HeldFrom(new WaveFacts(null, null, 0, memory, memory, []), 87));
        Assert.InRange(WaveView.HeldFrom(new WaveFacts(null, null, 0, memory / 2, memory, []), 87), 42, 44);
    }

    [Fact]
    public void The_band_with_no_device_shows_no_figures_so_its_title_is_never_cut()
    {
        var band = StatusText.From(Status(micCapturing: false, activity: CaptureActivity.NoDevice), 300);

        Assert.Equal("No audio device", band.Title);
        Assert.Equal(string.Empty, band.Mic);
        Assert.Equal(string.Empty, band.Duration);
        Assert.Equal(string.Empty, band.Ram);
        Assert.Equal("Pause", band.PauseLabel);
        Assert.True(band.PauseEnabled);
    }
}
