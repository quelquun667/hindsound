using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class UpdateLaunchTests
{
    private static readonly UpdateVersion Installed = new(0, 2, 0);
    private static readonly PendingSetup Newer = new("0.3.0", new string('a', 64), 2_000_000);

    private static UpdateStateData Seen() => new() { HighestSeen = "0.3.0", LastCheck = new DateTimeOffset(2026, 11, 6, 14, 2, 0, TimeSpan.FromHours(1)) };

    [Fact]
    public void Nothing_expected_and_nothing_waiting_changes_nothing()
    {
        var state = Seen();

        var plan = UpdateLaunch.Plan(Installed, state, updateFailedFlag: false);

        Assert.Equal(LaunchOutcome.NoUpdateExpected, plan.Outcome);
        Assert.Same(state, plan.Next);
        Assert.Null(plan.Keep);
        Assert.True(plan.MayApply);
    }

    [Fact]
    public void A_newer_waiting_setup_is_kept()
    {
        var state = Seen() with { Pending = Newer };

        var plan = UpdateLaunch.Plan(Installed, state, updateFailedFlag: false);

        Assert.Equal(LaunchOutcome.NoUpdateExpected, plan.Outcome);
        Assert.Same(state, plan.Next);
        Assert.Equal(new UpdateVersion(0, 3, 0), plan.Keep);
        Assert.True(plan.MayApply);
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.1.0")]
    [InlineData("not a version")]
    public void A_setup_at_or_below_the_running_version_is_forgotten(string version)
    {
        var state = Seen() with { Pending = Newer with { Version = version } };

        var plan = UpdateLaunch.Plan(Installed, state, updateFailedFlag: false);

        Assert.Equal(LaunchOutcome.NoUpdateExpected, plan.Outcome);
        Assert.Equal(Seen(), plan.Next);
        Assert.Null(plan.Keep);
        Assert.True(plan.MayApply);
    }

    [Fact]
    public void Reached_version_is_updated_and_clears_the_attempt()
    {
        var state = Seen() with { ExpectedVersion = "0.2.0", Pending = Newer with { Version = "0.2.0" } };

        var plan = UpdateLaunch.Plan(Installed, state, updateFailedFlag: false);

        Assert.Equal(LaunchOutcome.Updated, plan.Outcome);
        Assert.Null(plan.Next.ExpectedVersion);
        Assert.Null(plan.Next.Pending);
        Assert.Equal("0.3.0", plan.Next.HighestSeen);
        Assert.Null(plan.Keep);
        Assert.True(plan.MayApply);
    }

    [Fact]
    public void Missed_version_failed_and_keeps_its_setup()
    {
        var attempts = new Dictionary<string, int> { ["0.3.0"] = 1 };
        var state = Seen() with { ExpectedVersion = "0.3.0", Pending = Newer, AttemptsByVersion = attempts };

        var plan = UpdateLaunch.Plan(Installed, state, updateFailedFlag: false);

        Assert.Equal(LaunchOutcome.Failed, plan.Outcome);
        Assert.Null(plan.Next.ExpectedVersion);
        Assert.Equal(Newer, plan.Next.Pending);
        Assert.Same(attempts, plan.Next.AttemptsByVersion);
        Assert.Equal(new UpdateVersion(0, 3, 0), plan.Keep);
        Assert.False(plan.MayApply);
    }

    [Fact]
    public void Flag_without_state_says_failed_and_keeps_setup()
    {
        var state = Seen() with { Pending = Newer };

        var plan = UpdateLaunch.Plan(Installed, state, updateFailedFlag: true);

        Assert.Equal(LaunchOutcome.Failed, plan.Outcome);
        Assert.Same(state, plan.Next);
        Assert.Equal(new UpdateVersion(0, 3, 0), plan.Keep);
        Assert.False(plan.MayApply);
    }

    [Fact]
    public void Flag_without_any_state_still_says_failed()
    {
        var plan = UpdateLaunch.Plan(Installed, new UpdateStateData(), updateFailedFlag: true);

        Assert.Equal(LaunchOutcome.Failed, plan.Outcome);
        Assert.Equal(new UpdateStateData(), plan.Next);
        Assert.Null(plan.Keep);
        Assert.False(plan.MayApply);
    }

    [Fact]
    public void Reached_version_with_the_flag_is_still_updated()
    {
        var state = Seen() with { ExpectedVersion = "0.2.0", Pending = Newer with { Version = "0.2.0" } };

        var plan = UpdateLaunch.Plan(Installed, state, updateFailedFlag: true);

        Assert.Equal(LaunchOutcome.Updated, plan.Outcome);
        Assert.Null(plan.Next.ExpectedVersion);
        Assert.Null(plan.Next.Pending);
        Assert.Null(plan.Keep);
        Assert.True(plan.MayApply);
    }

    [Fact]
    public void Clean_keeps_the_setup_of_the_plan_and_nothing_else()
    {
        using var folder = new TempFolder();
        File.WriteAllBytes(folder.File("setup-0.3.0.exe"), [1, 2, 3]);
        File.WriteAllBytes(folder.File("setup-0.2.5.exe"), [1]);
        File.WriteAllBytes(folder.File("setup-0.3.0.exe.part"), [1]);
        File.WriteAllBytes(folder.File("state.json"), [1]);
        var plan = UpdateLaunch.Plan(Installed, Seen() with { ExpectedVersion = "0.3.0", Pending = Newer }, updateFailedFlag: false);

        UpdateFiles.Clean(folder.Path, plan.Keep);

        Assert.Equal(["setup-0.3.0.exe", "state.json"], Directory.GetFiles(folder.Path).Select(f => Path.GetFileName(f)!).Order().ToArray());
    }

    [Fact]
    public void Clean_after_an_update_removes_every_setup()
    {
        using var folder = new TempFolder();
        File.WriteAllBytes(folder.File("setup-0.2.0.exe"), [1]);
        var plan = UpdateLaunch.Plan(Installed, Seen() with { ExpectedVersion = "0.2.0", Pending = Newer with { Version = "0.2.0" } }, updateFailedFlag: false);

        UpdateFiles.Clean(folder.Path, plan.Keep);

        Assert.Empty(Directory.GetFiles(folder.Path));
    }
}
