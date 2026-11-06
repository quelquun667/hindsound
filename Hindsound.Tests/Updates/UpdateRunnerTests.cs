using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class UpdateRunnerTests
{
    private static readonly UpdateVersion Next = new(0, 2, 0);
    private static readonly byte[] Setup = [1, 2, 3, 4, 5];

    private sealed class Bench : IDisposable
    {
        public TempFolder Folder { get; } = new();

        public UpdateStateStore Store { get; }

        public List<ProcessStartInfo> Started { get; } = [];

        public Bench(byte[]? onDisk = null, UpdateStateData? state = null)
        {
            Store = new UpdateStateStore(Folder.File("state.json"));
            File.WriteAllBytes(UpdateFiles.SetupPath(Folder.Path, Next), onDisk ?? Setup);
            Store.Save(state ?? new UpdateStateData
            {
                Pending = new PendingSetup("0.2.0", Convert.ToHexString(SHA256.HashData(Setup)), Setup.Length),
            });
        }

        public UpdateRunner Runner(Action<ProcessStartInfo>? start = null) => new(Store, Folder.Path, start ?? Started.Add);

        public void Dispose() => Folder.Dispose();
    }

    [Fact]
    public void TheCommandLineIsExact()
    {
        Assert.Equal(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/WAITEXIT=60"], UpdateRunner.SetupArguments(restartApp: false));
        Assert.Equal(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/WAITEXIT=60", "/RESTARTAPP"], UpdateRunner.SetupArguments(restartApp: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCheckedSetupIsStartedSilently(bool restart)
    {
        using var bench = new Bench();

        var status = bench.Runner().Run(restart);

        Assert.Equal(RunStatus.Started, status);
        var info = Assert.Single(bench.Started);
        Assert.Equal(UpdateFiles.SetupPath(bench.Folder.Path, Next), info.FileName);
        Assert.Equal(UpdateRunner.SetupArguments(restart), info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(bench.Folder.Path, info.WorkingDirectory);
    }

    [Fact]
    public void TheAttemptIsCountedBeforeSetupStarts()
    {
        using var bench = new Bench();
        var attemptsSeenAtStart = -1;
        string? expectedSeenAtStart = null;

        bench.Runner(_ =>
        {
            var onDisk = bench.Store.Load();
            attemptsSeenAtStart = onDisk.AttemptsFor(Next);
            expectedSeenAtStart = onDisk.ExpectedVersion;
        }).Run(restartApp: true);

        Assert.Equal(1, attemptsSeenAtStart);
        Assert.Equal("0.2.0", expectedSeenAtStart);
    }

    [Fact]
    public void TheAttemptsAddUp()
    {
        using var bench = new Bench();

        bench.Runner().Run(true);
        bench.Runner().Run(true);

        Assert.Equal(2, bench.Store.Load().AttemptsFor(Next));
    }

    [Fact]
    public void NothingOnDiskStartsNothing()
    {
        using var bench = new Bench();
        File.Delete(UpdateFiles.SetupPath(bench.Folder.Path, Next));

        Assert.Equal(RunStatus.SetupMissing, bench.Runner().Run(true));
        Assert.Empty(bench.Started);
    }

    [Fact]
    public void NoPendingSetupStartsNothing()
    {
        using var bench = new Bench(state: new UpdateStateData());

        Assert.Equal(RunStatus.SetupMissing, bench.Runner().Run(true));
        Assert.Empty(bench.Started);
    }

    [Fact]
    public void AFileChangedOnDiskIsNotStartedNorCounted()
    {
        using var bench = new Bench(onDisk: [1, 2, 3, 4, 6]);

        Assert.Equal(RunStatus.SetupChanged, bench.Runner().Run(true));
        Assert.Empty(bench.Started);
        Assert.Equal(0, bench.Store.Load().AttemptsFor(Next));
    }

    [Fact]
    public void AFileOfAnotherSizeIsNotStarted()
    {
        using var bench = new Bench(onDisk: [1, 2, 3, 4, 5, 6]);

        Assert.Equal(RunStatus.SetupChanged, bench.Runner().Run(true));
        Assert.Empty(bench.Started);
    }

    [Fact]
    public void AFileLockedBySomeoneElseIsNotStarted()
    {
        using var bench = new Bench();

        using (new FileStream(UpdateFiles.SetupPath(bench.Folder.Path, Next), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(RunStatus.SetupChanged, bench.Runner().Run(true));
        }

        Assert.Empty(bench.Started);
    }

    [Fact]
    public void AnAttemptThatCannotBeRecordedStartsNothing()
    {
        using var bench = new Bench();

        // The temporary file of the state is held: the state can be read, and not written.
        using (new FileStream(bench.Folder.File("state.json.tmp"), FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.Equal(RunStatus.StateNotSaved, bench.Runner().Run(true));
        }

        Assert.Empty(bench.Started);
    }

    [Fact]
    public void NobodyCanWriteToTheFileBetweenTheCheckAndTheStart()
    {
        using var bench = new Bench();
        var setup = UpdateFiles.SetupPath(bench.Folder.Path, Next);
        var refused = false;

        bench.Runner(_ =>
        {
            try
            {
                using var writer = new FileStream(setup, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            }
            catch (IOException)
            {
                refused = true;
            }
        }).Run(true);

        Assert.True(refused);
    }

    [Fact]
    public void ASetupThatCouldNotStartKeepsItsAttemptAndExpectsNothing()
    {
        using var bench = new Bench();

        var status = bench.Runner(_ => throw new Win32Exception(5)).Run(true);

        Assert.Equal(RunStatus.StartFailed, status);
        var state = bench.Store.Load();
        Assert.Equal(1, state.AttemptsFor(Next));
        Assert.Null(state.ExpectedVersion);
    }

    /// <summary>The open file must not stop Windows from running it. A harmless system tool stands in for Setup, with no window.</summary>
    [Fact]
    public void WindowsCanRunTheFileWhileItIsHeldOpen()
    {
        var tool = Path.Combine(Environment.SystemDirectory, "whoami.exe");
        var bytes = File.ReadAllBytes(tool);
        using var bench = new Bench(onDisk: bytes, state: new UpdateStateData
        {
            Pending = new PendingSetup("0.2.0", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length),
        });

        var status = bench.Runner(info =>
        {
            using var process = Process.Start(info)!;
            process.WaitForExit(10_000);
        }).Run(true);

        Assert.Equal(RunStatus.Started, status);
    }

    [Fact]
    public void TheUpdaterIsOnlyActiveNextToTheUninstaller()
    {
        using var folder = new TempFolder();

        Assert.False(InstallKind.UpdaterIsActive(folder.Path));

        File.WriteAllBytes(folder.File("unins000.exe"), [1]);

        Assert.True(InstallKind.UpdaterIsActive(folder.Path));
    }
}
