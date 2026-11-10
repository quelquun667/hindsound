using System.Diagnostics;
using Hindsound.Updates;

namespace Hindsound.App;

/// <summary>
/// Which updater this run gets, if any. The real one only for a run started as a user starts it, from an installed copy:
/// a test run, whatever its options, never reaches the network nor starts a Setup.
/// </summary>
internal static class UpdateWiring
{
    public const string TestRun = "test run";
    public const string TestFolder = "test folder";
    public const string NotInstalled = "not installed";
    public const string Installed = "installed";

    /// <summary>A platform that only tells about a new version: any copy may, since none replaces itself.</summary>
    public const string AnnouncesOnly = "announces only";

    /// <summary>The floor of the real timer: even a clock gone wild cannot make it loop on the network.</summary>
    public static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(60);

    /// <summary>The test flow's first step comes 3 s after the launch, instead of 2 min: the proofs should not wait.</summary>
    public static readonly TimeSpan TestFirstStep = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan TestMinimumDelay = TimeSpan.FromSeconds(1);

    private const string StateFile = "state.json";
    private const string TestStateFolder = "updates";

    /// <summary>
    /// Null: this run never reaches the network nor a Setup. The only place that makes the network transport or a real start.
    /// Touches neither the disk nor the network: everything begins at <see cref="UpdateFlow.AtLaunch"/>.
    /// </summary>
    public static UpdateFlow? Create(bool updaterMayRun, AppOptions options, string? exeFolder, IUpdateHost host, DateTimeOffset now)
    {
        var channel = Channel;
        switch (Why(updaterMayRun, options, exeFolder, channel))
        {
            case Installed or AnnouncesOnly:
                var folder = UpdateStateStore.DefaultFolder;
                return new UpdateFlow(new UpdateStateStore(Path.Combine(folder, StateFile)), folder, InstalledVersion(), PlatformDefaults.Make(PlatformDefaults.UpdateNetwork, "an update network"),
                    UpdateRunner.StartProcess, host, now, MinimumDelay)
                {
                    Channel = channel,
                };

            case TestFolder:
                // Its state lives next to its files: the user's own state is never read nor written.
                var source = options.SelfTestUpdateFolder!;
                var state = Path.Combine(source, TestStateFolder);
                return new UpdateFlow(new UpdateStateStore(Path.Combine(state, StateFile)), state, InstalledVersion(), new FolderTransport(source),
                    info => WouldStart(info, host), host, now - UpdateDecider.DelayAfterLaunch + TestFirstStep, TestMinimumDelay)
                {
                    Channel = channel,
                };

            default:
                return null;
        }
    }

    /// <summary>The channel of this platform: the installed Windows app's when the platform layer names none.</summary>
    public static UpdateChannel Channel => PlatformDefaults.UpdateChannel ?? UpdateChannel.WindowsSetup;

    /// <summary>What the log says: "test run", "test folder", "not installed", or "installed".</summary>
    public static string Why(bool updaterMayRun, AppOptions options, string? exeFolder) => Why(updaterMayRun, options, exeFolder, UpdateChannel.WindowsSetup);

    /// <summary>The same, and "announces only" for a run started as a user starts it, on a platform that only tells.</summary>
    public static string Why(bool updaterMayRun, AppOptions options, string? exeFolder, UpdateChannel channel)
    {
        if (!updaterMayRun)
        {
            return options.SettingsPath is not null && options.SelfTestUpdateFolder is not null ? TestFolder : TestRun;
        }

        if (channel.AnnouncesOnly)
        {
            return AnnouncesOnly;
        }

        return exeFolder is not null && InstallKind.UpdaterIsActive(exeFolder) ? Installed : NotInstalled;
    }

    /// <summary>Does this run check for new versions on its own? The log says it at launch.</summary>
    public static bool IsActive(string why) => why is Installed or AnnouncesOnly;

    /// <summary>The version the release was built with (-p:Version=X.Y.Z). A build without it says 1.0.0, and is not installed anyway.</summary>
    public static UpdateVersion InstalledVersion()
    {
        var version = typeof(UpdateWiring).Assembly.GetName().Version;
        return version is null ? new UpdateVersion(0, 0, 0) : new UpdateVersion(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    /// <summary>
    /// Arms the first timer, once the first-launch card is open. The first launch promises to tell about the check before it
    /// happens: a card that could not open told nothing, so nothing checks on its own in this run. Check now still can.
    /// </summary>
    public static void StartAfterLaunch(UpdateFlow? flow, bool firstRunCardFailed)
    {
        if (!firstRunCardFailed)
        {
            flow?.Start();
        }
    }

    /// <summary>--selftest-update-check: one real GET, off the loop. The only other place that makes the network transport.</summary>
    public static CheckResult SelfTestCheck() => UpdateDownloader.Check(PlatformDefaults.Make(PlatformDefaults.UpdateNetwork, "an update network"), Channel, () => false);

    /// <summary>The test flow's start: a line in the log, and no way to reach Process.Start from here.</summary>
    public static string WouldStartLine(ProcessStartInfo info) =>
        $"update setup: would start {Path.GetFileName(info.FileName)} {string.Join(" ", info.ArgumentList)}";

    private static void WouldStart(ProcessStartInfo info, IUpdateHost host) => host.Journal(WouldStartLine(info));
}
