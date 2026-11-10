using Hindsound.App;

namespace Hindsound.Updates;

/// <summary>Install automatically (checks, downloads, installs on leaving), Ask me (checks and announces), Off (no network).</summary>
internal enum UpdateMode
{
    Auto,
    Ask,
    Off,
}

/// <summary>What the app is asking about.</summary>
public enum UpdateEvent
{
    /// <summary>The one-shot timer fired, or the previous step of a pass finished well.</summary>
    Tick,
    CheckNowClicked,
    UpdateClicked,

    /// <summary>The user chose Quit. Not the end of the Windows session, which is <see cref="UpdateSituation.SessionEnding"/>.</summary>
    Quitting,

    /// <summary>At launch, after the Mutex and before the capture.</summary>
    Launching,
}

internal enum UpdateAction
{
    None,
    Check,
    Download,
    Announce,
    WaitThirtyMinutes,
    Apply,
}

/// <summary>What the installed version found about the attempt recorded before its last Setup.</summary>
internal enum LaunchOutcome
{
    NoUpdateExpected,
    Updated,
    Failed,
}

/// <summary>All the decider looks at. Built by the app from the settings, <see cref="UpdateStateData"/> and the system state.</summary>
internal sealed record UpdateSituation
{
    public required UpdateEvent Event { get; init; }

    public required UpdateMode Mode { get; init; }

    public required DateTimeOffset Now { get; init; }

    public required DateTimeOffset AppStartedAt { get; init; }

    public DateTimeOffset? LastCheck { get; init; }

    public bool FullScreen { get; init; }

    public bool Presentation { get; init; }

    public bool ClipInProgress { get; init; }

    public bool SessionEnding { get; init; }

    public required UpdateVersion Installed { get; init; }

    /// <summary>The version named by the last valid manifest of this run, if any.</summary>
    public UpdateVersion? Manifest { get; init; }

    public UpdateVersion? HighestSeen { get; init; }

    public UpdateVersion? Skipped { get; init; }

    public UpdateVersion? Announced { get; init; }

    /// <summary>The version Setup was last started for. Read at launch, before the app clears it.</summary>
    public UpdateVersion? Expected { get; init; }

    /// <summary>The version of the Setup already on disk, if any.</summary>
    public UpdateVersion? Downloaded { get; init; }

    public IReadOnlyDictionary<string, int>? AttemptsByVersion { get; init; }

    /// <summary>Install, or only tell. A platform that only tells never downloads nor installs, whatever the mode.</summary>
    public UpdateDelivery Delivery { get; init; }
}

/// <summary>
/// The rules of the updater, with no clock, file, network or window: everything arrives in the situation. After a failed
/// download the app does not ask again before the next due check, so a broken release cannot make it loop on the network.
/// </summary>
internal static class UpdateDecider
{
    public const int MaxAttemptsPerVersion = 3;

    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan DelayAfterLaunch = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan BusyRetry = TimeSpan.FromMinutes(30);

    public static UpdateAction Decide(UpdateSituation situation) => situation.Delivery == UpdateDelivery.AnnounceOnly
        ? DecideAnnounceOnly(situation)
        : DecideSetup(situation);

    /// <summary>
    /// The mode as a delivery can honour it. Install automatically cannot install where the app does not replace itself: it
    /// tells, as Ask me does.
    /// </summary>
    public static UpdateMode EffectiveMode(UpdateDelivery delivery, UpdateMode mode) =>
        delivery == UpdateDelivery.AnnounceOnly && mode == UpdateMode.Auto ? UpdateMode.Ask : mode;

    // The check when due, then one announcement per version. No click downloads, and nothing waits for the app to leave.
    private static UpdateAction DecideAnnounceOnly(UpdateSituation situation) => situation.Event switch
    {
        UpdateEvent.Tick => OnTick(situation with { Mode = EffectiveMode(UpdateDelivery.AnnounceOnly, situation.Mode) }),
        UpdateEvent.CheckNowClicked => IsBusy(situation) ? UpdateAction.None : UpdateAction.Check,
        _ => UpdateAction.None,
    };

    private static UpdateAction DecideSetup(UpdateSituation situation) => situation.Event switch
    {
        UpdateEvent.Tick => OnTick(situation),
        UpdateEvent.CheckNowClicked => IsBusy(situation) ? UpdateAction.None : UpdateAction.Check,
        UpdateEvent.UpdateClicked => OnUpdateClicked(situation),
        _ => OnLeaving(situation),
    };

    /// <summary>When the one-shot timer should fire. Null in Off: no timer at all.</summary>
    public static DateTimeOffset? NextCheckAt(UpdateMode mode, DateTimeOffset appStartedAt, DateTimeOffset? lastCheck, DateTimeOffset now)
    {
        if (mode == UpdateMode.Off)
        {
            return null;
        }

        var earliest = appStartedAt + DelayAfterLaunch;

        // A last check in the future means the clock moved: waiting for that date could stop the updates for years.
        if (lastCheck is not { } last || last > now)
        {
            return earliest;
        }

        var due = last + CheckInterval;
        return due > earliest ? due : earliest;
    }

    /// <summary>The version check every path goes through: strictly above the installed one, and not below the highest seen.</summary>
    public static bool IsAcceptable(UpdateVersion version, UpdateVersion installed, UpdateVersion? highestSeen) =>
        version > installed && (highestSeen is not { } highest || version >= highest);

    /// <summary>Does the app bring this version up by itself? Check now and the Update button still work on a skipped one.</summary>
    public static bool IsOffered(UpdateVersion version, UpdateVersion installed, UpdateVersion? highestSeen, UpdateVersion? skipped) =>
        IsAcceptable(version, installed, highestSeen) && (skipped is not { } skip || version > skip);

    /// <summary>What remains of the skipped version once a manifest was read: a higher version clears it.</summary>
    public static UpdateVersion? SkippedAfter(UpdateVersion? skipped, UpdateVersion manifest) =>
        skipped is { } skip && manifest > skip ? null : skipped;

    /// <summary>Restart after Setup? Quit means the user is done, so it installs without coming back.</summary>
    public static bool RestartAfterApply(UpdateEvent trigger) => trigger != UpdateEvent.Quitting;

    public static LaunchOutcome OutcomeAtLaunch(UpdateVersion installed, UpdateVersion? expected)
    {
        if (expected is not { } version)
        {
            return LaunchOutcome.NoUpdateExpected;
        }

        return installed >= version ? LaunchOutcome.Updated : LaunchOutcome.Failed;
    }

    /// <summary>
    /// Reads SHQueryUserNotificationState for the updater: an exclusive or borderless game, a window over the whole screen,
    /// nobody at the screen, or a failed call. When in doubt, nothing happens for 30 minutes.
    /// </summary>
    public static bool IsFullScreen(UserNotificationState state, bool foregroundCoversScreen) =>
        foregroundCoversScreen || state is UserNotificationState.Busy
            or UserNotificationState.RunningD3dFullScreen
            or UserNotificationState.NotPresent
            or UserNotificationState.Unknown;

    public static bool IsPresentation(UserNotificationState state) => state == UserNotificationState.PresentationMode;

    private static UpdateAction OnTick(UpdateSituation s)
    {
        if (s.Mode == UpdateMode.Off)
        {
            return UpdateAction.None;
        }

        var step = NextAutomaticStep(s);
        return step != UpdateAction.None && IsBusy(s) ? UpdateAction.WaitThirtyMinutes : step;
    }

    private static UpdateAction NextAutomaticStep(UpdateSituation s)
    {
        if (NextCheckAt(s.Mode, s.AppStartedAt, s.LastCheck, s.Now) <= s.Now)
        {
            return UpdateAction.Check;
        }

        if (s.Manifest is not { } version || !IsOffered(version, s.Installed, s.HighestSeen, s.Skipped) || AttemptsUsedUp(s, version))
        {
            return UpdateAction.None;
        }

        if (s.Mode == UpdateMode.Auto && s.Downloaded != version)
        {
            return UpdateAction.Download;
        }

        return s.Announced == version ? UpdateAction.None : UpdateAction.Announce;
    }

    // A click is the one thing that may go past the attempts and a skipped version. It still never restarts over a game or
    // a clip, and it is dropped rather than kept for later: coming back 30 minutes on would be a restart nobody asked for.
    private static UpdateAction OnUpdateClicked(UpdateSituation s)
    {
        if (IsBusy(s))
        {
            return UpdateAction.None;
        }

        var manifest = s.Manifest is { } m && IsAcceptable(m, s.Installed, s.HighestSeen) ? m : (UpdateVersion?)null;
        var downloaded = s.Downloaded is { } d && IsAcceptable(d, s.Installed, s.HighestSeen) ? d : (UpdateVersion?)null;

        if (downloaded is { } ready && (manifest is not { } newer || newer <= ready))
        {
            return UpdateAction.Apply;
        }

        return manifest is not null && s.Mode != UpdateMode.Off ? UpdateAction.Download : UpdateAction.None;
    }

    private static UpdateAction OnLeaving(UpdateSituation s)
    {
        if (s.Mode != UpdateMode.Auto || IsBusy(s) || s.Downloaded is not { } version)
        {
            return UpdateAction.None;
        }

        // Setup failed and restarted this version. Trying again right away would turn the 3 attempts into one long stall.
        if (s.Event == UpdateEvent.Launching && OutcomeAtLaunch(s.Installed, s.Expected) == LaunchOutcome.Failed)
        {
            return UpdateAction.None;
        }

        return IsOffered(version, s.Installed, s.HighestSeen, s.Skipped) && !AttemptsUsedUp(s, version)
            ? UpdateAction.Apply
            : UpdateAction.None;
    }

    private static bool IsBusy(UpdateSituation s) => s.FullScreen || s.Presentation || s.ClipInProgress || s.SessionEnding;

    private static bool AttemptsUsedUp(UpdateSituation s, UpdateVersion version) =>
        s.AttemptsByVersion is not null
        && s.AttemptsByVersion.TryGetValue(version.ToString(), out var attempts)
        && attempts >= MaxAttemptsPerVersion;
}
