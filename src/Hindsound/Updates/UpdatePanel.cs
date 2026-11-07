using System.Globalization;

namespace Hindsound.Updates;

/// <summary>What the Updates section of General and the confirmation card show. Built anew at each refresh.</summary>
/// <param name="VersionLine">The running version, shown even when the section has nothing else.</param>
/// <param name="Active">False for a copy that was not installed, or a test run: the rest of the section goes.</param>
/// <param name="Offered">The version the buttons act on: from this run's manifest, or the Setup already on disk.</param>
internal sealed record UpdatePanelView(string VersionLine, string StatusLine, bool Active, bool CheckEnabled,
    UpdateVersion? Offered, bool UpdateEnabled, bool ShowWhatsNew, bool ShowSkip, bool ShowUpdate)
{
    /// <summary>Greyed during a pass, and when the version is skipped already.</summary>
    public bool SkipEnabled { get; init; }

    /// <summary>What the card says about the click, or null when it has nothing to say.</summary>
    public string? CardLine { get; init; }

    /// <summary>A click stands while its version downloads: only Cancel stays active on the card.</summary>
    public bool CardBusy { get; init; }
}

/// <summary>The window's side of the updater. Kept out of ISettingsBackend, which test doubles implement.</summary>
internal interface IUpdatesPanel
{
    UpdatePanelView View { get; }

    bool ClipBusy { get; }

    int BufferSeconds { get; }

    void CheckNow();

    void ConfirmUpdate(bool saveFirst);

    void CancelUpdate();

    void WhatsNew();

    void SkipThisVersion();
}

internal enum UpdatePassKind
{
    None,
    Check,
    Download,
}

/// <summary>How the last pass ended, when that is worth a line. Cleared when the next one starts.</summary>
internal enum UpdateProblem
{
    None,

    /// <summary>Stopped by a game, a presentation, a clip or the end of the session.</summary>
    Paused,
    CheckFailed,
    DownloadFailed,
}

/// <summary>Why a click on Update did not restart the app.</summary>
internal enum UpdateCardNote
{
    None,
    NotNow,
    DownloadFailed,

    /// <summary>Setup did not start: the line comes from the <see cref="RunStatus"/>.</summary>
    NotStarted,
}

/// <summary>All the view is computed from. The flow fills it; the tests write it by hand.</summary>
internal sealed record UpdatePanelFacts
{
    public required UpdateVersion Installed { get; init; }

    public required UpdateMode Mode { get; init; }

    public required DateTimeOffset Now { get; init; }

    /// <summary>False while state.json stays locked: nothing may be written, so nothing may be clicked.</summary>
    public bool StateKnown { get; init; } = true;

    public DateTimeOffset? LastCheck { get; init; }

    public UpdateVersion? HighestSeen { get; init; }

    public UpdateVersion? Manifest { get; init; }

    public UpdateVersion? Downloaded { get; init; }

    public UpdateVersion? Skipped { get; init; }

    public IReadOnlyDictionary<string, int>? Attempts { get; init; }

    public UpdatePassKind Pass { get; init; }

    public UpdateVersion? PassVersion { get; init; }

    public UpdateProblem Problem { get; init; }

    /// <summary>The last Setup that did not start, until the next pass.</summary>
    public RunStatus? Run { get; init; }

    public bool ClickStands { get; init; }

    public UpdateCardNote Card { get; init; }
}

/// <summary>The Updates section as text and switches, with no window: every line of it is tested from fixed facts.</summary>
internal static class UpdatePanel
{
    public const string NotInstalled = "This copy was not installed, so it does not update itself.";

    public static UpdatePanelView Inactive(UpdateVersion installed) =>
        new(VersionLine(installed), NotInstalled, Active: false, CheckEnabled: false, Offered: null,
            UpdateEnabled: false, ShowWhatsNew: false, ShowSkip: false, ShowUpdate: false);

    public static UpdatePanelView Compute(UpdatePanelFacts facts)
    {
        var offered = Offered(facts.Installed, facts.HighestSeen, facts.Manifest, facts.Downloaded);
        var idle = facts.StateKnown && facts.Pass == UpdatePassKind.None;
        var known = offered is not null;
        var ready = known && offered == facts.Downloaded;

        return new UpdatePanelView(
            VersionLine(facts.Installed),
            StatusLine(facts, offered),
            Active: true,
            CheckEnabled: idle,
            offered,

            // In Off the decider downloads nothing, even on a click: only a Setup already on disk can be installed.
            UpdateEnabled: known && idle && (ready || facts.Mode != UpdateMode.Off),
            ShowWhatsNew: known,
            ShowSkip: known,
            ShowUpdate: known)
        {
            SkipEnabled = known && idle && !IsSkipped(offered!.Value, facts.Skipped),
            CardLine = CardLine(facts),
            CardBusy = facts.ClickStands && facts.Pass == UpdatePassKind.Download,
        };
    }

    /// <summary>The version the section offers: the higher acceptable one of the manifest and the Setup on disk.</summary>
    public static UpdateVersion? Offered(UpdateVersion installed, UpdateVersion? highestSeen, UpdateVersion? manifest, UpdateVersion? downloaded)
    {
        UpdateVersion? fromManifest = manifest is { } m && UpdateDecider.IsAcceptable(m, installed, highestSeen) ? m : null;
        UpdateVersion? onDisk = downloaded is { } d && UpdateDecider.IsAcceptable(d, installed, highestSeen) ? d : null;
        return fromManifest is { } newer && (onDisk is not { } ready || newer > ready) ? newer : onDisk;
    }

    /// <summary>
    /// The version the tray menu offers, or null. Off has no reminder, and a skipped version none either. After 3 failed
    /// attempts the entry stays: only a click can still install it.
    /// </summary>
    public static UpdateVersion? MenuVersion(UpdateMode mode, UpdateVersion installed, UpdateVersion? highestSeen, UpdateVersion? skipped,
        UpdateVersion? manifest, UpdateVersion? downloaded)
    {
        if (mode == UpdateMode.Off)
        {
            return null;
        }

        if (downloaded is { } ready && UpdateDecider.IsOffered(ready, installed, highestSeen, skipped))
        {
            return ready;
        }

        // Auto downloads before it offers anything: a version only named by the manifest is on its way.
        return mode == UpdateMode.Ask && manifest is { } seen && UpdateDecider.IsOffered(seen, installed, highestSeen, skipped) ? seen : null;
    }

    /// <summary>SetupChanged also means locked (UpdateRunner): the line never says the file was changed.</summary>
    public static string RunMessage(RunStatus status) => status switch
    {
        RunStatus.SetupChanged => "Couldn't open the update file. Try again in a moment.",
        RunStatus.SetupMissing => "The update file is gone. Use Check now.",
        RunStatus.StateNotSaved => "Couldn't write the update state. Nothing changed.",
        RunStatus.StartFailed => "Windows refused to start the update.",
        _ => "Starting the update...",
    };

    private static string VersionLine(UpdateVersion installed) => $"Version {installed}";

    private static string StatusLine(UpdatePanelFacts f, UpdateVersion? offered)
    {
        switch (f.Pass)
        {
            case UpdatePassKind.Check:
                return "Checking...";
            case UpdatePassKind.Download:
                return $"Downloading {f.PassVersion}...";
        }

        if (f.Run is { } run && run != RunStatus.Started)
        {
            return RunMessage(run);
        }

        if (offered is { } ready && ready == f.Downloaded)
        {
            return OfferedLine(f, ready, onDisk: true);
        }

        switch (f.Problem)
        {
            case UpdateProblem.Paused:
                return "Paused by a game or a clip. It will try again later.";
            case UpdateProblem.DownloadFailed:
                return "Download failed. It will try again tomorrow.";
            case UpdateProblem.CheckFailed:
                // Off has no next check: only the button tries again.
                return f.Mode == UpdateMode.Off ? "Couldn't check." : "Couldn't check. It will try again tomorrow.";
        }

        if (offered is { } available)
        {
            return OfferedLine(f, available, onDisk: false);
        }

        if (f.LastCheck is not { } last)
        {
            return f.Mode == UpdateMode.Off ? "Updates are off." : "Not checked yet.";
        }

        // The manifest does not outlive a restart, the floor does: "Up to date" would be wrong for up to 24 hours.
        if (f.HighestSeen is { } seen && seen > f.Installed)
        {
            return IsSkipped(seen, f.Skipped) ? $"Version {seen} is skipped." : $"Version {seen} is available. Use Check now.";
        }

        return "Up to date. " + Checked(last, f.Now);
    }

    private static string OfferedLine(UpdatePanelFacts f, UpdateVersion version, bool onDisk)
    {
        if (IsSkipped(version, f.Skipped))
        {
            return $"Version {version} is skipped.";
        }

        if (f.Attempts is not null && f.Attempts.TryGetValue(version.ToString(), out var attempts) && attempts >= UpdateDecider.MaxAttemptsPerVersion)
        {
            return $"Update to {version} failed {UpdateDecider.MaxAttemptsPerVersion} times. Use Update now to try again.";
        }

        if (onDisk)
        {
            return f.Mode == UpdateMode.Auto ? $"Version {version} is ready. It installs when Hindsound closes." : $"Version {version} is ready.";
        }

        // The decider downloads nothing in Off.
        return f.Mode == UpdateMode.Off ? $"Version {version} is available. Switch to Ask me to download it." : $"Version {version} is available.";
    }

    // InvariantGlobalization is on in the published exe: digits only, no month names.
    private static string Checked(DateTimeOffset last, DateTimeOffset now)
    {
        var local = last.ToOffset(now.Offset);
        if (local.Date == now.Date)
        {
            return "Checked today " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return local.Date == now.Date.AddDays(-1)
            ? "Checked yesterday " + local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : "Checked " + local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string? CardLine(UpdatePanelFacts f)
    {
        if (f.ClickStands && f.Pass == UpdatePassKind.Download)
        {
            return $"Downloading {f.PassVersion}...";
        }

        return f.Card switch
        {
            UpdateCardNote.NotNow => "Not now: a game or a clip is running.",
            UpdateCardNote.DownloadFailed => "Download failed. Nothing changed.",
            UpdateCardNote.NotStarted when f.Run is { } run => RunMessage(run),
            _ => null,
        };
    }

    private static bool IsSkipped(UpdateVersion version, UpdateVersion? skipped) => skipped is { } skip && version <= skip;
}
