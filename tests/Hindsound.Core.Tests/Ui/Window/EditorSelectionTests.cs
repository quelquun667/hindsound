using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The rules of the part kept: inside the clip, never under 0.2 s, never over 5.2 s for the soundboard.</summary>
public class EditorSelectionTests
{
    private static EditorSelection Of(double seconds, EditorMode mode = EditorMode.Normal) => new(TimeSpan.FromSeconds(seconds), mode);

    [Fact]
    public void A_normal_edit_starts_with_the_whole_clip()
    {
        var selection = Of(30);
        Assert.Equal(0, selection.StartMs);
        Assert.Equal(30_000, selection.EndMs);
    }

    [Fact]
    public void A_soundboard_edit_starts_with_the_first_5_2_seconds_or_the_whole_of_a_shorter_clip()
    {
        Assert.Equal(5_200, Of(30, EditorMode.Soundboard).EndMs);
        Assert.Equal(3_000, Of(3, EditorMode.Soundboard).EndMs);
    }

    [Fact]
    public void The_ends_never_come_closer_than_0_2_second()
    {
        var selection = Of(30);
        selection.Move(EditorEnd.End, 1_000);
        selection.Move(EditorEnd.Start, 5_000);
        Assert.Equal(800, selection.StartMs);
        selection.Move(EditorEnd.End, 0);
        Assert.Equal(1_000, selection.EndMs);
        Assert.Equal(200, selection.KeptMs);
    }

    [Fact]
    public void The_ends_stay_inside_the_clip()
    {
        var selection = Of(30);
        selection.Nudge(EditorEnd.Start, -1_000);
        selection.Nudge(EditorEnd.End, 1_000);
        Assert.Equal(0, selection.StartMs);
        Assert.Equal(30_000, selection.EndMs);
    }

    [Fact]
    public void A_clip_shorter_than_the_minimum_keeps_all_of_itself()
    {
        var selection = Of(0.15);
        selection.Move(EditorEnd.Start, 100);
        Assert.Equal(0, selection.StartMs);
        Assert.Equal(150, selection.EndMs);
    }

    [Fact]
    public void For_the_soundboard_the_end_pushed_past_5_2_seconds_drags_the_start()
    {
        var selection = Of(30, EditorMode.Soundboard);
        selection.Move(EditorEnd.End, 20_000);
        Assert.Equal(20_000, selection.EndMs);
        Assert.Equal(14_800, selection.StartMs);
        Assert.False(selection.TooLongForSoundboard);
    }

    [Fact]
    public void For_the_soundboard_the_start_pulled_back_drags_the_end()
    {
        var selection = Of(30, EditorMode.Soundboard);
        selection.Move(EditorEnd.End, 20_000);
        selection.Move(EditorEnd.Start, 2_000);
        Assert.Equal(2_000, selection.StartMs);
        Assert.Equal(7_200, selection.EndMs);
    }

    [Fact]
    public void A_normal_edit_may_be_longer_than_the_soundboard_takes_and_says_so()
    {
        var selection = Of(30);
        Assert.True(selection.TooLongForSoundboard);
        selection.Move(EditorEnd.End, 5_200);
        Assert.False(selection.TooLongForSoundboard);
        selection.Nudge(EditorEnd.End, EditorSelection.NudgeMs);
        Assert.True(selection.TooLongForSoundboard);
    }

    [Fact]
    public void A_hundred_nudges_land_on_the_whole_tenth()
    {
        var selection = Of(30);
        for (var i = 0; i < 100; i++)
        {
            selection.Nudge(EditorEnd.Start, EditorSelection.NudgeMs);
        }

        Assert.Equal(10_000, selection.StartMs);
    }
}
