using Hindsound.App;

namespace Hindsound.Tests.App;

/// <summary>
/// Only a run started as a user starts it may update: no argument at all, or only what Setup passes. A test run, a typing
/// error or an option of tomorrow never reaches the network nor a Setup.
/// </summary>
public class UpdateOptionsTests
{
    [Fact]
    public void Update_failed_is_read()
    {
        Assert.True(AppOptions.Parse(["--update-failed"]).UpdateFailed);
        Assert.False(AppOptions.Parse([]).UpdateFailed);
    }

    [Fact]
    public void A_run_without_options_may_update()
    {
        Assert.True(AppOptions.UpdaterMayRun([]));
    }

    [Fact]
    public void A_run_started_again_by_setup_may_update()
    {
        Assert.True(AppOptions.UpdaterMayRun(["--update-failed"]));
    }

    [Theory]
    [InlineData("--settings", "s.json")]
    [InlineData("--exit-after", "5")]
    [InlineData("--selftest-save")]
    [InlineData("--offscreen")]
    [InlineData("--dump-notifications", "out")]
    [InlineData("--dump-settings", "out")]
    [InlineData("--settings-probe")]
    [InlineData("--first-run")]
    [InlineData("--scale", "1.5")]
    [InlineData("--selftest-delay", "10")]
    [InlineData("--selftest-fail", "card")]
    [InlineData("--selftest-game", "15")]
    [InlineData("--selftest-clip-gone")]
    [InlineData("--selftest-slow-save", "10")]
    [InlineData("--no-mic")]
    [InlineData("--no-clipboard")]
    [InlineData("--no-hotkeys")]
    [InlineData("--prove-mp3", "out")]
    [InlineData("--selftest-update", "folder")]
    [InlineData("--selftest-update-check")]
    public void Each_option_is_a_test_run(params string[] args)
    {
        Assert.False(AppOptions.UpdaterMayRun(args));
        Assert.False(AppOptions.UpdaterMayRun([.. args, "--update-failed"]));
        Assert.False(AppOptions.UpdaterMayRun(["--update-failed", .. args]));
    }

    [Theory]
    [InlineData("--nonsense")]
    [InlineData("--setting", "s.json")]
    [InlineData("--settings")]
    [InlineData("--exit-after")]
    [InlineData("--scale", "1")]
    [InlineData("--selftest-delay", "3")]
    [InlineData("--update-failed", "--update-faild")]
    [InlineData("")]
    [InlineData("--UPDATE-FAILED")]
    public void Unknown_option_is_a_test_run(params string[] args)
    {
        // The parsed options cannot tell these from a real run: each gives the defaults, as an unknown option is ignored.
        Assert.Equal(new AppOptions(), AppOptions.Parse(args) with { UpdateFailed = false });
        Assert.False(AppOptions.UpdaterMayRun(args));
    }

    [Fact]
    public void A_run_started_again_by_setup_does_not_wake_the_running_one()
    {
        Assert.False(Program.WakesTheFirstInstance(AppOptions.Parse(["--update-failed"])));
        Assert.True(Program.WakesTheFirstInstance(AppOptions.Parse([])));
    }

    [Fact]
    public void The_update_self_tests_do_nothing_without_a_settings_file()
    {
        var alone = AppOptions.Parse(["--selftest-update", "folder", "--selftest-update-check"]);

        Assert.Null(alone.SelfTestUpdateFolder);
        Assert.False(alone.SelfTestUpdateCheck);
        Assert.Equal(new AppOptions(), alone);
    }

    [Fact]
    public void The_update_self_tests_are_read_with_a_settings_file_whatever_the_order()
    {
        var before = AppOptions.Parse(["--settings", "s.json", "--selftest-update", "folder", "--selftest-update-check"]);
        var after = AppOptions.Parse(["--selftest-update", "folder", "--selftest-update-check", "--settings", "s.json"]);

        Assert.Equal("folder", before.SelfTestUpdateFolder);
        Assert.True(before.SelfTestUpdateCheck);
        Assert.Equal(before, after);
    }

    [Fact]
    public void A_forgotten_self_test_folder_is_not_taken_from_the_next_option()
    {
        var options = AppOptions.Parse(["--selftest-update", "--settings", "s.json"]);

        Assert.Null(options.SelfTestUpdateFolder);
        Assert.Equal("s.json", options.SettingsPath);
    }
}
