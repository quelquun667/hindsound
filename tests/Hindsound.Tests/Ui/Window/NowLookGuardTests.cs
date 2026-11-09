using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// Each thing that decides a look found something new, taken alone: a guard that forgot one of them would leave the old picture
/// on screen while everything else stays equal.
/// </summary>
public class NowLookGuardTests
{
    private const double Scale = 1.0;

    [Fact]
    public void A_new_status_alone_writes_the_titles_again()
    {
        var wave = new NowWave();
        var facts = NowRig.Held(null, null, 20);
        var whole = ScreenRig.Capturing();
        wave.Look(Scale, facts, 30, whole);
        Assert.Equal(WaveChange.None, wave.Look(Scale, facts, 30, whole));

        // The chosen app stops running: the facts, the clip and the line are the same, only the words of the title are not.
        var gone = whole with { SourceApp = "Discord.exe", SourceAppMissing = true };

        Assert.Equal(WaveChange.Lanes | WaveChange.Titles, wave.Look(Scale, facts, 30, gone));
    }

    [Fact]
    public void The_kept_part_moving_alone_is_a_change_of_the_lanes()
    {
        // No track, so every bar is 0 and stays 0. The memory loses its oldest ten seconds (a smaller memory chosen, say) while the
        // clip, which is the last 30 s of the same end, stays where it is: only the part held starts somewhere else.
        const long end = 40 * NowRig.Rate;
        var wave = new NowWave();
        var status = ScreenRig.Capturing();
        wave.Look(Scale, new WaveFacts(null, null, 0, end, NowRig.Memory, []), 30, status);
        var (zone, held) = (wave.ZoneColumn, WaveView.HeldFrom(new WaveFacts(null, null, 0, end, NowRig.Memory, []), wave.Columns));

        var after = new WaveFacts(null, null, 10 * NowRig.Rate, end, NowRig.Memory, []);
        var change = wave.Look(Scale, after, 30, status);

        Assert.Equal(zone, wave.ZoneColumn);
        Assert.NotEqual(held, WaveView.HeldFrom(after, wave.Columns));
        Assert.Equal(WaveChange.Lanes, change);
    }

    [Fact]
    public void A_longer_clip_with_the_same_seconds_kept_says_so_in_the_line()
    {
        var wave = new NowWave();
        var facts = NowRig.Held(null, null, 12);
        var status = ScreenRig.Capturing();
        wave.Look(Scale, facts, 30, status);
        Assert.Equal("Save: last 0:12 of 0:30", wave.SaveText);

        var change = wave.Look(Scale, facts, 60, status);

        Assert.Equal("Save: last 0:12 of 1:00", wave.SaveText);
        Assert.True((change & WaveChange.SaveLine) != 0);
    }

    [Fact]
    public void Another_app_at_the_same_second_changes_the_name_in_the_line()
    {
        var wave = new NowWave();
        var status = ScreenRig.Capturing();
        wave.Look(Scale, NowRig.FullUntil(null, null, 100, new SourceChange(95 * NowRig.Rate, "Discord.exe")), 30, status);
        Assert.Equal("Save: last 0:30 (0:25 from before Discord)", wave.SaveText);

        var change = wave.Look(Scale, NowRig.FullUntil(null, null, 100, new SourceChange(95 * NowRig.Rate, "Chrome.exe")), 30, status);

        Assert.Equal("Save: last 0:30 (0:25 from before Chrome)", wave.SaveText);
        Assert.True((change & WaveChange.SaveLine) != 0);
    }
}

/// <summary>The clip length of the settings is what the page draws and says, on a full paint and on every tick after it.</summary>
public class NowClipLengthTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(5)]
    public void A_tick_keeps_the_zone_and_the_words_of_the_clip_set(int clipSeconds)
    {
        var facts = NowRig.Held(NowRig.Loud(10), null, 40);
        using var rig = new NowRig(facts);
        rig.Screen.Editor.SetClipSeconds(clipSeconds);
        rig.Settle();
        var zone = WaveView.ZoneOf(clipSeconds, facts, rig.Wave.Columns);
        Assert.Equal(zone, rig.Wave.ZoneColumn);
        Assert.Equal(WaveView.SaveLine(clipSeconds, facts), rig.Wave.SaveText);

        // Time goes on a little, the clip is the same: nothing about it may fall back to another length.
        rig.Panel.Facts = NowRig.Held(facts.System, null, 41);
        rig.Screen.WaveTick();
        rig.Screen.WaveTick();

        Assert.Equal(WaveView.ZoneOf(clipSeconds, rig.Panel.Facts, rig.Wave.Columns), rig.Wave.ZoneColumn);
        Assert.Equal(WaveView.SaveLine(clipSeconds, rig.Panel.Facts), rig.Wave.SaveText);
    }

    [Fact]
    public void The_lengths_do_not_share_a_zone()
    {
        // The two tests above only mean something if 60, 30 and 5 s are three different places on this memory.
        var facts = NowRig.Held(null, null, 40);
        var wave = new NowWave();
        wave.Look(1.0, facts, 30, null);
        var columns = wave.Columns;

        Assert.Equal(3, new[] { 60, 30, 5 }.Select(s => WaveView.ZoneOf(s, facts, columns)).Distinct().Count());
    }
}
