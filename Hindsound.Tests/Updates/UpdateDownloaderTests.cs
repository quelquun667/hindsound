using System.Security.Cryptography;
using System.Text;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class UpdateDownloaderTests
{
    private static readonly UpdateVersion Next = new(0, 2, 0);
    private static readonly DateTimeOffset Published = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Body(int size = 200_000)
    {
        var body = new byte[size];
        new Random(42).NextBytes(body);
        return body;
    }

    private static UpdateManifest ManifestOf(byte[] body, string? sha256 = null, long? size = null) => new(
        Next,
        "https://github.com/quelquun667/hindsound/releases/download/v0.2.0/Hindsound-0.2.0-Setup.exe",
        sha256 ?? Convert.ToHexString(SHA256.HashData(body)),
        size ?? body.Length,
        Published);

    private static string[] FilesIn(TempFolder folder) => Directory.GetFiles(folder.Path).Select(f => Path.GetFileName(f)!).Order().ToArray();

    // ---- The check

    private static string ManifestJson() =>
        "{\"product\":\"Hindsound\",\"version\":\"0.2.0\",\"url\":\"https://github.com/quelquun667/hindsound/releases/download/v0.2.0/a.exe\","
        + "\"sha256\":\"" + new string('a', 64) + "\",\"size\":8200000,\"published\":\"2026-11-06T12:00:00Z\"}";

    [Fact]
    public void TheCheckAsksForTheManifestOfThisSystemAndNothingElse()
    {
        var transport = new FakeTransport(Encoding.UTF8.GetBytes(ManifestJson()));

        var result = UpdateDownloader.Check(transport, () => false);

        Assert.Equal(CheckStatus.Found, result.Status);
        Assert.Equal(Next, result.Value!.Version);
        Assert.Equal(["https://github.com/quelquun667/hindsound/releases/latest/download/hindsound-update-win-x64.json"], transport.Urls);
    }

    [Fact]
    public void TheCheckHasItsLimits()
    {
        var transport = new FakeTransport(Encoding.UTF8.GetBytes(ManifestJson()));

        UpdateDownloader.Check(transport, () => false);

        var limits = Assert.Single(transport.Limits);
        Assert.Equal(TimeSpan.FromSeconds(15), limits.Total);
        Assert.Equal(16 * 1024, limits.MaxBytes);
    }

    [Fact]
    public void ARefusedManifestIsNotFound()
    {
        var result = UpdateDownloader.Check(new FakeTransport(Encoding.UTF8.GetBytes("{\"product\":\"Other\"}")), () => false);

        Assert.Equal(CheckStatus.ManifestRefused, result.Status);
        Assert.Equal(ManifestStatus.WrongProduct, result.Manifest);
        Assert.Null(result.Value);
    }

    [Fact]
    public void ATransportFailureIsNotARefusedManifest()
    {
        var result = UpdateDownloader.Check(new FakeTransport([], TransportStatus.TimedOut), () => false);

        Assert.Equal(CheckStatus.TransportFailed, result.Status);
        Assert.Equal(TransportStatus.TimedOut, result.Transport.Status);
        Assert.Null(result.Value);
    }

    // ---- The file

    [Fact]
    public void TheFileArrivesUnderItsVersionWithNoPieceLeft()
    {
        using var folder = new TempFolder();
        var body = Body();

        var result = UpdateDownloader.Download(ManifestOf(body), folder.Path, new FakeTransport(body), () => false);

        Assert.Equal(DownloadStatus.Ready, result.Status);
        Assert.Equal(folder.File("setup-0.2.0.exe"), result.Path);
        Assert.Equal(body, File.ReadAllBytes(result.Path!));
        Assert.Equal(["setup-0.2.0.exe"], FilesIn(folder));
    }

    [Fact]
    public void TheHashIsReadInAnyCase()
    {
        using var folder = new TempFolder();
        var body = Body();
        var lower = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

        var result = UpdateDownloader.Download(ManifestOf(body, sha256: lower), folder.Path, new FakeTransport(body), () => false);

        Assert.Equal(DownloadStatus.Ready, result.Status);
    }

    [Fact]
    public void TheFileHasItsLimits()
    {
        using var folder = new TempFolder();
        var body = Body();
        var transport = new FakeTransport(body);

        UpdateDownloader.Download(ManifestOf(body), folder.Path, transport, () => false);

        var limits = Assert.Single(transport.Limits);
        Assert.Equal(TimeSpan.FromMinutes(5), limits.Total);
        Assert.Equal(TimeSpan.FromSeconds(30), limits.Stall);
        Assert.Equal(body.Length, limits.MaxBytes);
    }

    [Fact]
    public void TheBytesGoToAPartFileUntilTheyAreChecked()
    {
        using var folder = new TempFolder();
        var body = Body();
        var transport = new FakeTransport(body)
        {
            BeforeChunk = _ =>
            {
                Assert.False(File.Exists(folder.File("setup-0.2.0.exe")));
                Assert.True(File.Exists(folder.File("setup-0.2.0.exe.part")));
            },
        };

        UpdateDownloader.Download(ManifestOf(body), folder.Path, transport, () => false);

        Assert.Equal(["setup-0.2.0.exe"], FilesIn(folder));
    }

    [Fact]
    public void AWrongHashLeavesNoFile()
    {
        using var folder = new TempFolder();
        var body = Body();

        var result = UpdateDownloader.Download(ManifestOf(body, sha256: new string('0', 64)), folder.Path, new FakeTransport(body), () => false);

        Assert.Equal(DownloadStatus.Mismatch, result.Status);
        Assert.Null(result.Path);
        Assert.Empty(FilesIn(folder));
    }

    [Fact]
    public void AnotherByteOfTheSameSizeIsAWrongFile()
    {
        using var folder = new TempFolder();
        var body = Body();
        var manifest = ManifestOf(body);
        var damaged = (byte[])body.Clone();
        damaged[1234] ^= 1;

        var result = UpdateDownloader.Download(manifest, folder.Path, new FakeTransport(damaged), () => false);

        Assert.Equal(DownloadStatus.Mismatch, result.Status);
        Assert.Empty(FilesIn(folder));
    }

    [Fact]
    public void ATooShortFileIsAWrongFileEvenWithItsOwnHash()
    {
        using var folder = new TempFolder();
        var body = Body();
        var manifest = ManifestOf(body, size: body.Length + 1);

        var result = UpdateDownloader.Download(manifest, folder.Path, new FakeTransport(body), () => false);

        Assert.Equal(DownloadStatus.Mismatch, result.Status);
        Assert.Empty(FilesIn(folder));
    }

    [Theory]
    [InlineData(TransportStatus.Stopped)]
    [InlineData(TransportStatus.TimedOut)]
    [InlineData(TransportStatus.TooLarge)]
    [InlineData(TransportStatus.Blocked)]
    [InlineData(TransportStatus.Failed)]
    public void ACutTransferLeavesNothing(TransportStatus status)
    {
        using var folder = new TempFolder();
        var body = Body();

        var result = UpdateDownloader.Download(ManifestOf(body), folder.Path, new FakeTransport(body[..1000], status), () => false);

        Assert.Equal(DownloadStatus.Interrupted, result.Status);
        Assert.Equal(status, result.Transport.Status);
        Assert.Empty(FilesIn(folder));
    }

    [Fact]
    public void ACutTransferStartsAgainFromZero()
    {
        using var folder = new TempFolder();
        var body = Body();
        var manifest = ManifestOf(body);

        UpdateDownloader.Download(manifest, folder.Path, new FakeTransport(body[..1000], TransportStatus.Failed), () => false);
        var second = UpdateDownloader.Download(manifest, folder.Path, new FakeTransport(body), () => false);

        Assert.Equal(DownloadStatus.Ready, second.Status);
        Assert.Equal(body, File.ReadAllBytes(second.Path!));
    }

    [Fact]
    public void APieceFromAnEarlierRunIsOverwrittenNotContinued()
    {
        using var folder = new TempFolder();
        var body = Body();
        File.WriteAllBytes(folder.File("setup-0.2.0.exe.part"), [1, 2, 3]);

        var result = UpdateDownloader.Download(ManifestOf(body), folder.Path, new FakeTransport(body), () => false);

        Assert.Equal(DownloadStatus.Ready, result.Status);
        Assert.Equal(body, File.ReadAllBytes(result.Path!));
    }

    [Fact]
    public void PiecesAndOldSetupsAreCleanedAfterEachPass()
    {
        using var folder = new TempFolder();
        var body = Body();
        File.WriteAllBytes(folder.File("setup-0.1.0.exe"), [1]);
        File.WriteAllBytes(folder.File("setup-0.1.5.exe.part"), [1]);
        File.WriteAllText(folder.File("state.json"), "{}");

        UpdateDownloader.Download(ManifestOf(body), folder.Path, new FakeTransport(body), () => false);

        Assert.Equal(["setup-0.2.0.exe", "state.json"], FilesIn(folder));
    }

    [Fact]
    public void CleaningAfterAFailureKeepsTheFilesThatAreNotOurs()
    {
        using var folder = new TempFolder();
        var body = Body();
        File.WriteAllText(folder.File("state.json"), "{}");
        File.WriteAllText(folder.File("note.txt"), "x");

        UpdateDownloader.Download(ManifestOf(body), folder.Path, new FakeTransport(body[..10], TransportStatus.Failed), () => false);

        Assert.Equal(["note.txt", "state.json"], FilesIn(folder));
    }

    [Fact]
    public void ADiskThatRefusesTheFileIsAReportedFailure()
    {
        using var folder = new TempFolder();
        var body = Body();
        Directory.CreateDirectory(folder.File("setup-0.2.0.exe.part"));

        var result = UpdateDownloader.Download(ManifestOf(body), folder.Path, new FakeTransport(body), () => false);

        Assert.Equal(DownloadStatus.DiskFailed, result.Status);
    }

    [Fact]
    public void AFolderThatCannotExistIsAReportedFailure()
    {
        using var folder = new TempFolder();
        var body = Body();
        var notAFolder = folder.File("file");
        File.WriteAllText(notAFolder, "x");

        var result = UpdateDownloader.Download(ManifestOf(body), notAFolder, new FakeTransport(body), () => false);

        Assert.Equal(DownloadStatus.DiskFailed, result.Status);
    }

    [Fact]
    public void CleaningAFolderThatDoesNotExistIsNothing()
    {
        using var folder = new TempFolder();

        UpdateFiles.Clean(folder.File("absent"), null);
    }

    [Fact]
    public void AFileInUseIsLeftForTheNextPass()
    {
        using var folder = new TempFolder();
        var path = folder.File("setup-0.1.0.exe");
        File.WriteAllBytes(path, [1]);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            UpdateFiles.Clean(folder.Path, null);
        }

        Assert.True(File.Exists(path));
        UpdateFiles.Clean(folder.Path, null);
        Assert.False(File.Exists(path));
    }

    // ---- The thread

    [Fact]
    public async Task APassRunsOnItsOwnThreadOfLowPriorityAndEnds()
    {
        Thread? seen = null;
        var priority = ThreadPriority.Normal;
        var background = false;
        var pool = true;

        var answer = await UpdatePass.Run(() =>
        {
            seen = Thread.CurrentThread;
            priority = seen.Priority;
            background = seen.IsBackground;
            pool = seen.IsThreadPoolThread;
            return 42;
        });

        Assert.Equal(42, answer);
        Assert.Equal(ThreadPriority.Lowest, priority);
        Assert.True(background);
        Assert.False(pool);
        Assert.True(seen!.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task AFailedPassGivesItsExceptionBack()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePass.Run<int>(() => throw new InvalidDataException()));
    }
}
