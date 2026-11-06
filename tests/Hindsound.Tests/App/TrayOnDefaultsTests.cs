using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>A settings file that stays unreadable: the notification goes after a few seconds, the icon has to keep saying it.</summary>
public class TrayOnDefaultsTests
{
    private const string Said = "Hindsound - " + StatusTransitions.OnDefaultsProblem;
    private static readonly HotkeyResult[] AllGood = [];

    private static AudioEngineStatus Status(CaptureActivity activity = CaptureActivity.Capturing) =>
        new(activity, true, false, false, "Speakers", null, true, TimeSpan.Zero, 0);

    [Fact]
    public void The_icon_and_the_tip_say_it()
    {
        Assert.Equal((TrayState.Error, Said), StatusTransitions.TrayOf(Status(), AllGood, false, onDefaults: true));
    }

    [Fact]
    public void Otherwise_nothing_changes()
    {
        Assert.Equal((TrayState.Active, "Hindsound"), StatusTransitions.TrayOf(Status(), AllGood, false));
    }

    [Fact]
    public void A_pause_still_reads_as_a_problem_while_on_the_defaults()
    {
        Assert.Equal((TrayState.Error, Said), StatusTransitions.TrayOf(Status(CaptureActivity.Paused), AllGood, false, onDefaults: true));
    }

    [Fact]
    public void No_device_and_a_clip_with_a_problem_are_named_first()
    {
        Assert.Equal((TrayState.Error, "Hindsound - No audio device"), StatusTransitions.TrayOf(Status(CaptureActivity.NoDevice), AllGood, false, onDefaults: true));
        Assert.Equal((TrayState.Error, "Hindsound - A clip has a problem"), StatusTransitions.TrayOf(Status(), AllGood, true, onDefaults: true));
    }

    [Fact]
    public void Leaving_says_leaving()
    {
        Assert.Equal(StatusTransitions.ClosingTip, StatusTransitions.TrayOf(Status(), AllGood, false, closingAfterClip: true, onDefaults: true).Tip);
    }

    [Fact]
    public void It_ends_with_the_first_real_read()
    {
        var file = new SettingsFileState();
        file.Started(SettingsLoadStatus.Unreadable, null);
        Assert.Equal(Said, StatusTransitions.TrayOf(Status(), AllGood, false, onDefaults: file.StillOnDefaults).Tip);

        file.Reread(SettingsLoadStatus.Corrupt, default);
        Assert.Equal(Said, StatusTransitions.TrayOf(Status(), AllGood, false, onDefaults: file.StillOnDefaults).Tip);

        file.Reread(SettingsLoadStatus.Loaded, default);
        Assert.Equal((TrayState.Active, "Hindsound"), StatusTransitions.TrayOf(Status(), AllGood, false, onDefaults: file.StillOnDefaults));
    }
}
