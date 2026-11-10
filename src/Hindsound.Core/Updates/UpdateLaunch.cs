namespace Hindsound.Updates;

/// <param name="Outcome">What became of the last Setup.</param>
/// <param name="Next">The state to keep. Written only when it differs from the one read: no write at every launch.</param>
/// <param name="Keep">The one Setup <see cref="UpdateFiles.Clean"/> leaves on disk, if any.</param>
/// <param name="MayApply">False after a failed Setup: starting it again at once would chain the 3 attempts in one stall.</param>
internal readonly record struct LaunchPlan(LaunchOutcome Outcome, UpdateStateData Next, UpdateVersion? Keep, bool MayApply);

/// <param name="SetupStarted">Setup runs: the app leaves before its icon, its hotkeys or its capture.</param>
/// <param name="UpdatedTo">The running version, when it is the one the last Setup was started for.</param>
/// <param name="WillRetry">Whether the failure notification may promise another attempt.</param>
internal readonly record struct LaunchReport(bool SetupStarted, LaunchOutcome Outcome, UpdateVersion? UpdatedTo, bool WillRetry);

/// <summary>What the state read at launch says about the last Setup, with no file and no clock.</summary>
internal static class UpdateLaunch
{
    public static LaunchPlan Plan(UpdateVersion installed, UpdateStateData state, bool updateFailedFlag)
    {
        UpdateVersion? pending = state.Pending is { } setup && UpdateVersion.TryParse(setup.Version, out var waiting) ? waiting : null;
        UpdateVersion? expected = UpdateVersion.TryParse(state.ExpectedVersion, out var version) ? version : null;

        switch (UpdateDecider.OutcomeAtLaunch(installed, expected))
        {
            case LaunchOutcome.Updated:
                // The running version is a fact: a flag passed by a Setup that failed after writing the files changes nothing.
                return new LaunchPlan(LaunchOutcome.Updated, state with { ExpectedVersion = null, Pending = null }, null, MayApply: true);

            case LaunchOutcome.Failed:
                // The Setup stays: the next attempt, or the button, uses it without a new download.
                return new LaunchPlan(LaunchOutcome.Failed, state with { ExpectedVersion = null }, pending, MayApply: false);
        }

        // Setup says it failed, and the state does not know: lost, or locked when Setup started. Saying it touches no file.
        if (updateFailedFlag)
        {
            return new LaunchPlan(LaunchOutcome.Failed, state, pending, MayApply: false);
        }

        if (pending is { } newer && newer > installed)
        {
            return new LaunchPlan(LaunchOutcome.NoUpdateExpected, state, newer, MayApply: true);
        }

        // A Setup at or below the running version was run by hand, or never was a version: nothing left to install.
        return new LaunchPlan(LaunchOutcome.NoUpdateExpected, state.Pending is null ? state : state with { Pending = null }, null, MayApply: true);
    }
}
