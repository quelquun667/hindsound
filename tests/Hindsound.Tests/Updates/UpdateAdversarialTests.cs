using System.Security.Cryptography;
using System.Text;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>A second look at the updater: what a hostile server, a damaged disk or a bad moment can do to it.</summary>
public class UpdateAdversarialTests
{
    private const string Core =
        "\"product\":\"Hindsound\",\"version\":\"0.2.0\","
        + "\"url\":\"https://github.com/quelquun667/hindsound/releases/download/v0.2.0/s.exe\","
        + "\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\","
        + "\"size\":2000000,\"published\":\"2026-11-06T00:00:00Z\"";

    // JSON text. \xFF is a raw byte that is not UTF-8, \\uD800 an escape that is not a whole character.
    private static readonly string[] HostileManifestFields =
    [
        "\"product\":\"\\uD800\"",
        "\"product\":\"\u00FF\"",
        "\"version\":\"\\uDC00\"",
        "\"url\":\"\\uD83D\"",
        "\"url\":\"\u00FF\"",
        "\"sha256\":\"\\uD800\"",
        "\"published\":\"\\uD800\"",
        "\"published\":\"\u00FF\"",
        "\"published\":\"nope\"",
        "\"published\":123",
        "\"size\":1e400",
        "\"size\":99999999999999999999",
        "\"size\":1.5",
        "\"size\":\"\\uD800\"",
        "\"size\":true",
        "\"\u00FF\":1",
        "\"\\uD800\":1",
        "\"x\":\"\\uD800\"",
        "\"x\":\"\u00FF\"",
    ];

    private static readonly string[] HostileStateFields =
    [
        "\"lastCheck\":\"\\uD800\"",
        "\"lastCheck\":\"\u00FF\"",
        "\"lastCheck\":5",
        "\"lastCheck\":\"0001-01-01T00:00:00+14:00\"",
        "\"highestSeen\":\"\\uD800\"",
        "\"highestSeen\":\"\u00FF\"",
        "\"highestSeen\":5",
        "\"expectedVersion\":\"\\uDC00\"",
        "\"announcedVersion\":\"\\uDC00\"",
        "\"attemptsByVersion\":{\"\\uD800\":1}",
        "\"attemptsByVersion\":{\"\u00FF\":1}",
        "\"attemptsByVersion\":{\"0.2.0\":1e400}",
        "\"attemptsByVersion\":{\"0.2.0\":\"x\"}",
        "\"attemptsByVersion\":{\"0.2.0\":null}",
        "\"attemptsByVersion\":[]",
        "\"pending\":{\"version\":\"\\uD800\"}",
        "\"pending\":{\"version\":\"0.2.0\",\"sha256\":\"\\uD800\",\"size\":5}",
        "\"pending\":{\"version\":\"0.2.0\",\"size\":5}",
        "\"pending\":{\"version\":\"0.2.0\",\"sha256\":null,\"size\":0}",
        "\"pending\":{\"version\":\"..\\\\..\\\\evil\",\"size\":5}",
        "\"pending\":[]",
        "\"pending\":5",
        "\"\\uD800\":1",
    ];

    private static readonly string[] BrokenDocuments = ["", "{", "[", "\"", "null", "0", "[]", "\"\"", "\u00EF\u00BB\u00BF{}", "{}{}", "\0", new string('[', 7000)];

    private static byte[] Raw(string text) => Encoding.Latin1.GetBytes(text);

    [Fact]
    public void NoManifestBytesMakeTheReaderThrow()
    {
        var thrown = new List<string>();
        var documents = HostileManifestFields.SelectMany(f => new[] { "{" + f + "}", "{" + Core + "," + f + "}" }).Concat(BrokenDocuments);
        foreach (var document in documents)
        {
            try
            {
                UpdateManifestReader.Read(Raw(document), out _);
            }
            catch (Exception ex)
            {
                thrown.Add($"{document}: {ex.GetType().Name}");
            }
        }

        Assert.True(thrown.Count == 0, string.Join("\n", thrown));
    }

    [Fact]
    public void AValidManifestStaysValidWhenAnUnknownFieldIsDamaged()
    {
        Assert.Equal(ManifestStatus.Valid, UpdateManifestReader.Read(Raw("{" + Core + ",\"x\":\"\\uD800\"}"), out _));
    }

    [Fact]
    public void NoStateBytesMakeTheStoreOrTheRunnerThrow()
    {
        using var folder = new TempFolder();
        var path = folder.File("state.json");
        var store = new UpdateStateStore(path);
        File.WriteAllBytes(UpdateFiles.SetupPath(folder.Path, new UpdateVersion(0, 2, 0)), [1, 2, 3, 4, 5]);
        var started = 0;
        var thrown = new List<string>();

        foreach (var document in HostileStateFields.Select(f => "{" + f + "}").Concat(BrokenDocuments))
        {
            try
            {
                File.WriteAllBytes(path, Raw(document));
                var state = store.Load();
                UpdateFiles.DownloadedVersion(state, folder.Path);
                new UpdateRunner(store, folder.Path, _ => started++).Run(restartApp: true);
                File.WriteAllBytes(path, Raw(document));
                store.Save(store.Load());
            }
            catch (Exception ex)
            {
                thrown.Add($"{document}: {ex.GetType().Name}");
            }
        }

        Assert.True(thrown.Count == 0, string.Join("\n", thrown));
        Assert.Equal(0, started);
    }

    [Fact]
    public void NoAddressMakesThePolicyThrowOrLeaveTheAllowedHosts()
    {
        string[] addresses =
        [
            "https://" + new string('a', 70000) + ".com/",
            "https://" + new string('\u00E9', 300) + ".com/",
            "https://" + new string('\u00E9', 60) + "." + new string('\u00E9', 60) + ".com/",
            "https://xn--.com/",
            "https://xn--a-.com/",
            "https://xn--\u00E9.com/",
            "https://github.com\u00AD/a",
            "https://github\u3002com/a",
            "https://\uFF47\uFF49\uFF54\uFF48\uFF55\uFF42.com/a",
            "https://[::1]/",
            "https://[::ffff:140.82.121.4]/",
            "https://[fe80::1%25eth0]/",
            "https://140.82.121.4/",
            "https://2354477316/",
            "https://0x8c.0x52.0x79.0x04/",
            "https://github.com:0443/a",
            "https://github.com:99999/a",
            "https://github.com:/a",
            "https://github.com%2f@evil.example/",
            "https://github.com%00.evil.example/",
            "https://evil.example%2f.github.com/",
            "https://git%68ub.com/a",
            "https://github.com./a",
            "https://GITHUB.COM/a",
            "https://\uD83D\uDE00.com/",
            "https://\uD800.com/",
            "https://a\0b/",
            "https://github.com\t/a",
            " https://github.com/a",
            "https://github.com/\r\nHost: evil.example",
            "https://evil.example#@github.com/",
            "https://evil.example?@github.com/",
            "https://github.com/a#\uD800",
            "https://github.com/a?\uD800",
            "https://github.com/" + new string('a', 70000),
            "https://",
            "https:",
            "",
        ];

        var failures = new List<string>();
        foreach (var address in addresses)
        {
            var shown = address.Length > 60 ? address[..60] + "..." : address;
            try
            {
                if (UpdateUrlPolicy.IsAllowedHop(address, out var uri)
                    && (uri!.IdnHost is not ("github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com")
                        || uri.Port != 443
                        || uri.PathAndQuery.IndexOfAny(['\r', '\n', ' ', '\0']) >= 0))
                {
                    failures.Add($"{shown}: went to {uri.IdnHost}:{uri.Port}{uri.PathAndQuery}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{shown}: {ex.GetType().Name}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void NoLocationMakesTheLoopThrowOrConnectOutsideTheAllowedHosts()
    {
        string[] locations =
        [
            "\\\\evil.example\\x",
            "/\\evil.example/",
            "\\/evil.example/",
            "///evil.example/",
            "//evil.example/",
            "https:/evil.example/",
            "https:evil.example",
            "http:evil.example",
            "file:///c:/windows/system32/calc.exe",
            "c:\\windows\\system32\\calc.exe",
            "\\\\?\\c:\\x",
            "ftp://github.com/a",
            "javascript:alert(1)",
            "https://github.com@evil.example/",
            "https://github.com:8443/",
            "https://github.com\\@evil.example/",
            "https://evil.example\\@github.com/",
            "https://evil.example/https://github.com/",
            " https://evil.example/ ",
            "\thttps://evil.example/",
            "\u0000https://evil.example/",
            "https://\uD800/",
            "https://xn--\u00E9.com/",
            "https://" + new string('\u00E9', 300) + ".com/",
            new string('/', 70000),
            "../../../..",
            "?",
            "#",
        ];

        var failures = new List<string>();
        foreach (var location in locations)
        {
            var shown = location.Length > 60 ? location[..60] + "..." : location;
            var asked = 0;
            try
            {
                TransferLoops.FollowRedirects(
                    "https://github.com/a/b",
                    uri =>
                    {
                        asked++;
                        if (uri.IdnHost is not ("github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com") || uri.Port != 443)
                        {
                            failures.Add($"{shown}: connected to {uri.IdnHost}:{uri.Port}");
                        }

                        return asked == 1 ? new HopOutcome(default, location) : new HopOutcome(new TransportResult(TransportStatus.Completed));
                    },
                    () => false);
            }
            catch (Exception ex)
            {
                failures.Add($"{shown}: {ex.GetType().Name}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void AServerDrippingBytesCannotHideAGameGoingFullScreen()
    {
        var clock = TimeSpan.Zero;
        var gameAt = TimeSpan.FromSeconds(3);
        var limits = TransferLimits.ForSetup(50 * 1024 * 1024);

        // One byte a second: never silent long enough for the stall, and 64 KB away for ever.
        var result = TransferLoops.PumpBody(
            (buffer, _) =>
            {
                clock += TimeSpan.FromSeconds(1);
                return new BodyRead(1);
            },
            limits,
            _ => { },
            () => clock >= gameAt,
            () => clock);

        Assert.Equal(TransportStatus.Stopped, result.Status);
        Assert.True(clock <= gameAt + TimeSpan.FromSeconds(2), $"The transfer went on until {clock}.");
    }

    [Fact]
    public void ASetupDamagedWithItsSizeIntactIsFetchedAgainInsteadOfBlockingTheVersion()
    {
        using var folder = new TempFolder();
        var store = new UpdateStateStore(folder.File("state.json"));
        var version = new UpdateVersion(0, 2, 0);
        var setup = UpdateFiles.SetupPath(folder.Path, version);
        byte[] good = [1, 2, 3, 4, 5];
        File.WriteAllBytes(setup, [9, 9, 9, 9, 9]);
        store.Save(new UpdateStateData { Pending = new PendingSetup("0.2.0", Convert.ToHexString(SHA256.HashData(good)), good.Length) });

        var status = new UpdateRunner(store, folder.Path, _ => Assert.Fail("A damaged Setup was started.")).Run(restartApp: true);

        Assert.Equal(RunStatus.SetupChanged, status);
        Assert.Null(store.Load().Pending);
        Assert.False(File.Exists(setup));
        var next = UpdateDecider.Decide(new UpdateSituation
        {
            Event = UpdateEvent.Tick,
            Mode = UpdateMode.Auto,
            Now = DateTimeOffset.UnixEpoch.AddDays(10),
            AppStartedAt = DateTimeOffset.UnixEpoch,
            LastCheck = DateTimeOffset.UnixEpoch.AddDays(10),
            Installed = new UpdateVersion(0, 1, 0),
            Manifest = version,
            Downloaded = UpdateFiles.DownloadedVersion(store.Load(), folder.Path),
        });
        Assert.Equal(UpdateAction.Download, next);
    }

    [Fact]
    public void ASetupOnlyLockedByAnotherProgramIsKeptForTheNextTry()
    {
        using var folder = new TempFolder();
        var store = new UpdateStateStore(folder.File("state.json"));
        var setup = UpdateFiles.SetupPath(folder.Path, new UpdateVersion(0, 2, 0));
        byte[] good = [1, 2, 3, 4, 5];
        File.WriteAllBytes(setup, good);
        store.Save(new UpdateStateData { Pending = new PendingSetup("0.2.0", Convert.ToHexString(SHA256.HashData(good)), good.Length) });

        using (new FileStream(setup, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            new UpdateRunner(store, folder.Path, _ => Assert.Fail("A locked Setup was started.")).Run(restartApp: true);
        }

        Assert.True(File.Exists(setup));
        Assert.NotNull(store.Load().Pending);
    }

    [Theory]
    [InlineData("setup-0.2.0.exe")]
    [InlineData("setup-0.2.0.exe.part")]
    public void ADirectoryInTheWayOfADownloadIsADiskFailure(string name)
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File(name));
        byte[] body = [1, 2, 3, 4, 5];
        var manifest = new UpdateManifest(new UpdateVersion(0, 2, 0), "https://github.com/x", Convert.ToHexString(SHA256.HashData(body)), body.Length, DateTimeOffset.UnixEpoch);

        var result = UpdateDownloader.Download(manifest, folder.Path, new FakeTransport(body), () => false);

        Assert.Equal(DownloadStatus.DiskFailed, result.Status);
    }

    [Fact]
    public void ASetupThatKeepsFailingStartsThreeTimesAtMostOverAnyNumberOfLaunches()
    {
        using var folder = new TempFolder();
        var store = new UpdateStateStore(folder.File("state.json"));
        var installed = new UpdateVersion(0, 1, 0);
        byte[] setup = [1, 2, 3, 4, 5];
        File.WriteAllBytes(UpdateFiles.SetupPath(folder.Path, new UpdateVersion(0, 2, 0)), setup);
        store.Save(new UpdateStateData
        {
            HighestSeen = "0.2.0",
            Pending = new PendingSetup("0.2.0", Convert.ToHexString(SHA256.HashData(setup)), setup.Length),
        });
        var started = 0;
        var runner = new UpdateRunner(store, folder.Path, _ => started++);

        // Each launch: asked at launch, and if nothing runs, asked again when the user quits. Every Setup fails and restarts.
        for (var launch = 0; launch < 12; launch++)
        {
            var atLaunch = Situation(UpdateEvent.Launching, store, folder.Path, installed);
            if (UpdateDecider.Decide(atLaunch) == UpdateAction.Apply && runner.Run(restartApp: true) == RunStatus.Started)
            {
                continue;
            }

            store.Save(store.Load() with { ExpectedVersion = null });
            if (UpdateDecider.Decide(Situation(UpdateEvent.Quitting, store, folder.Path, installed)) == UpdateAction.Apply)
            {
                runner.Run(restartApp: false);
            }
        }

        Assert.Equal(UpdateDecider.MaxAttemptsPerVersion, started);
    }

    [Fact]
    public void TheDeciderKeepsItsPromisesInEverySituation()
    {
        var installed = new UpdateVersion(0, 2, 0);
        var start = DateTimeOffset.UnixEpoch;
        var now = start.AddHours(1);
        UpdateVersion?[] versions = [null, new UpdateVersion(0, 1, 0), new UpdateVersion(0, 3, 0), new UpdateVersion(0, 4, 0)];
        DateTimeOffset?[] lastChecks = [null, now, now.AddHours(1), now.AddDays(-2)];
        var failures = new HashSet<string>();
        var looked = 0;

        foreach (var trigger in Enum.GetValues<UpdateEvent>())
        foreach (var mode in Enum.GetValues<UpdateMode>())
        foreach (var manifest in versions)
        foreach (var highest in versions)
        foreach (var skipped in versions)
        foreach (var downloaded in versions)
        foreach (var expected in versions)
        foreach (var lastCheck in lastChecks)
        foreach (var attempts in new[] { 0, UpdateDecider.MaxAttemptsPerVersion })
        for (var busy = 0; busy < 5; busy++)
        {
            looked++;
            var s = new UpdateSituation
            {
                Event = trigger,
                Mode = mode,
                Now = now,
                AppStartedAt = start,
                LastCheck = lastCheck,
                FullScreen = busy == 1,
                Presentation = busy == 2,
                ClipInProgress = busy == 3,
                SessionEnding = busy == 4,
                Installed = installed,
                Manifest = manifest,
                HighestSeen = highest,
                Skipped = skipped,
                Expected = expected,
                Downloaded = downloaded,
                AttemptsByVersion = new Dictionary<string, int> { ["0.3.0"] = attempts, ["0.4.0"] = attempts },
            };
            var action = UpdateDecider.Decide(s);
            var clicked = trigger == UpdateEvent.UpdateClicked;
            var leaving = trigger is UpdateEvent.Quitting or UpdateEvent.Launching;

            void Promise(bool kept, string promise)
            {
                if (!kept)
                {
                    failures.Add($"{promise} ({trigger}, {mode}, gave {action})");
                }
            }

            if (busy != 0)
            {
                Promise(action is UpdateAction.None or UpdateAction.WaitThirtyMinutes, "Nothing happens during a game, a clip or the end of the session");
            }

            Promise(action != UpdateAction.Apply || clicked || leaving, "Only a click, Quit or a launch applies");
            Promise(action != UpdateAction.Apply || clicked || mode == UpdateMode.Auto, "Without a click, only Auto applies");
            Promise(mode != UpdateMode.Off || trigger != UpdateEvent.Tick || action == UpdateAction.None, "Off does nothing by itself");
            Promise(mode != UpdateMode.Off || action is not UpdateAction.Download, "Off never downloads");
            Promise(mode != UpdateMode.Ask || trigger != UpdateEvent.Tick || action != UpdateAction.Download, "Ask never downloads by itself");
            Promise(!leaving || action is UpdateAction.None or UpdateAction.Apply, "Leaving touches no network");

            if (action == UpdateAction.Apply)
            {
                Promise(downloaded is { } d && d > installed && (highest is not { } h || d >= h), "Only a newer Setup, not below the highest seen, is applied");
                Promise(downloaded != null, "Nothing to apply without a Setup");
                Promise(clicked || attempts == 0, "Three attempts, then only the button");
                Promise(clicked || skipped is not { } skip || downloaded > skip, "A skipped version is not applied by itself");
                Promise(trigger != UpdateEvent.Launching || expected is not { } e || installed >= e, "No new try on the launch right after a failed Setup");
            }

            if (action == UpdateAction.Download)
            {
                Promise(manifest is { } m && m > installed && (highest is not { } h2 || m >= h2), "Only a newer version, not below the highest seen, is downloaded");
                Promise(manifest != downloaded, "What is already there is not fetched again");
                Promise(clicked || attempts == 0, "No download by itself once the attempts are used up");
                Promise(clicked || skipped is not { } skip2 || manifest > skip2, "A skipped version is not downloaded by itself");
            }

            if (action == UpdateAction.Announce)
            {
                Promise(manifest is { } m2 && m2 > installed && (highest is not { } h3 || m2 >= h3), "Only a newer version is announced");
                Promise(attempts == 0, "No announcement once the attempts are used up");
                Promise(skipped is not { } skip3 || manifest > skip3, "A skipped version is not announced");
            }
        }

        Assert.True(looked > 100_000);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void ACheckJustDoneIsNeverDueAgain()
    {
        var start = DateTimeOffset.UnixEpoch;
        foreach (var hours in new[] { 0.0, 0.01, 1, 23, 24, 25, 24 * 400 })
        {
            var now = start.AddHours(hours);
            var next = UpdateDecider.NextCheckAt(UpdateMode.Auto, start, now, now);
            if (hours >= 0.04)
            {
                Assert.Equal(now + UpdateDecider.CheckInterval, next);
            }

            Assert.True(next > now);
        }
    }

    [Fact]
    public void AFastSmallTransferStillTestsTheStopOnlyOnceASecond()
    {
        var clock = TimeSpan.Zero;
        var reads = 0;
        var stopTests = 0;

        var result = TransferLoops.PumpBody(
            (buffer, _) =>
            {
                clock += TimeSpan.FromMilliseconds(10);
                return new BodyRead(++reads <= 1000 ? 1 : 0);
            },
            TransferLimits.ForSetup(50 * 1024 * 1024),
            _ => { },
            () =>
            {
                stopTests++;
                return false;
            },
            () => clock);

        Assert.Equal(TransportStatus.Completed, result.Status);
        Assert.InRange(stopTests, 10, 12);
    }

    // 256 KB against the 1.5 MB of a default thread: a deep recursion would end the whole app, with no exception to catch.
    [Fact]
    public async Task TheHostileInputsFitTheSmallStackOfThePass()
    {
        using var folder = new TempFolder();
        var documents = HostileManifestFields.Select(f => "{" + f + "}").Concat(BrokenDocuments).Append("{" + Core + "}");
        var body = new byte[3 * 1024 * 1024];
        var manifest = new UpdateManifest(new UpdateVersion(0, 2, 0), "https://github.com/x", Convert.ToHexString(SHA256.HashData(body)), body.Length, DateTimeOffset.UnixEpoch);

        var (valid, download, stateSaved) = await UpdatePass.Run(() =>
        {
            var valid = documents.Count(d => UpdateManifestReader.Read(Raw(d), out _) == ManifestStatus.Valid);
            UpdateUrlPolicy.IsAllowedHop("https://github.com/" + new string('a', 70000), out _);
            UpdateUrlPolicy.IsAllowedHop("https://" + new string('é', 300) + ".com/", out _);
            UpdateUrlPolicy.IsAllowedHop("https://xn--é.com/", out _);
            var download = UpdateDownloader.Download(manifest, folder.Path, new FakeTransport(body), () => false);
            var store = new UpdateStateStore(folder.File("state.json"));
            return (valid, download.Status, store.Save(store.Load().WithSeen(manifest.Version)));
        });

        Assert.Equal(1, valid);
        Assert.Equal(DownloadStatus.Ready, download);
        Assert.True(stateSaved);
    }

    [Fact]
    [Trait("Category", "Device")]
    public async Task ARealRequestFitsTheSmallStackOfThePass()
    {
        var result = await UpdatePass.Run(() => new WinHttpTransport().Get(UpdateUrlPolicy.ManifestUrl, TransferLimits.Manifest, _ => { }, () => false));

        // Before the first release GitHub answers 404: what matters is the whole TLS exchange on this thread.
        Assert.True(result.Status == TransportStatus.Completed || result.Detail == 404, $"{result.Status} {result.Detail}");
    }

    private static UpdateSituation Situation(UpdateEvent trigger, UpdateStateStore store, string folder, UpdateVersion installed)
    {
        var state = store.Load();
        UpdateVersion.TryParse(state.ExpectedVersion, out var expected);
        UpdateVersion.TryParse(state.HighestSeen, out var highest);
        return new UpdateSituation
        {
            Event = trigger,
            Mode = UpdateMode.Auto,
            Now = DateTimeOffset.UnixEpoch.AddDays(10),
            AppStartedAt = DateTimeOffset.UnixEpoch.AddDays(10),
            Installed = installed,
            HighestSeen = highest,
            Expected = state.ExpectedVersion is null ? null : expected,
            Downloaded = UpdateFiles.DownloadedVersion(state, folder),
            AttemptsByVersion = state.AttemptsByVersion,
        };
    }
}
