using Hindsound.App;
using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>The rule behind every text about how much is kept: nothing may promise what the memory does not hold yet.</summary>
public class KeptDurationTests
{
    [Theory]
    [InlineData(300, 300, KeptState.Full)]
    [InlineData(299, 300, KeptState.Full)]
    [InlineData(298, 300, KeptState.Filling)]
    [InlineData(301, 300, KeptState.Full)]
    [InlineData(5, 300, KeptState.Filling)]
    [InlineData(4, 300, KeptState.Starting)]
    [InlineData(0, 300, KeptState.Starting)]
    [InlineData(1, 1, KeptState.Full)]
    [InlineData(0, 1, KeptState.Starting)]
    [InlineData(1, 2, KeptState.Full)]
    [InlineData(0, 2, KeptState.Starting)]
    [InlineData(2, 3, KeptState.Full)]
    [InlineData(1, 3, KeptState.Starting)]
    [InlineData(5, 6, KeptState.Full)]
    [InlineData(4, 6, KeptState.Starting)]
    [InlineData(5, 7, KeptState.Filling)]
    public void The_state_comes_from_what_is_held_and_the_setting(int kept, int cap, KeptState expected)
    {
        Assert.Equal(expected, KeptDuration.StateOf(kept, cap));
    }

    [Theory]
    [InlineData(299, 300, 300)]
    [InlineData(301, 300, 300)]
    [InlineData(130, 300, 130)]
    [InlineData(0, 300, 0)]
    public void A_full_memory_shows_the_setting(int kept, int cap, int shown)
    {
        Assert.Equal(shown, KeptDuration.Shown(kept, cap));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.99, false)]
    [InlineData(1, true)]
    [InlineData(300, true)]
    public void Under_a_second_there_is_nothing_to_save(double seconds, bool can)
    {
        Assert.Equal(can, KeptDuration.CanSave(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0.4, 0)]
    [InlineData(11.6, 12)]
    public void Seconds_are_those_of_the_clip_title(double seconds, int whole)
    {
        Assert.Equal(whole, KeptDuration.SecondsOf(TimeSpan.FromSeconds(seconds)));
    }
}

public class CaptureOnBodyTests
{
    private const string Keys = "Ctrl+Shift+F10";

    [Theory]
    [InlineData(0, "Filling, up to 5:00. Save:")]
    [InlineData(4, "Filling, up to 5:00. Save:")]
    [InlineData(5, "0:05 of 5:00 kept. Save:")]
    [InlineData(130, "2:10 of 5:00 kept. Save:")]
    [InlineData(298, "4:58 of 5:00 kept. Save:")]
    [InlineData(299, "Keeping the last 5:00. Save:")]
    [InlineData(300, "Keeping the last 5:00. Save:")]
    public void With_keys(int kept, string body)
    {
        var content = NotificationStates.CaptureOn(300, Keys, keptSeconds: kept);

        Assert.Equal("Capture on", content.Title);
        Assert.Equal(body, content.Body);
        Assert.Equal(["Ctrl", "Shift", "F10"], content.Keys);
        Assert.Equal(NotificationPalette.Hot, content.Stripe);
        Assert.False(content.ClickOpensSettings);
    }

    [Theory]
    [InlineData(0, "Filling, up to 5:00")]
    [InlineData(130, "2:10 of 5:00 kept")]
    [InlineData(300, "Keeping the last 5:00")]
    public void Without_keys(int kept, string body)
    {
        foreach (var none in new[] { null, string.Empty, " " })
        {
            var content = NotificationStates.CaptureOn(300, none, keptSeconds: kept);

            Assert.Equal(body, content.Body);
            Assert.Empty(content.Keys);
        }
    }

    [Fact]
    public void A_duration_nobody_has_said_yet_promises_nothing()
    {
        Assert.Equal("Keeping up to 5:00. Save:", NotificationStates.CaptureOn(300, Keys).Body);
        Assert.Equal("Keeping up to 5:00", NotificationStates.CaptureOn(300, null).Body);
        Assert.Equal("Keeping up to 5:00. Save hotkey is taken", NotificationStates.CaptureOn(300, Keys, "Save hotkey is taken").Body);
    }

    [Theory]
    [InlineData(0, "Save hotkey is taken", "Filling, up to 5:00. Save hotkey is taken")]
    [InlineData(0, "Save hotkey doesn't work", "Filling, up to 5:00. Save hotkey doesn't work")]
    [InlineData(130, "Save hotkey is taken", "2:10 of 5:00 kept. Save hotkey is taken")]
    [InlineData(130, "Save hotkey doesn't work", "2:10 of 5:00 kept. Save hotkey doesn't work")]
    [InlineData(300, "Save hotkey is taken", "Keeping the last 5:00. Save hotkey is taken")]
    [InlineData(300, "Save hotkey doesn't work", "Keeping the last 5:00. Save hotkey doesn't work")]
    public void A_refused_hotkey_replaces_the_keys_in_every_state(int kept, string problem, string body)
    {
        var content = NotificationStates.CaptureOn(300, Keys, problem, keptSeconds: kept);

        Assert.Equal(body, content.Body);
        Assert.Empty(content.Keys);
        Assert.Equal(NotificationPalette.Amber, content.Stripe);
        Assert.True(content.ClickOpensSettings);
    }

    [Fact]
    public void After_an_update_the_body_is_the_one_of_the_state()
    {
        var content = NotificationStates.CaptureOn(300, Keys, updatedTo: "0.2.0", keptSeconds: 0);

        Assert.Equal("Updated to 0.2.0", content.Title);
        Assert.Equal("Filling, up to 5:00. Save:", content.Body);
        Assert.Equal(NotificationKind.CaptureOn, content.Kind);
    }

    [Fact]
    public void A_memory_of_one_second_never_says_filling_once_it_holds_it()
    {
        Assert.Equal("Keeping the last 0:01. Save:", NotificationStates.CaptureOn(1, Keys, keptSeconds: 1).Body);
        Assert.Equal("Filling, up to 0:01. Save:", NotificationStates.CaptureOn(1, Keys, keptSeconds: 0).Body);
    }

    [Theory]
    [InlineData(0, "Nothing new is being recorded")]
    [InlineData(1, "0:01 kept. Nothing new is recorded")]
    [InlineData(130, "2:10 kept. Nothing new is recorded")]
    public void A_pause_says_what_can_still_be_saved(int kept, string body)
    {
        var content = NotificationStates.Paused(kept);

        Assert.Equal(NotificationKind.Paused, content.Kind);
        Assert.Equal("Capture paused", content.Title);
        Assert.Equal(body, content.Body);
    }

    [Fact]
    public void A_clip_shorter_than_asked_reads_as_before()
    {
        var clip = new ClipResult(["clip.mp3"], TimeSpan.FromSeconds(12), 1, false, Enumerable.Repeat(0.8f, 24).ToArray(), false);

        var content = NotificationStates.ForClip(clip, TimeSpan.FromSeconds(30), true, true, NotificationPalette.Lime);

        Assert.Equal("Clip saved (0:12 of 0:30)", content.Title);
        Assert.Equal("Copied. Paste", content.Body);
    }

    [Fact]
    public void What_waited_is_made_again_with_the_memory_of_now()
    {
        var settings = new AppSettings { BufferSeconds = 300 };
        settings.Hotkeys.SaveDefault = "F24";
        HotkeyResult[] good = [new(HotkeyAction.SaveDefault, "F24", HotkeyStatus.Registered, null)];
        var waited = NotificationStates.CaptureOn(300, "F24", keptSeconds: 0);

        Assert.Equal("2:10 of 5:00 kept. Save:", StatusTransitions.Fresh(waited, settings, good, keptSeconds: 130).Body);
        Assert.Equal("Keeping the last 5:00. Save:", StatusTransitions.Fresh(waited, settings, good, keptSeconds: 300).Body);
        Assert.Equal("Keeping up to 5:00. Save:", StatusTransitions.Fresh(waited, settings, good).Body);
    }
}
