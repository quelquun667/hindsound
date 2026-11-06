using Hindsound.App;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class UpdateDeciderTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly UpdateVersion Old = new(0, 1, 0);
    private static readonly UpdateVersion Next = new(0, 2, 0);
    private static readonly UpdateVersion Later = new(0, 3, 0);

    /// <summary>Running for an hour, checked 10 minutes ago, nothing known and nothing busy: nothing to do.</summary>
    private static UpdateSituation Calm(UpdateEvent trigger = UpdateEvent.Tick, UpdateMode mode = UpdateMode.Auto) => new()
    {
        Event = trigger,
        Mode = mode,
        Now = Noon,
        AppStartedAt = Noon.AddHours(-1),
        LastCheck = Noon.AddMinutes(-10),
        Installed = Old,
    };

    private static UpdateAction Decide(UpdateSituation situation) => UpdateDecider.Decide(situation);


    private static IEnumerable<UpdateSituation> EachBusy(UpdateSituation calm)
    {
        yield return calm with { FullScreen = true };
        yield return calm with { Presentation = true };
        yield return calm with { ClipInProgress = true };
        yield return calm with { SessionEnding = true };
    }

    // ---- When to check

    [Fact]
    public void NothingIsDueSoNothingHappens()
    {
        Assert.Equal(UpdateAction.None, Decide(Calm()));
    }

    [Fact]
    public void TheFirstCheckEverWaitsTwoMinutesAfterTheLaunch()
    {
        var started = Noon;
        var situation = Calm() with { AppStartedAt = started, LastCheck = null };

        Assert.Equal(started.AddMinutes(2), UpdateDecider.NextCheckAt(UpdateMode.Auto, started, null, Noon));
        Assert.Equal(UpdateAction.None, Decide(situation with { Now = started.AddSeconds(119) }));
        Assert.Equal(UpdateAction.Check, Decide(situation with { Now = started.AddSeconds(120) }));
    }

    [Fact]
    public void AnOldCheckStillWaitsTwoMinutesAfterTheLaunch()
    {
        var situation = Calm() with { AppStartedAt = Noon, LastCheck = Noon.AddDays(-3) };

        Assert.Equal(UpdateAction.None, Decide(situation with { Now = Noon.AddSeconds(119) }));
        Assert.Equal(UpdateAction.Check, Decide(situation with { Now = Noon.AddSeconds(120) }));
    }

    [Fact]
    public void ACheckComesTwentyFourHoursAfterTheLastOne()
    {
        var last = Noon.AddHours(-10);
        var situation = Calm() with { AppStartedAt = Noon.AddDays(-5), LastCheck = last };

        Assert.Equal(last.AddHours(24), UpdateDecider.NextCheckAt(UpdateMode.Auto, situation.AppStartedAt, last, Noon));
        Assert.Equal(UpdateAction.None, Decide(situation with { Now = last.AddHours(24).AddSeconds(-1) }));
        Assert.Equal(UpdateAction.Check, Decide(situation with { Now = last.AddHours(24) }));
    }

    [Fact]
    public void ALastCheckInTheFutureDoesNotStopTheUpdates()
    {
        var situation = Calm() with { LastCheck = Noon.AddYears(5) };

        Assert.Equal(UpdateAction.Check, Decide(situation));
    }

    [Fact]
    public void AskChecksToo()
    {
        Assert.Equal(UpdateAction.Check, Decide(Calm(mode: UpdateMode.Ask) with { LastCheck = null }));
    }

    // ---- Off

    [Fact]
    public void OffHasNoTimer()
    {
        Assert.Null(UpdateDecider.NextCheckAt(UpdateMode.Off, Noon, null, Noon));
    }

    [Fact]
    public void OffNeverTouchesTheNetworkByItself()
    {
        var situation = Calm(mode: UpdateMode.Off) with { LastCheck = null, Manifest = Next };

        Assert.Equal(UpdateAction.None, Decide(situation));
        Assert.Equal(UpdateAction.None, Decide(situation with { Downloaded = Next }));
        Assert.Equal(UpdateAction.None, Decide(situation with { FullScreen = true }));
    }

    [Fact]
    public void OffStillAnswersCheckNowOnly()
    {
        var off = Calm(mode: UpdateMode.Off);

        Assert.Equal(UpdateAction.Check, Decide(off with { Event = UpdateEvent.CheckNowClicked }));
        Assert.Equal(UpdateAction.None, Decide(off with { Event = UpdateEvent.UpdateClicked, Manifest = Next }));
    }

    [Fact]
    public void OffInstallsNothingOnLeaving()
    {
        var off = Calm(mode: UpdateMode.Off) with { Downloaded = Next };

        Assert.Equal(UpdateAction.None, Decide(off with { Event = UpdateEvent.Quitting }));
        Assert.Equal(UpdateAction.None, Decide(off with { Event = UpdateEvent.Launching }));
    }

    // ---- Never during a game

    [Fact]
    public void ADueCheckWaitsThirtyMinutesWhileBusy()
    {
        foreach (var busy in EachBusy(Calm() with { LastCheck = null }))
        {
            Assert.Equal(UpdateAction.WaitThirtyMinutes, Decide(busy));
        }
    }

    [Fact]
    public void ADueDownloadWaitsThirtyMinutesWhileBusy()
    {
        foreach (var busy in EachBusy(Calm() with { Manifest = Next }))
        {
            Assert.Equal(UpdateAction.WaitThirtyMinutes, Decide(busy));
        }
    }

    [Fact]
    public void ADueAnnouncementWaitsThirtyMinutesWhileBusy()
    {
        foreach (var busy in EachBusy(Calm() with { Manifest = Next, Downloaded = Next }))
        {
            Assert.Equal(UpdateAction.WaitThirtyMinutes, Decide(busy));
        }
    }

    [Fact]
    public void BusyWithNothingToDoStaysNothing()
    {
        foreach (var busy in EachBusy(Calm()))
        {
            Assert.Equal(UpdateAction.None, Decide(busy));
        }
    }

    [Fact]
    public void TheRetryDelayIsThirtyMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), UpdateDecider.BusyRetry);
    }

    [Theory]
    [InlineData(UserNotificationState.Busy, false, true)]
    [InlineData(UserNotificationState.RunningD3dFullScreen, false, true)]
    [InlineData(UserNotificationState.NotPresent, false, true)]
    [InlineData(UserNotificationState.Unknown, false, true)]
    [InlineData(UserNotificationState.AcceptsNotifications, true, true)]
    [InlineData(UserNotificationState.AcceptsNotifications, false, false)]
    [InlineData(UserNotificationState.QuietTime, false, false)]
    [InlineData(UserNotificationState.App, false, false)]
    [InlineData(UserNotificationState.PresentationMode, false, false)]
    public void TheNotificationStateTellsAGame(UserNotificationState state, bool covers, bool expected)
    {
        Assert.Equal(expected, UpdateDecider.IsFullScreen(state, covers));
    }

    [Theory]
    [InlineData(UserNotificationState.PresentationMode, true)]
    [InlineData(UserNotificationState.AcceptsNotifications, false)]
    public void TheNotificationStateTellsAPresentation(UserNotificationState state, bool expected)
    {
        Assert.Equal(expected, UpdateDecider.IsPresentation(state));
    }

    // ---- The automatic chain

    [Fact]
    public void AutoDownloadsANewVersion()
    {
        Assert.Equal(UpdateAction.Download, Decide(Calm() with { Manifest = Next }));
    }

    [Fact]
    public void AutoDownloadsANewerVersionOverAnOlderSetup()
    {
        Assert.Equal(UpdateAction.Download, Decide(Calm() with { Manifest = Later, Downloaded = Next }));
    }

    [Fact]
    public void AutoAnnouncesOnceTheFileIsThere()
    {
        Assert.Equal(UpdateAction.Announce, Decide(Calm() with { Manifest = Next, Downloaded = Next }));
    }

    [Fact]
    public void AnnouncedOnceIsAnnouncedForGood()
    {
        var known = Calm() with { Manifest = Next, Downloaded = Next, Announced = Next };

        Assert.Equal(UpdateAction.None, Decide(known));
    }

    [Fact]
    public void ANewerVersionIsAnnouncedInItsTurn()
    {
        var known = Calm() with { Manifest = Later, Downloaded = Later, Announced = Next };

        Assert.Equal(UpdateAction.Announce, Decide(known));
    }

    [Fact]
    public void AskAnnouncesWithoutDownloading()
    {
        var ask = Calm(mode: UpdateMode.Ask) with { Manifest = Next };

        Assert.Equal(UpdateAction.Announce, Decide(ask));
        Assert.Equal(UpdateAction.None, Decide(ask with { Announced = Next }));
    }

    [Fact]
    public void ADueCheckComesBeforeAKnownVersion()
    {
        Assert.Equal(UpdateAction.Check, Decide(Calm() with { LastCheck = null, Manifest = Next }));
    }

    [Fact]
    public void ATickNeverApplies()
    {
        var ready = Calm() with { Manifest = Next, Downloaded = Next, Announced = Next };

        Assert.Equal(UpdateAction.None, Decide(ready));
        Assert.Equal(UpdateAction.None, Decide(ready with { Mode = UpdateMode.Ask }));
    }

    // ---- Anti-rollback

    [Fact]
    public void TheInstalledVersionIsNoUpdate()
    {
        Assert.Equal(UpdateAction.None, Decide(Calm() with { Manifest = Old }));
    }

    [Fact]
    public void AnOlderVersionIsNoUpdate()
    {
        Assert.Equal(UpdateAction.None, Decide(Calm() with { Installed = Next, Manifest = Old }));
    }

    [Fact]
    public void AVersionBelowTheHighestSeenIsRefused()
    {
        var replayed = Calm() with { Manifest = Next, HighestSeen = Later };

        Assert.Equal(UpdateAction.None, Decide(replayed));
        Assert.Equal(UpdateAction.None, Decide(replayed with { Event = UpdateEvent.UpdateClicked }));
        Assert.Equal(UpdateAction.None, Decide(replayed with { Event = UpdateEvent.UpdateClicked, Manifest = null, Downloaded = Next }));
        Assert.Equal(UpdateAction.None, Decide(replayed with { Event = UpdateEvent.Quitting, Downloaded = Next }));
        Assert.Equal(UpdateAction.None, Decide(replayed with { Event = UpdateEvent.Launching, Downloaded = Next }));
    }

    [Fact]
    public void TheHighestSeenItselfIsAccepted()
    {
        Assert.True(UpdateDecider.IsAcceptable(Next, Old, Next));
        Assert.Equal(UpdateAction.Download, Decide(Calm() with { Manifest = Next, HighestSeen = Next }));
    }

    [Theory]
    [InlineData(0, 1, 0, false)]
    [InlineData(0, 0, 9, false)]
    [InlineData(0, 1, 1, true)]
    public void AcceptableMeansStrictlyAboveTheInstalledOne(int major, int minor, int patch, bool expected)
    {
        Assert.Equal(expected, UpdateDecider.IsAcceptable(new UpdateVersion(major, minor, patch), Old, null));
    }

    [Fact]
    public void AnOldSetupOnDiskIsNotInstalledOverANewerApp()
    {
        var updated = Calm() with { Installed = Next, Downloaded = Next };

        Assert.Equal(UpdateAction.None, Decide(updated with { Event = UpdateEvent.Launching }));
        Assert.Equal(UpdateAction.None, Decide(updated with { Event = UpdateEvent.Quitting }));
        Assert.Equal(UpdateAction.None, Decide(updated with { Event = UpdateEvent.UpdateClicked }));
    }

    // ---- Skipped version

    [Fact]
    public void ASkippedVersionIsNeitherDownloadedNorAnnounced()
    {
        var skipped = Calm() with { Manifest = Next, Skipped = Next };

        Assert.Equal(UpdateAction.None, Decide(skipped));
        Assert.Equal(UpdateAction.None, Decide(skipped with { Mode = UpdateMode.Ask }));
    }

    [Fact]
    public void ASkippedVersionIsNotInstalledOnLeaving()
    {
        var skipped = Calm() with { Downloaded = Next, Skipped = Next };

        Assert.Equal(UpdateAction.None, Decide(skipped with { Event = UpdateEvent.Quitting }));
        Assert.Equal(UpdateAction.None, Decide(skipped with { Event = UpdateEvent.Launching }));
    }

    [Fact]
    public void CheckNowAndTheButtonStillReachASkippedVersion()
    {
        var skipped = Calm() with { Manifest = Next, Skipped = Next };

        Assert.Equal(UpdateAction.Check, Decide(skipped with { Event = UpdateEvent.CheckNowClicked }));
        Assert.True(UpdateDecider.IsAcceptable(Next, Old, null));
        Assert.Equal(UpdateAction.Download, Decide(skipped with { Event = UpdateEvent.UpdateClicked }));
    }

    [Fact]
    public void AHigherVersionEndsTheSkip()
    {
        Assert.Null(UpdateDecider.SkippedAfter(Next, Later));
        Assert.Equal(Next, UpdateDecider.SkippedAfter(Next, Next));
        Assert.Equal(Later, UpdateDecider.SkippedAfter(Later, Next));
        Assert.Null(UpdateDecider.SkippedAfter(null, Next));
        Assert.Equal(UpdateAction.Download, Decide(Calm() with { Manifest = Later, Skipped = Next }));
    }

    // ---- Applying: only on a click, on Quit, or at launch

    [Fact]
    public void AClickAppliesWhatIsOnDisk()
    {
        Assert.Equal(UpdateAction.Apply, Decide(Calm(UpdateEvent.UpdateClicked) with { Downloaded = Next }));
        Assert.Equal(UpdateAction.Apply, Decide(Calm(UpdateEvent.UpdateClicked, UpdateMode.Ask) with { Downloaded = Next, Manifest = Next }));
    }

    [Fact]
    public void AClickInAskDownloadsFirst()
    {
        Assert.Equal(UpdateAction.Download, Decide(Calm(UpdateEvent.UpdateClicked, UpdateMode.Ask) with { Manifest = Next }));
    }

    [Fact]
    public void AClickTakesTheNewerVersionOverAnOlderSetup()
    {
        Assert.Equal(UpdateAction.Download, Decide(Calm(UpdateEvent.UpdateClicked) with { Manifest = Later, Downloaded = Next }));
    }

    [Fact]
    public void AClickWithNothingKnownDoesNothing()
    {
        Assert.Equal(UpdateAction.None, Decide(Calm(UpdateEvent.UpdateClicked)));
    }

    [Fact]
    public void AClickIsDroppedWhileBusyAndNeverComesBackLater()
    {
        foreach (var busy in EachBusy(Calm(UpdateEvent.UpdateClicked) with { Downloaded = Next, Manifest = Next }))
        {
            Assert.Equal(UpdateAction.None, Decide(busy));
        }
    }

    [Fact]
    public void CheckNowIsDroppedWhileBusy()
    {
        foreach (var busy in EachBusy(Calm(UpdateEvent.CheckNowClicked)))
        {
            Assert.Equal(UpdateAction.None, Decide(busy));
        }
    }

    [Theory]
    [InlineData(UpdateEvent.Quitting)]
    [InlineData(UpdateEvent.Launching)]
    public void AutoAppliesOnLeavingAndAtLaunch(UpdateEvent trigger)
    {
        Assert.Equal(UpdateAction.Apply, Decide(Calm(trigger) with { Downloaded = Next }));
    }

    [Theory]
    [InlineData(UpdateEvent.Quitting)]
    [InlineData(UpdateEvent.Launching)]
    public void AskWaitsForTheClick(UpdateEvent trigger)
    {
        Assert.Equal(UpdateAction.None, Decide(Calm(trigger, UpdateMode.Ask) with { Downloaded = Next }));
    }

    [Theory]
    [InlineData(UpdateEvent.Quitting)]
    [InlineData(UpdateEvent.Launching)]
    public void NothingOnDiskMeansNothingToApply(UpdateEvent trigger)
    {
        Assert.Equal(UpdateAction.None, Decide(Calm(trigger) with { Manifest = Next }));
    }

    [Theory]
    [InlineData(UpdateEvent.Quitting)]
    [InlineData(UpdateEvent.Launching)]
    public void NothingIsAppliedWhileBusy(UpdateEvent trigger)
    {
        foreach (var busy in EachBusy(Calm(trigger) with { Downloaded = Next }))
        {
            Assert.Equal(UpdateAction.None, Decide(busy));
        }
    }

    [Fact]
    public void QuitInstallsWithoutComingBack()
    {
        Assert.False(UpdateDecider.RestartAfterApply(UpdateEvent.Quitting));
        Assert.True(UpdateDecider.RestartAfterApply(UpdateEvent.UpdateClicked));
        Assert.True(UpdateDecider.RestartAfterApply(UpdateEvent.Launching));
    }

    // ---- Attempts

    private static Dictionary<string, int> Attempts(int count) => new() { [Next.ToString()] = count };

    [Theory]
    [InlineData(UpdateEvent.Quitting)]
    [InlineData(UpdateEvent.Launching)]
    public void TwoFailedAttemptsLeaveOneMore(UpdateEvent trigger)
    {
        Assert.Equal(UpdateAction.Apply, Decide(Calm(trigger) with { Downloaded = Next, AttemptsByVersion = Attempts(2) }));
    }

    [Theory]
    [InlineData(UpdateEvent.Quitting)]
    [InlineData(UpdateEvent.Launching)]
    public void ThreeFailedAttemptsStopTheAutomaticInstall(UpdateEvent trigger)
    {
        Assert.Equal(UpdateAction.None, Decide(Calm(trigger) with { Downloaded = Next, AttemptsByVersion = Attempts(3) }));
    }

    [Fact]
    public void ThreeFailedAttemptsStopTheChainToo()
    {
        Assert.Equal(UpdateAction.None, Decide(Calm() with { Manifest = Next, AttemptsByVersion = Attempts(3) }));
        Assert.Equal(UpdateAction.None, Decide(Calm() with { Manifest = Next, Downloaded = Next, AttemptsByVersion = Attempts(3) }));
    }

    [Fact]
    public void ThreeFailedAttemptsLeaveTheButton()
    {
        Assert.Equal(UpdateAction.Apply, Decide(Calm(UpdateEvent.UpdateClicked) with { Downloaded = Next, AttemptsByVersion = Attempts(3) }));
    }

    [Fact]
    public void AttemptsCountPerVersion()
    {
        Assert.Equal(UpdateAction.Apply, Decide(Calm(UpdateEvent.Quitting) with { Downloaded = Later, AttemptsByVersion = Attempts(3) }));
    }

    // ---- At launch

    [Fact]
    public void TheVersionIsComparedAtLaunch()
    {
        Assert.Equal(LaunchOutcome.NoUpdateExpected, UpdateDecider.OutcomeAtLaunch(Old, null));
        Assert.Equal(LaunchOutcome.Failed, UpdateDecider.OutcomeAtLaunch(Old, Next));
        Assert.Equal(LaunchOutcome.Updated, UpdateDecider.OutcomeAtLaunch(Next, Next));
        Assert.Equal(LaunchOutcome.Updated, UpdateDecider.OutcomeAtLaunch(Later, Next));
    }

    [Fact]
    public void ALaunchAfterAFailedSetupDoesNotRetryAtOnce()
    {
        var failed = Calm(UpdateEvent.Launching) with { Downloaded = Next, Expected = Next, AttemptsByVersion = Attempts(1) };

        Assert.Equal(UpdateAction.None, Decide(failed));
    }

    [Fact]
    public void QuitAfterAFailedSetupTriesAgain()
    {
        var failed = Calm(UpdateEvent.Quitting) with { Downloaded = Next, Expected = Next, AttemptsByVersion = Attempts(1) };

        Assert.Equal(UpdateAction.Apply, Decide(failed));
    }
}
