using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>What a pass cut by a game leaves in memory, and a pass that threw leaves on disk: seen by what runs next.</summary>
public class UpdateFlowStoppedRetryTests
{
    private static readonly TimeSpan ThirtyMinutes = UpdateDecider.BusyRetry;

    private static readonly UpdateSurface Game = new(FullScreen: true, Presentation: false, ClipInProgress: false, SessionEnding: false);

    [Fact]
    public void Check_cut_by_a_game_is_tried_again_after_thirty_minutes()
    {
        using var rig = new FlowRig();
        rig.Transport.ManifestStatus = TransportStatus.Stopped;
        rig.Launch();
        rig.CheckFinds();
        Assert.Empty(rig.Host.Passes);

        // The game is over. A check time kept in memory would make the next check not due for a day.
        rig.Transport.ManifestStatus = TransportStatus.Completed;
        rig.Host.Now += ThirtyMinutes;
        rig.Host.FireLast();

        Assert.Single(rig.Host.Passes);
        rig.Host.FinishPass();
        Assert.Equal(2, rig.Transport.Urls.Count(url => url == UpdateUrlPolicy.ManifestUrl));
        Assert.Equal(rig.Host.Now, rig.State.LastCheck);
    }

    [Fact]
    public void Download_cut_by_a_game_is_tried_again_after_thirty_minutes()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.FireLast();
        rig.Host.FinishPass();
        Assert.Single(rig.Host.Passes);

        // The game starts between the check and the download.
        rig.Host.Surface = Game;
        rig.Host.FinishPass();
        Assert.Null(rig.State.Pending);
        Assert.Empty(rig.Host.Passes);

        rig.Host.Surface = default;
        rig.Host.Now += ThirtyMinutes;
        rig.Host.FireLast();

        Assert.Single(rig.Host.Passes);
        rig.Host.FinishPass();
        Assert.Equal(rig.PendingSetup, rig.State.Pending);
        Assert.Equal(2, rig.Transport.Urls.Count(url => url == ReleaseTransport.SetupUrl));
    }

    [Fact]
    public void Download_that_throws_fails_even_when_a_valid_Setup_is_on_disk()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.FireLast();
        rig.Host.ThrowInNextPass = new IOException("thrown on the pass thread");
        rig.Host.FinishPass();
        rig.PutSetup();

        rig.Host.FinishPass();

        Assert.Null(rig.State.Pending);
        Assert.Empty(rig.Host.Announced);
        Assert.Null(rig.Flow.MenuVersion);
        Assert.Equal("Download failed. It will try again tomorrow.", rig.Flow.View.StatusLine);
        Assert.Empty(rig.Host.Passes);
    }
}
