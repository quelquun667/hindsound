using System.Security.Cryptography;
using Hindsound.App;
using Hindsound.Tests.Updates;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// Which updater a run gets. Every test runs in a folder of its own that holds a fake uninstaller, as an installed copy
/// does: only a launch without options may then get the real flow, and nothing is called on it here.
/// </summary>
public class UpdateWiringTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly UpdateVersion Next = new(99, 0, 0);

    private static TempFolder InstalledCopy()
    {
        var folder = new TempFolder();
        File.WriteAllBytes(folder.File(InstallKind.UninstallerName), []);
        return folder;
    }

    private static UpdateFlow? Create(string[] args, string? exeFolder, FakeUpdateHost host) =>
        UpdateWiring.Create(AppOptions.UpdaterMayRun(args), AppOptions.Parse(args), exeFolder, host, Now);

    private static void AssertUntouched(FakeUpdateHost host)
    {
        Assert.Empty(host.Lines);
        Assert.Empty(host.Timers);
        Assert.Empty(host.Passes);
        Assert.Empty(host.Announced);
        Assert.Empty(host.SkippedWrites);
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
    [InlineData("--settings", "s.json", "--selftest-update-check")]
    [InlineData("--update-failed", "--no-mic")]
    public void Any_option_gives_no_flow(params string[] args)
    {
        using var copy = InstalledCopy();
        var host = new FakeUpdateHost();

        Assert.Null(Create(args, copy.Path, host));
        Assert.Equal(UpdateWiring.TestRun, UpdateWiring.Why(AppOptions.UpdaterMayRun(args), AppOptions.Parse(args), copy.Path));
        AssertUntouched(host);
    }

    [Theory]
    [InlineData("--nonsense")]
    [InlineData("--setting", "s.json")]
    [InlineData("--settings")]
    [InlineData("--scale", "1")]
    [InlineData("--selftest-delay", "3")]
    [InlineData("")]
    [InlineData("--UPDATE-FAILED")]
    public void Unknown_option_gives_no_flow(params string[] args)
    {
        using var copy = InstalledCopy();

        // These parse to the defaults: only the raw command line tells them from a real launch.
        Assert.Null(Create(args, copy.Path, new FakeUpdateHost()));
    }

    [Fact]
    public void No_option_in_an_installed_copy_gives_the_real_flow_and_calls_nothing()
    {
        using var copy = InstalledCopy();
        var host = new FakeUpdateHost();

        Assert.NotNull(Create([], copy.Path, host));
        Assert.Equal(UpdateWiring.Installed, UpdateWiring.Why(true, new AppOptions(), copy.Path));
        AssertUntouched(host);
    }

    [Fact]
    public void A_run_started_again_by_setup_gives_the_real_flow()
    {
        using var copy = InstalledCopy();

        Assert.NotNull(Create([AppOptions.UpdateFailedOption], copy.Path, new FakeUpdateHost()));
    }

    [Fact]
    public void No_uninstaller_gives_no_flow()
    {
        using var source = new TempFolder();
        var host = new FakeUpdateHost();

        Assert.Null(Create([], source.Path, host));
        Assert.Null(Create([], null, host));
        Assert.Equal(UpdateWiring.NotInstalled, UpdateWiring.Why(true, new AppOptions(), source.Path));
        Assert.Equal(UpdateWiring.NotInstalled, UpdateWiring.Why(true, new AppOptions(), null));
        AssertUntouched(host);
    }

    [Fact]
    public void Test_flow_needs_its_own_settings_and_a_folder()
    {
        using var copy = InstalledCopy();
        using var files = new TempFolder();
        string[] both = ["--settings", files.File("s.json"), "--selftest-update", files.Path];
        string[] folderOnly = ["--selftest-update", files.Path];

        Assert.NotNull(Create(both, copy.Path, new FakeUpdateHost()));
        Assert.Equal(UpdateWiring.TestFolder, UpdateWiring.Why(false, AppOptions.Parse(both), copy.Path));
        Assert.Null(Create(folderOnly, copy.Path, new FakeUpdateHost()));
        Assert.Equal(UpdateWiring.TestRun, UpdateWiring.Why(false, AppOptions.Parse(folderOnly), copy.Path));
    }

    [Fact]
    public void Test_flow_takes_its_first_step_three_seconds_after_the_launch()
    {
        using var files = new TempFolder();
        var host = new FakeUpdateHost { Now = Now };
        var flow = Create(["--settings", files.File("s.json"), "--selftest-update", files.Path], null, host)!;

        flow.AtLaunch(false, default);
        flow.Start();

        Assert.Equal(3000, host.LastDelay);
    }

    [Fact]
    public void Test_flow_keeps_its_state_in_its_folder_and_never_starts_a_process()
    {
        using var files = new TempFolder();
        var state = Path.Combine(files.Path, "updates");
        Directory.CreateDirectory(state);
        var setup = new byte[64 * 1024];
        new Random(7).NextBytes(setup);
        File.WriteAllBytes(UpdateFiles.SetupPath(state, Next), setup);
        Assert.True(new UpdateStateStore(Path.Combine(state, "state.json")).Save(new UpdateStateData
        {
            Pending = new PendingSetup(Next.ToString(), Convert.ToHexStringLower(SHA256.HashData(setup)), setup.Length),
        }));
        var host = new FakeUpdateHost { Now = Now };
        var flow = Create(["--settings", files.File("s.json"), "--selftest-update", files.Path], null, host)!;

        // A Setup waiting at launch is applied: in this flow, "started" is only a line in the log.
        var report = flow.AtLaunch(false, default);

        Assert.True(report.SetupStarted);
        Assert.Contains("update setup: would start setup-99.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /WAITEXIT=330 /RESTARTAPP", host.Lines);
        Assert.Contains("\"expectedVersion\": \"99.0.0\"", File.ReadAllText(Path.Combine(state, "state.json")));
    }

    [Fact]
    public void Test_flow_reads_the_manifest_and_the_setup_from_its_folder()
    {
        using var files = new TempFolder();
        var setup = new byte[(int)UpdateManifestReader.MinSetupBytes + 1000];
        new Random(3).NextBytes(setup);
        File.WriteAllBytes(files.File(FolderTransport.SetupFile), setup);
        File.WriteAllText(files.File(FolderTransport.ManifestFile),
            "{\"product\":\"Hindsound\",\"version\":\"99.0.0\",\"url\":\"https://github.com/quelquun667/hindsound/releases/download/v99.0.0/setup.exe\","
            + "\"sha256\":\"" + Convert.ToHexStringLower(SHA256.HashData(setup)) + "\",\"size\":" + setup.Length + ",\"published\":\"2026-11-07T12:00:00Z\"}");
        var host = new FakeUpdateHost { Now = Now };
        var flow = Create(["--settings", files.File("s.json"), "--selftest-update", files.Path], null, host)!;
        flow.AtLaunch(false, default);
        flow.Start();

        host.Now = Now.AddSeconds(3);
        host.FireLast();
        host.FinishPass();
        host.FinishPass();

        Assert.Contains("update check: Found 99.0.0", host.Lines);

        Assert.Contains("update download: Ready 99.0.0", host.Lines);
        Assert.Equal([(Next, UpdateMode.Auto)], host.Announced);
    }

    [Fact]
    public void Installed_version_is_the_version_of_the_build()
    {
        var version = typeof(UpdateWiring).Assembly.GetName().Version!;

        Assert.Equal(new UpdateVersion(version.Major, version.Minor, version.Build), UpdateWiring.InstalledVersion());
    }
}
