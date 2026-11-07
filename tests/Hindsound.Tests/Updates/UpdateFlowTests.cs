using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class UpdateFlowTests
{
    private static readonly UpdateVersion Next = FlowRig.Next;
    private static readonly int ThirtyMinutes = (int)UpdateDecider.BusyRetry.TotalMilliseconds;
    private static readonly int AlmostADay = (int)TimeSpan.FromHours(23).TotalMilliseconds;

    private static readonly UpdateSurface Game = new(FullScreen: true, Presentation: false, ClipInProgress: false, SessionEnding: false);
    private static readonly UpdateSurface Clip = new(FullScreen: false, Presentation: false, ClipInProgress: true, SessionEnding: false);
    private static readonly UpdateSurface SessionEnd = new(FullScreen: false, Presentation: false, ClipInProgress: false, SessionEnding: true);

    // ---- Rule 1: the state, read once

    [Fact]
    public void Missing_folder_is_a_known_empty_state()
    {
        using var rig = new FlowRig(Path.Combine("never", "made"));

        Assert.True(rig.Store.TryLoad(out var state));
        Assert.Equal(new UpdateStateData(), state);

        rig.Launch();
        rig.Flow.CheckNow();

        Assert.Single(rig.Host.Passes);
    }

    [Fact]
    public void Locked_state_is_never_written_over()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { HighestSeen = "0.2.0", AttemptsByVersion = new() { ["0.2.0"] = 2 }, Pending = rig.PendingSetup });
        rig.PutSetup();
        var before = File.ReadAllBytes(rig.StatePath);

        LaunchReport report;
        using (new FileStream(rig.StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            report = rig.Flow.AtLaunch(updateFailedFlag: false, default);
        }

        rig.Flow.Start();
        rig.Flow.CheckNow();
        var click = rig.Flow.UpdateClicked();
        rig.Flow.SkipThisVersion();
        var quit = rig.Flow.ApplyOnQuit();

        Assert.False(report.SetupStarted);
        Assert.Equal(new ClickAnswer(ClickKind.Nothing), click);
        Assert.False(quit);
        Assert.Empty(rig.Host.Passes);
        Assert.Empty(rig.Started);
        Assert.Empty(rig.Host.SkippedWrites);
        Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
        Assert.False(rig.Flow.View.CheckEnabled);
        Assert.Null(rig.Flow.MenuVersion);
    }

    [Fact]
    public void Locked_state_with_the_flag_still_says_failed()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData());

        LaunchReport report;
        using (new FileStream(rig.StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            report = rig.Flow.AtLaunch(updateFailedFlag: true, default);
        }

        Assert.Equal(LaunchOutcome.Failed, report.Outcome);
        Assert.False(report.SetupStarted);
    }

    [Fact]
    public void Locked_state_is_read_again_thirty_minutes_later_without_setup()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.PutSetup();

        using (new FileStream(rig.StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            rig.Flow.AtLaunch(updateFailedFlag: false, default);
            rig.Flow.Start();
            Assert.Equal(ThirtyMinutes, rig.Host.LastDelay);
        }

        rig.Host.FireLast();

        Assert.Empty(rig.Started);
        Assert.Single(rig.Host.Passes);
        Assert.Equal(Next, rig.Flow.MenuVersion);
    }

    // ---- Rule 2: the runner reads the file

    [Fact]
    public void Apply_refuses_when_state_cannot_be_written()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.PutSetup();
        rig.Launch();

        // The disk no longer holds what memory holds, and cannot take it.
        File.WriteAllText(rig.StatePath, "{}");
        rig.BlockSaves();
        var click = rig.Flow.UpdateClicked();

        Assert.Equal(new ClickAnswer(ClickKind.NotStarted, RunStatus.StateNotSaved), click);
        Assert.Empty(rig.Started);
        Assert.Equal("Couldn't write the update state. Nothing changed.", rig.Flow.View.CardLine);
    }

    [Fact]
    public void Apply_writes_memory_before_the_runner_reads_it()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.PutSetup();
        rig.Launch();
        File.WriteAllText(rig.StatePath, "{}");

        var click = rig.Flow.UpdateClicked();

        Assert.Equal(new ClickAnswer(ClickKind.Applied), click);
        var setup = Assert.Single(rig.Started);
        Assert.Contains("/RESTARTAPP", setup.ArgumentList);
        Assert.Equal(1, rig.State.AttemptsFor(Next));
    }

    [Fact]
    public void A_changed_setup_is_dropped_from_memory_too()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup with { Sha256 = new string('0', 64) } });
        rig.PutSetup();
        rig.Launch();

        var click = rig.Flow.UpdateClicked();

        Assert.Equal(new ClickAnswer(ClickKind.NotStarted, RunStatus.SetupChanged), click);
        Assert.Equal("Couldn't open the update file. Try again in a moment.", rig.Flow.View.StatusLine);
        Assert.Null(rig.Flow.MenuVersion);
        Assert.Null(rig.State.Pending);
    }

    // ---- Rule 3: one timer, a floor, a generation

    [Fact]
    public void Off_arms_nothing()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Off;

        rig.Launch();
        rig.Flow.SettingsChanged();

        Assert.Empty(rig.Host.Timers);
    }

    [Fact]
    public void Check_now_reaches_the_network_in_off()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Off;
        rig.Launch();

        rig.Flow.CheckNow();
        rig.Host.FinishPass();

        Assert.Equal([UpdateUrlPolicy.ManifestUrl], rig.Transport.Urls);
        Assert.Empty(rig.Host.Timers);
        Assert.Empty(rig.Host.Announced);
        Assert.Equal("Version 0.2.0 is available. Switch to Ask me to download it.", rig.Flow.View.StatusLine);
    }

    [Fact]
    public void Timer_never_fires_below_a_minute()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { LastCheck = FlowRig.Now - TimeSpan.FromDays(400) });

        rig.Launch();

        Assert.Equal(60_000, rig.Host.LastDelay);
    }

    [Fact]
    public void First_timer_waits_for_the_next_due_check()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { LastCheck = FlowRig.Now - TimeSpan.FromHours(20) });

        rig.Launch();

        Assert.Equal((int)TimeSpan.FromHours(4).TotalMilliseconds, rig.Host.LastDelay);
    }

    [Fact]
    public void Old_timer_does_nothing()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Flow.SettingsChanged();
        Assert.Equal(2, rig.Host.Timers.Count);

        rig.Host.Timers[0].Fire();
        Assert.Empty(rig.Host.Passes);

        rig.Host.Timers[1].Fire();
        Assert.Single(rig.Host.Passes);
    }

    // ---- Rule 4: the step

    [Fact]
    public void First_run_card_puts_the_check_off()
    {
        using var rig = new FlowRig();
        rig.Host.FirstRunCardOpen = true;
        rig.Launch();

        rig.Host.FireLast();

        Assert.Empty(rig.Host.Passes);
        Assert.Equal((int)TimeSpan.FromMinutes(5).TotalMilliseconds, rig.Host.LastDelay);

        rig.Host.FirstRunCardOpen = false;
        rig.Host.FireLast();
        Assert.Single(rig.Host.Passes);
    }

    [Fact]
    public void Auto_checks_downloads_then_announces()
    {
        using var rig = new FlowRig();
        rig.Launch();

        rig.CheckFinds();
        rig.Host.FinishPass();

        Assert.Equal([UpdateUrlPolicy.ManifestUrl, ReleaseTransport.SetupUrl], rig.Transport.Urls);
        Assert.Equal([(Next, UpdateMode.Auto)], rig.Host.Announced);
        Assert.Equal(rig.PendingSetup, rig.State.Pending);
        Assert.Equal("0.2.0", rig.State.HighestSeen);
        Assert.Equal(Next, rig.Flow.MenuVersion);
        Assert.Equal("Version 0.2.0 is ready. It installs when Hindsound closes.", rig.Flow.View.StatusLine);
        Assert.True(rig.Host.LastDelay >= AlmostADay);
    }

    [Fact]
    public void Game_keeps_the_announcement_waiting()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.Host.FireLast();

        // The check got its answer just before the game; the step after it meets the game.
        rig.Host.Surface = Game;
        rig.Host.FinishPass();

        Assert.Equal([(Next, UpdateMode.Ask)], rig.Host.Announced);
        Assert.Equal(ThirtyMinutes, rig.Host.LastDelay);
        Assert.Contains("update: busy, waits 30 min", rig.Host.Lines);
        Assert.Null(rig.State.AnnouncedVersion);
    }

    [Fact]
    public void Waiting_announcement_is_not_added_twice()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.Host.FireLast();
        rig.Host.Surface = Game;
        rig.Host.FinishPass();

        rig.Host.FireLast();
        rig.Host.FireLast();

        Assert.Single(rig.Host.Announced);
        Assert.Equal(ThirtyMinutes, rig.Host.LastDelay);
    }

    [Fact]
    public void Announce_counts_once_shown()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.CheckFinds();

        Assert.Single(rig.Host.Announced);
        Assert.Null(rig.State.AnnouncedVersion);

        rig.Flow.Shown();

        Assert.Equal("0.2.0", rig.State.AnnouncedVersion);
    }

    [Fact]
    public void Announced_version_is_not_announced_again_after_a_restart()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.WriteState(new UpdateStateData { AnnouncedVersion = "0.2.0" });
        rig.Launch();

        rig.CheckFinds();

        Assert.Empty(rig.Host.Announced);
    }

    // ---- Rule 5: the end of a check

    [Fact]
    public void Failed_check_is_not_retried_at_once()
    {
        using var rig = new FlowRig();
        rig.Transport.ManifestStatus = TransportStatus.Failed;
        rig.Launch();

        rig.CheckFinds();

        Assert.True(rig.Host.LastDelay >= AlmostADay);
        Assert.Equal(FlowRig.Now, rig.State.LastCheck);
        Assert.Equal("Couldn't check. It will try again tomorrow.", rig.Flow.View.StatusLine);
    }

    [Fact]
    public void Check_time_stays_in_memory()
    {
        using var rig = new FlowRig();
        rig.Transport.ManifestStatus = TransportStatus.Failed;
        rig.Launch();
        rig.BlockSaves();

        rig.CheckFinds();

        Assert.Null(rig.State.LastCheck);
        Assert.True(rig.Host.LastDelay >= AlmostADay);
    }

    [Fact]
    public void Stopped_check_waits_thirty_minutes()
    {
        using var rig = new FlowRig();
        rig.Transport.ManifestStatus = TransportStatus.Stopped;
        rig.Launch();

        rig.CheckFinds();

        Assert.Equal(ThirtyMinutes, rig.Host.LastDelay);
        Assert.Null(rig.State.LastCheck);
        Assert.Equal("Paused by a game or a clip. It will try again later.", rig.Flow.View.StatusLine);
    }

    [Fact]
    public void A_higher_version_clears_the_skipped_one()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Host.Skipped = new UpdateVersion(0, 1, 5);
        rig.Launch();

        rig.CheckFinds();

        Assert.Equal(new UpdateVersion?[] { null }, rig.Host.SkippedWrites);
    }

    [Fact]
    public void The_same_skipped_version_stays_skipped()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Host.Skipped = Next;
        rig.Launch();

        rig.CheckFinds();

        Assert.Empty(rig.Host.SkippedWrites);
        Assert.Empty(rig.Host.Announced);
        Assert.Equal("Version 0.2.0 is skipped.", rig.Flow.View.StatusLine);
    }

    // ---- Rule 6: the end of a download

    [Fact]
    public void Ready_without_saved_pending_is_a_failure()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();
        rig.BlockSaves();

        rig.Host.FinishPass();

        Assert.Empty(rig.Host.Announced);
        Assert.Null(rig.Flow.MenuVersion);
        Assert.Null(rig.State.Pending);
        Assert.True(rig.Host.LastDelay >= AlmostADay);
    }

    [Fact]
    public void Failed_download_waits_for_the_next_check()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();
        rig.Transport.Setup = new byte[rig.Transport.Setup.Length];

        rig.Host.FinishPass();
        Assert.True(rig.Host.LastDelay >= AlmostADay);
        Assert.Equal("Download failed. It will try again tomorrow.", rig.Flow.View.StatusLine);

        rig.Flow.SettingsChanged();
        rig.Host.FireLast();

        Assert.Empty(rig.Host.Passes);
        Assert.Equal(2, rig.Transport.Urls.Count);
    }

    [Fact]
    public void Interrupted_download_keeps_the_manifest_for_thirty_minutes()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();
        rig.Host.Surface = Clip;

        rig.Host.FinishPass();
        Assert.Equal(ThirtyMinutes, rig.Host.LastDelay);

        rig.Host.Surface = default;
        rig.Host.FireLast();
        rig.Host.FinishPass();

        Assert.Equal(rig.PendingSetup, rig.State.Pending);
    }

    [Fact]
    public void Gone_setup_is_downloaded_again()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.Launch();

        rig.CheckFinds();

        Assert.Single(rig.Host.Passes);
        rig.Host.FinishPass();
        Assert.Equal(ReleaseTransport.SetupUrl, rig.Transport.Urls[^1]);
        Assert.Equal(rig.PendingSetup, rig.State.Pending);
    }

    // ---- Rule 7: one pass at a time

    [Fact]
    public void Two_clicks_run_one_pass()
    {
        using var rig = new FlowRig();
        rig.Launch();

        rig.Flow.CheckNow();
        rig.Flow.CheckNow();

        Assert.Single(rig.Host.Passes);
        Assert.False(rig.Flow.View.CheckEnabled);
        Assert.Equal(new ClickAnswer(ClickKind.Nothing), rig.Flow.UpdateClicked());
    }

    [Fact]
    public void Pass_that_throws_frees_the_flow()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.ThrowInNextPass = new IOException("thrown on the pass thread");
        rig.Flow.CheckNow();

        rig.Host.FinishPass();
        rig.Flow.CheckNow();

        Assert.Single(rig.Host.Passes);
    }

    [Fact]
    public void Pass_that_throws_is_not_a_success()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.FireLast();
        rig.Host.ThrowInNextPass = new IOException("thrown on the pass thread");
        rig.Host.FinishPass();

        rig.Host.FinishPass();

        Assert.Null(rig.State.Pending);
        Assert.Empty(rig.Host.Announced);
        Assert.Null(rig.Flow.MenuVersion);
        Assert.Equal("Download failed. It will try again tomorrow.", rig.Flow.View.StatusLine);
    }

    [Fact]
    public void Check_that_throws_is_a_failed_check()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.ThrowInNextPass = new InvalidOperationException();
        rig.Host.FireLast();

        rig.Host.FinishPass();

        Assert.Equal(FlowRig.Now, rig.State.LastCheck);
        Assert.True(rig.Host.LastDelay >= AlmostADay);
    }

    // ---- Rule 8: the click in Ask me

    [Fact]
    public void Click_downloads_then_applies()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.CheckFinds();

        Assert.Equal(new ClickAnswer(ClickKind.Downloading), rig.Flow.UpdateClicked());
        Assert.Equal("Downloading 0.2.0...", rig.Flow.View.CardLine);
        Assert.True(rig.Flow.View.CardBusy);

        rig.Host.FinishPass();

        var setup = Assert.Single(rig.Started);
        Assert.Contains("/RESTARTAPP", setup.ArgumentList);
        Assert.True(rig.Host.Left);
    }

    [Fact]
    public void Closed_card_never_restarts()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.CheckFinds();
        rig.Flow.UpdateClicked();

        rig.Flow.CardClosed();
        rig.Host.FinishPass();

        Assert.Empty(rig.Started);
        Assert.False(rig.Host.Left);
        Assert.Equal(rig.PendingSetup, rig.State.Pending);

        // The Setup waits for the next click.
        Assert.Equal(new ClickAnswer(ClickKind.Applied), rig.Flow.UpdateClicked());
    }

    [Fact]
    public void Game_during_download_drops_the_click()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Transport.IgnoreStop = true;
        rig.Launch();
        rig.CheckFinds();
        rig.Flow.UpdateClicked();

        rig.Host.Surface = Clip;
        rig.Host.FinishPass();

        Assert.Empty(rig.Started);
        Assert.False(rig.Host.Left);
        Assert.Equal("Not now: a game or a clip is running.", rig.Flow.View.CardLine);
    }

    [Fact]
    public void Download_stopped_by_a_game_drops_the_click()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.CheckFinds();
        rig.Flow.UpdateClicked();

        rig.Host.Surface = Game;
        rig.Host.FinishPass();
        rig.Host.Surface = default;
        rig.Host.FireLast();

        Assert.Empty(rig.Started);
        Assert.Equal("Not now: a game or a clip is running.", rig.Flow.View.CardLine);
    }

    [Fact]
    public void Failed_download_tells_the_card()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.CheckFinds();
        rig.Flow.UpdateClicked();
        rig.Transport.Setup = new byte[rig.Transport.Setup.Length];

        rig.Host.FinishPass();

        Assert.Empty(rig.Started);
        Assert.Equal("Download failed. Nothing changed.", rig.Flow.View.CardLine);
    }

    [Fact]
    public void Click_during_a_game_is_not_now()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.CheckFinds();
        rig.Host.Surface = Game;

        Assert.Equal(new ClickAnswer(ClickKind.NotNow), rig.Flow.UpdateClicked());
        Assert.Empty(rig.Host.Passes);
    }

    // ---- Rule 9: stopping a pass

    [Fact]
    public void Off_stops_an_automatic_pass()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.FireLast();

        rig.Host.Mode = UpdateMode.Off;
        rig.Flow.SettingsChanged();
        rig.Host.FinishPass();

        Assert.Null(rig.State.LastCheck);
    }

    [Fact]
    public void Off_does_not_stop_check_now()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Flow.CheckNow();

        rig.Host.Mode = UpdateMode.Off;
        rig.Flow.SettingsChanged();
        rig.Host.FinishPass();

        Assert.Equal("0.2.0", rig.State.HighestSeen);
    }

    [Fact]
    public void Pass_runs_again_after_off_and_back()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.Launch();
        rig.Host.FireLast();
        rig.Host.Mode = UpdateMode.Off;
        rig.Flow.SettingsChanged();
        rig.Host.FinishPass();

        rig.Host.Mode = UpdateMode.Ask;
        rig.Flow.SettingsChanged();
        rig.Host.FireLast();
        rig.Host.FinishPass();

        Assert.Equal("0.2.0", rig.State.HighestSeen);
        Assert.Single(rig.Host.Announced);
    }

    [Fact]
    public void Stop_leaves_a_running_pass_behind()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.Host.FireLast();

        rig.Flow.Stop();
        rig.Host.FinishPass();

        Assert.Null(rig.State.LastCheck);
        Assert.Single(rig.Host.Timers);
    }

    // ---- Rule 11: the Setup on disk, never Pending alone

    [Fact]
    public void Gone_setup_is_not_applied_on_quit()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.Launch();

        Assert.False(rig.Flow.ApplyOnQuit());
        Assert.Empty(rig.Started);
    }

    // ---- Rule 12: Quit

    [Fact]
    public void Quit_applies_the_waiting_setup_without_restart()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();
        rig.Host.FinishPass();

        Assert.True(rig.Flow.ApplyOnQuit());

        var setup = Assert.Single(rig.Started);
        Assert.DoesNotContain("/RESTARTAPP", setup.ArgumentList);
        Assert.Equal("0.2.0", rig.State.ExpectedVersion);
    }

    [Fact]
    public void Quit_applies_even_during_a_pass()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.PutSetup();
        rig.Launch(surface: Game);
        rig.Flow.CheckNow();
        Assert.Single(rig.Host.Passes);

        Assert.True(rig.Flow.ApplyOnQuit());

        Assert.Single(rig.Started);
        rig.Host.FinishPass();
        Assert.Null(rig.State.LastCheck);
    }

    [Fact]
    public void Quit_in_ask_installs_nothing()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.PutSetup();
        rig.Launch();

        Assert.False(rig.Flow.ApplyOnQuit());
        Assert.Empty(rig.Started);
    }

    [Fact]
    public void Session_end_applies_nothing()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.PutSetup();
        var report = rig.Launch(surface: SessionEnd);
        rig.Host.Surface = SessionEnd;

        Assert.False(report.SetupStarted);
        Assert.Equal(new ClickAnswer(ClickKind.NotNow), rig.Flow.UpdateClicked());
        Assert.False(rig.Flow.ApplyOnQuit());
        Assert.Empty(rig.Started);
    }

    // ---- At launch

    [Fact]
    public void Launch_applies_a_waiting_setup_and_restarts()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.PutSetup();

        var report = rig.Flow.AtLaunch(updateFailedFlag: false, default);

        Assert.True(report.SetupStarted);
        var setup = Assert.Single(rig.Started);
        Assert.Contains("/RESTARTAPP", setup.ArgumentList);
    }

    [Fact]
    public void Failed_launch_does_not_apply_again()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup, ExpectedVersion = "0.2.0", AttemptsByVersion = new() { ["0.2.0"] = 1 } });
        rig.PutSetup();

        var report = rig.Flow.AtLaunch(updateFailedFlag: false, default);

        Assert.Equal(new LaunchReport(false, LaunchOutcome.Failed, null, WillRetry: true), report);
        Assert.Empty(rig.Started);
        Assert.Null(rig.State.ExpectedVersion);
        Assert.Equal(rig.PendingSetup, rig.State.Pending);
        Assert.True(File.Exists(UpdateFiles.SetupPath(rig.Folder, Next)));
    }

    [Fact]
    public void Failed_launch_after_three_attempts_will_not_retry()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup, ExpectedVersion = "0.2.0", AttemptsByVersion = new() { ["0.2.0"] = 3 } });
        rig.PutSetup();

        Assert.False(rig.Flow.AtLaunch(updateFailedFlag: false, default).WillRetry);
    }

    [Fact]
    public void Failed_launch_in_ask_will_not_retry()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Ask;
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup, ExpectedVersion = "0.2.0", AttemptsByVersion = new() { ["0.2.0"] = 1 } });
        rig.PutSetup();

        Assert.False(rig.Flow.AtLaunch(updateFailedFlag: false, default).WillRetry);
    }

    [Fact]
    public void Flag_alone_applies_nothing()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { Pending = rig.PendingSetup });
        rig.PutSetup();

        var report = rig.Flow.AtLaunch(updateFailedFlag: true, default);

        Assert.Equal(LaunchOutcome.Failed, report.Outcome);
        Assert.False(report.SetupStarted);
        Assert.Empty(rig.Started);
    }

    [Fact]
    public void Reached_version_reports_the_update_and_clears_the_state()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { ExpectedVersion = "0.1.0", Pending = rig.PendingSetup with { Version = "0.1.0" } });

        var report = rig.Flow.AtLaunch(updateFailedFlag: false, default);

        Assert.Equal(new LaunchReport(false, LaunchOutcome.Updated, FlowRig.Installed, false), report);
        Assert.Null(rig.State.ExpectedVersion);
        Assert.Null(rig.State.Pending);
    }

    [Fact]
    public void Launch_writes_nothing_when_nothing_changes()
    {
        using var rig = new FlowRig();
        rig.WriteState(new UpdateStateData { HighestSeen = "0.1.0" });
        File.SetLastWriteTimeUtc(rig.StatePath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        rig.Launch();

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(rig.StatePath));
    }

    [Fact]
    public void Launch_removes_a_piece_left_behind()
    {
        using var rig = new FlowRig();
        rig.Host.Mode = UpdateMode.Off;
        File.WriteAllBytes(Path.Combine(rig.Folder, "setup-0.2.0.exe.part"), [1, 2]);

        rig.Launch();

        Assert.Empty(Directory.GetFiles(rig.Folder));
    }

    // ---- Skip this version

    [Fact]
    public void Skip_this_version_forgets_it_everywhere()
    {
        using var rig = new FlowRig();
        rig.Launch();
        rig.CheckFinds();
        rig.Host.FinishPass();

        rig.Flow.SkipThisVersion();

        Assert.Equal(Next, rig.Host.Skipped);
        Assert.Equal(1, rig.Host.Withdrawn);
        Assert.Null(rig.State.Pending);
        Assert.False(File.Exists(UpdateFiles.SetupPath(rig.Folder, Next)));
        Assert.Null(rig.Flow.MenuVersion);
        Assert.False(rig.Flow.ApplyOnQuit());
    }
}
