using Hindsound.App;
using Hindsound.Tests.Updates;

namespace Hindsound.Tests.App;

/// <summary>The two decisions of the app that feed the updater: the end of the session, and the start after the first-launch card.</summary>
public class UpdateGlueTests
{
    [Fact]
    public void Session_is_not_ending_at_first()
    {
        Assert.False(new SessionEndBlock().Ending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Question_of_windows_ends_the_session_with_or_without_clips(bool clipsBusy)
    {
        var block = new SessionEndBlock();

        block.Asked(clipsBusy);

        Assert.True(block.Ending);
    }

    [Fact]
    public void Session_that_goes_on_is_no_longer_ending()
    {
        var block = new SessionEndBlock();
        block.Asked(clipsBusy: true);

        block.Over();

        Assert.False(block.Ending);
    }

    [Fact]
    public void Updates_start_after_the_first_launch_card()
    {
        using var rig = new FlowRig();
        rig.Flow.AtLaunch(updateFailedFlag: false, default);

        UpdateWiring.StartAfterLaunch(rig.Flow, firstRunCardFailed: false);

        Assert.Single(rig.Host.Timers);
    }

    [Fact]
    public void A_first_launch_card_that_failed_starts_nothing()
    {
        using var rig = new FlowRig();
        rig.Flow.AtLaunch(updateFailedFlag: false, default);

        UpdateWiring.StartAfterLaunch(rig.Flow, firstRunCardFailed: true);

        Assert.Empty(rig.Host.Timers);
        rig.Flow.CheckNow();
        Assert.Single(rig.Host.Passes);
    }

    [Fact]
    public void No_updater_starts_nothing()
    {
        UpdateWiring.StartAfterLaunch(null, firstRunCardFailed: false);
    }
}
