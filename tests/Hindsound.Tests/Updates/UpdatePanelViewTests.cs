using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class UpdatePanelViewTests
{
    private static readonly UpdateVersion Installed = new(0, 1, 0);
    private static readonly UpdateVersion Next = new(0, 2, 0);
    private static readonly TimeSpan Paris = TimeSpan.FromHours(1);
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 18, 30, 0, Paris);

    private static UpdatePanelFacts Facts(UpdateMode mode = UpdateMode.Auto) => new()
    {
        Installed = Installed,
        Mode = mode,
        Now = Now,
        LastCheck = new DateTimeOffset(2026, 11, 7, 14, 2, 0, Paris),
    };

    private static UpdatePanelView View(UpdatePanelFacts facts) => UpdatePanel.Compute(facts);

    // ---- The status lines

    [Fact]
    public void A_copy_that_was_not_installed_says_so_and_keeps_its_version()
    {
        var view = UpdatePanel.Inactive(Installed);

        Assert.Equal("This copy was not installed, so it does not update itself.", view.StatusLine);
        Assert.Equal("Version 0.1.0", view.VersionLine);
        Assert.False(view.Active);
        Assert.False(view.CheckEnabled);
        Assert.False(view.ShowUpdate || view.ShowWhatsNew || view.ShowSkip || view.UpdateEnabled || view.SkipEnabled);
        Assert.Null(view.Offered);
    }

    [Fact]
    public void Off_and_never_checked()
    {
        Assert.Equal("Updates are off.", View(Facts(UpdateMode.Off) with { LastCheck = null }).StatusLine);
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("Ask")]
    public void Never_checked(string modeName)
    {
        var mode = Enum.Parse<UpdateMode>(modeName);

        Assert.Equal("Not checked yet.", View(Facts(mode) with { LastCheck = null }).StatusLine);
    }

    [Fact]
    public void A_running_check()
    {
        Assert.Equal("Checking...", View(Facts() with { Pass = UpdatePassKind.Check }).StatusLine);
    }

    [Fact]
    public void A_running_download()
    {
        Assert.Equal("Downloading 0.2.0...", View(Facts() with { Pass = UpdatePassKind.Download, PassVersion = Next, Manifest = Next }).StatusLine);
    }

    [Fact]
    public void Up_to_date_checked_today()
    {
        Assert.Equal("Up to date. Checked today 14:02", View(Facts()).StatusLine);
    }

    [Fact]
    public void Up_to_date_checked_today_in_another_offset_reads_in_the_local_one()
    {
        var facts = Facts() with { LastCheck = new DateTimeOffset(2026, 11, 7, 13, 2, 0, TimeSpan.Zero) };

        Assert.Equal("Up to date. Checked today 14:02", View(facts).StatusLine);
    }

    [Fact]
    public void Up_to_date_checked_yesterday()
    {
        var facts = Facts() with { LastCheck = new DateTimeOffset(2026, 11, 6, 9, 5, 0, Paris) };

        Assert.Equal("Up to date. Checked yesterday 09:05", View(facts).StatusLine);
    }

    [Fact]
    public void Up_to_date_checked_earlier_gives_the_date_in_digits()
    {
        var facts = Facts() with { LastCheck = new DateTimeOffset(2026, 11, 4, 23, 59, 0, Paris) };

        Assert.Equal("Up to date. Checked 2026-11-04", View(facts).StatusLine);
    }

    [Fact]
    public void A_seen_version_at_or_below_the_running_one_is_up_to_date()
    {
        Assert.Equal("Up to date. Checked today 14:02", View(Facts() with { HighestSeen = Installed }).StatusLine);
    }

    [Fact]
    public void Higher_seen_is_not_up_to_date()
    {
        var view = View(Facts() with { HighestSeen = Next });

        Assert.Equal("Version 0.2.0 is available. Use Check now.", view.StatusLine);
        Assert.False(view.ShowUpdate);
        Assert.True(view.CheckEnabled);
    }

    [Fact]
    public void Higher_seen_and_skipped()
    {
        Assert.Equal("Version 0.2.0 is skipped.", View(Facts() with { HighestSeen = Next, Skipped = Next }).StatusLine);
    }

    [Theory]
    [InlineData("Auto", "Couldn't check. It will try again tomorrow.")]
    [InlineData("Ask", "Couldn't check. It will try again tomorrow.")]
    [InlineData("Off", "Couldn't check.")]
    public void A_failed_check(string modeName, string line)
    {
        var mode = Enum.Parse<UpdateMode>(modeName);

        Assert.Equal(line, View(Facts(mode) with { Problem = UpdateProblem.CheckFailed }).StatusLine);
    }

    [Fact]
    public void Ready_in_auto()
    {
        var view = View(Facts() with { Manifest = Next, Downloaded = Next, HighestSeen = Next });

        Assert.Equal("Version 0.2.0 is ready. It installs when Hindsound closes.", view.StatusLine);
    }

    [Theory]
    [InlineData("Ask")]
    [InlineData("Off")]
    public void Ready_without_auto(string modeName)
    {
        var mode = Enum.Parse<UpdateMode>(modeName);

        Assert.Equal("Version 0.2.0 is ready.", View(Facts(mode) with { Downloaded = Next, HighestSeen = Next }).StatusLine);
    }

    [Theory]
    [InlineData("Ask", "Version 0.2.0 is available.")]
    [InlineData("Auto", "Version 0.2.0 is available.")]
    [InlineData("Off", "Version 0.2.0 is available. Switch to Ask me to download it.")]
    public void Seen_but_not_downloaded(string modeName, string line)
    {
        var mode = Enum.Parse<UpdateMode>(modeName);

        Assert.Equal(line, View(Facts(mode) with { Manifest = Next, HighestSeen = Next }).StatusLine);
    }

    [Fact]
    public void Skipped()
    {
        var view = View(Facts(UpdateMode.Ask) with { Manifest = Next, HighestSeen = Next, Skipped = Next });

        Assert.Equal("Version 0.2.0 is skipped.", view.StatusLine);
    }

    [Fact]
    public void Paused_by_a_game()
    {
        Assert.Equal("Paused by a game or a clip. It will try again later.", View(Facts() with { Manifest = Next, Problem = UpdateProblem.Paused }).StatusLine);
    }

    [Fact]
    public void Three_attempts_used()
    {
        var facts = Facts() with { Downloaded = Next, HighestSeen = Next, Attempts = new Dictionary<string, int> { ["0.2.0"] = 3 } };

        Assert.Equal("Update to 0.2.0 failed 3 times. Use Update now to try again.", View(facts).StatusLine);
    }

    [Fact]
    public void Two_attempts_used_is_still_ready()
    {
        var facts = Facts() with { Downloaded = Next, HighestSeen = Next, Attempts = new Dictionary<string, int> { ["0.2.0"] = 2 } };

        Assert.Equal("Version 0.2.0 is ready. It installs when Hindsound closes.", View(facts).StatusLine);
    }

    [Fact]
    public void A_failed_download()
    {
        Assert.Equal("Download failed. It will try again tomorrow.", View(Facts() with { HighestSeen = Next, Problem = UpdateProblem.DownloadFailed }).StatusLine);
    }

    [Theory]
    [InlineData("SetupChanged", "Couldn't open the update file. Try again in a moment.")]
    [InlineData("SetupMissing", "The update file is gone. Use Check now.")]
    [InlineData("StateNotSaved", "Couldn't write the update state. Nothing changed.")]
    [InlineData("StartFailed", "Windows refused to start the update.")]
    public void A_setup_that_did_not_start(string statusName, string line)
    {
        var status = Enum.Parse<RunStatus>(statusName);

        var view = View(Facts() with { Downloaded = Next, HighestSeen = Next, Run = status, Card = UpdateCardNote.NotStarted });

        Assert.Equal(line, view.StatusLine);
        Assert.Equal(line, view.CardLine);
    }

    // ---- The buttons

    [Fact]
    public void Nothing_known_shows_check_now_only()
    {
        var view = View(Facts());

        Assert.True(view.Active);
        Assert.True(view.CheckEnabled);
        Assert.False(view.ShowUpdate || view.ShowWhatsNew || view.ShowSkip);
        Assert.Null(view.Offered);
    }

    [Fact]
    public void A_known_version_shows_the_three_buttons()
    {
        var view = View(Facts(UpdateMode.Ask) with { Manifest = Next, HighestSeen = Next });

        Assert.Equal(Next, view.Offered);
        Assert.True(view.ShowUpdate && view.ShowWhatsNew && view.ShowSkip);
        Assert.True(view.UpdateEnabled && view.SkipEnabled && view.CheckEnabled);
    }

    [Fact]
    public void After_a_restart_a_setup_on_disk_keeps_update_now()
    {
        var view = View(Facts(UpdateMode.Ask) with { Downloaded = Next, HighestSeen = Next });

        Assert.Equal(Next, view.Offered);
        Assert.True(view.ShowUpdate && view.UpdateEnabled);
    }

    [Fact]
    public void A_newer_manifest_wins_over_the_setup_on_disk()
    {
        var newer = new UpdateVersion(0, 3, 0);

        var view = View(Facts(UpdateMode.Ask) with { Manifest = newer, Downloaded = Next, HighestSeen = newer });

        Assert.Equal(newer, view.Offered);
        Assert.Equal("Version 0.3.0 is available.", view.StatusLine);
    }

    [Fact]
    public void A_setup_below_the_highest_seen_is_not_offered()
    {
        var view = View(Facts(UpdateMode.Ask) with { Downloaded = Next, HighestSeen = new UpdateVersion(0, 3, 0) });

        Assert.Null(view.Offered);
        Assert.False(view.ShowUpdate);
    }

    [Fact]
    public void A_pass_greys_every_button()
    {
        var view = View(Facts(UpdateMode.Ask) with { Manifest = Next, HighestSeen = Next, Pass = UpdatePassKind.Check });

        Assert.True(view.ShowUpdate);
        Assert.False(view.CheckEnabled || view.UpdateEnabled || view.SkipEnabled);
    }

    [Fact]
    public void A_locked_state_greys_every_button()
    {
        var view = View(Facts(UpdateMode.Ask) with { Manifest = Next, HighestSeen = Next, StateKnown = false });

        Assert.False(view.CheckEnabled || view.UpdateEnabled || view.SkipEnabled);
    }

    [Fact]
    public void A_skipped_version_greys_skip_only()
    {
        var view = View(Facts(UpdateMode.Ask) with { Manifest = Next, HighestSeen = Next, Skipped = Next });

        Assert.True(view.ShowSkip);
        Assert.False(view.SkipEnabled);
        Assert.True(view.UpdateEnabled);
    }

    [Fact]
    public void Off_cannot_download_on_a_click()
    {
        Assert.False(View(Facts(UpdateMode.Off) with { Manifest = Next, HighestSeen = Next }).UpdateEnabled);
        Assert.True(View(Facts(UpdateMode.Off) with { Downloaded = Next, HighestSeen = Next }).UpdateEnabled);
    }

    // ---- The card

    [Fact]
    public void A_standing_click_downloads_with_cancel_only()
    {
        var view = View(Facts(UpdateMode.Ask) with { Manifest = Next, Pass = UpdatePassKind.Download, PassVersion = Next, ClickStands = true });

        Assert.Equal("Downloading 0.2.0...", view.CardLine);
        Assert.True(view.CardBusy);
    }

    [Theory]
    [InlineData("NotNow", "Not now: a game or a clip is running.")]
    [InlineData("DownloadFailed", "Download failed. Nothing changed.")]
    [InlineData("None", null)]
    public void A_dropped_click(string noteName, string? line)
    {
        var note = Enum.Parse<UpdateCardNote>(noteName);

        var view = View(Facts(UpdateMode.Ask) with { Card = note });

        Assert.Equal(line, view.CardLine);
        Assert.False(view.CardBusy);
    }

    // ---- The menu

    [Fact]
    public void Menu_in_off_offers_nothing()
    {
        Assert.Null(UpdatePanel.MenuVersion(UpdateMode.Off, Installed, Next, null, Next, Next));
    }

    [Fact]
    public void Menu_in_auto_offers_the_downloaded_version_only()
    {
        Assert.Equal(Next, UpdatePanel.MenuVersion(UpdateMode.Auto, Installed, Next, null, Next, Next));
        Assert.Null(UpdatePanel.MenuVersion(UpdateMode.Auto, Installed, Next, null, Next, null));
    }

    [Fact]
    public void Menu_in_ask_offers_the_downloaded_version_then_the_manifest()
    {
        Assert.Equal(Next, UpdatePanel.MenuVersion(UpdateMode.Ask, Installed, Next, null, null, Next));
        Assert.Equal(Next, UpdatePanel.MenuVersion(UpdateMode.Ask, Installed, Next, null, Next, null));
        Assert.Null(UpdatePanel.MenuVersion(UpdateMode.Ask, Installed, null, null, null, null));
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("Ask")]
    public void Menu_with_a_skipped_version_offers_nothing(string modeName)
    {
        var mode = Enum.Parse<UpdateMode>(modeName);

        Assert.Null(UpdatePanel.MenuVersion(mode, Installed, Next, Next, Next, Next));
    }

    // ---- What's new

    [Fact]
    public void Whats_new_opens_the_release_page_of_the_version()
    {
        Assert.Equal("https://github.com/quelquun667/hindsound/releases/tag/v0.2.0", UpdateUrlPolicy.ReleasePage(Next));
    }
}
