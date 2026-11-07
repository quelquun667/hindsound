using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>The app around the flow: timers and passes wait until the test runs them, in order.</summary>
internal sealed class FakeUpdateHost : IUpdateHost
{
    public UpdateMode Mode { get; set; } = UpdateMode.Auto;

    public UpdateVersion? Skipped { get; set; }

    public DateTimeOffset Now { get; set; }

    public UpdateSurface Surface { get; set; }

    public bool FirstRunCardOpen { get; set; }

    public List<(int Milliseconds, Action Fire)> Timers { get; } = [];

    public List<Action> Passes { get; } = [];

    public List<(UpdateVersion Version, UpdateMode Mode)> Announced { get; } = [];

    public List<UpdateVersion?> SkippedWrites { get; } = [];

    public List<string> Lines { get; } = [];

    public int Withdrawn { get; private set; }

    public bool Left { get; private set; }

    /// <summary>The next pass ends with this exception, as a pass that threw on its thread.</summary>
    public Exception? ThrowInNextPass { get; set; }

    public int LastDelay => Timers[^1].Milliseconds;

    public void SetSkipped(UpdateVersion? version)
    {
        Skipped = version;
        SkippedWrites.Add(version);
    }

    UpdateSurface IUpdateHost.Surface() => Surface;

    public void After(int milliseconds, Action action) => Timers.Add((milliseconds, action));

    public void RunPass<T>(Func<T> work, Action<T?, Exception?> done)
    {
        var fail = ThrowInNextPass;
        ThrowInNextPass = null;
        Passes.Add(() =>
        {
            if (fail is not null)
            {
                done(default, fail);
                return;
            }

            var result = work();
            BetweenWorkAndDone?.Invoke();
            done(result, null);
        });
    }

    /// <summary>Runs after a pass's work, before the flow hears of it: an antivirus that takes the Setup away.</summary>
    public Action? BetweenWorkAndDone { get; set; }

    public void Announce(UpdateVersion version, UpdateMode mode) => Announced.Add((version, mode));

    public void Withdraw() => Withdrawn++;

    public void Changed()
    {
    }

    public void LeaveForSetup() => Left = true;

    public void Journal(string line) => Lines.Add(line);

    public void FireLast() => Timers[^1].Fire();

    public void FinishPass()
    {
        var pass = Passes[0];
        Passes.RemoveAt(0);
        pass();
    }
}

/// <summary>Serves the manifest and the Setup of 0.2.0, and stops when asked, as the real one does between two blocks.</summary>
internal sealed class ReleaseTransport : IUpdateTransport
{
    public ReleaseTransport()
    {
        Setup = new byte[(int)UpdateManifestReader.MinSetupBytes + 4096];
        new Random(7).NextBytes(Setup);
        Manifest = ManifestFor(Setup);
    }

    public byte[] Manifest { get; set; }

    /// <summary>What the Setup address serves. Change it after the check to serve other bytes than announced.</summary>
    public byte[] Setup { get; set; }

    public TransportStatus ManifestStatus { get; set; } = TransportStatus.Completed;

    /// <summary>For a game that comes right at the end, after the last test of shouldStop.</summary>
    public bool IgnoreStop { get; set; }

    public List<string> Urls { get; } = [];

    public static string SetupUrl => "https://github.com/quelquun667/hindsound/releases/download/v0.2.0/Hindsound-0.2.0-Setup.exe";

    public static byte[] ManifestFor(byte[] setup) => Encoding.UTF8.GetBytes(
        "{\"product\":\"Hindsound\",\"version\":\"0.2.0\",\"url\":\"" + SetupUrl + "\",\"sha256\":\""
        + Convert.ToHexString(SHA256.HashData(setup)) + "\",\"size\":" + setup.Length + ",\"published\":\"2026-11-06T12:00:00Z\"}");

    public TransportResult Get(string url, TransferLimits limits, BodySink sink, Func<bool> shouldStop)
    {
        Urls.Add(url);
        if (!IgnoreStop && shouldStop())
        {
            return new TransportResult(TransportStatus.Stopped);
        }

        var manifest = url == UpdateUrlPolicy.ManifestUrl;
        if (manifest && ManifestStatus != TransportStatus.Completed)
        {
            return new TransportResult(ManifestStatus, 503);
        }

        sink(manifest ? Manifest : Setup);
        return new TransportResult(TransportStatus.Completed);
    }
}

/// <summary>A flow on a temp folder, installed at 0.1.0, launched 3 minutes ago: the first check is due.</summary>
internal sealed class FlowRig : IDisposable
{
    public static readonly UpdateVersion Installed = new(0, 1, 0);
    public static readonly UpdateVersion Next = new(0, 2, 0);
    public static readonly DateTimeOffset Now = new(2026, 11, 7, 14, 2, 0, TimeSpan.FromHours(1));

    private readonly TempFolder _temp = new();

    public FlowRig(string? subfolder = null)
    {
        Folder = subfolder is null ? _temp.Path : Path.Combine(_temp.Path, subfolder);
        StatePath = Path.Combine(Folder, "state.json");
        Store = new UpdateStateStore(StatePath);
        Host.Now = Now;
        Flow = new UpdateFlow(Store, Folder, Installed, Transport, Started.Add, Host, Now - TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(60))
        {
            StateRetryPause = TimeSpan.Zero,
        };
    }

    public string Folder { get; }

    public string StatePath { get; }

    public UpdateStateStore Store { get; }

    public FakeUpdateHost Host { get; } = new();

    public ReleaseTransport Transport { get; } = new();

    public List<ProcessStartInfo> Started { get; } = [];

    public UpdateFlow Flow { get; }

    public UpdateStateData State => Store.Load();

    public PendingSetup PendingSetup => new(Next.ToString(), Convert.ToHexString(SHA256.HashData(Transport.Setup)), Transport.Setup.Length);

    public LaunchReport Launch(bool updateFailed = false, UpdateSurface surface = default)
    {
        var report = Flow.AtLaunch(updateFailed, surface);
        Flow.Start();
        return report;
    }

    public void WriteState(UpdateStateData state) => Assert.True(Store.Save(state));

    /// <summary>The Setup of 0.2.0 on disk, as a finished download leaves it.</summary>
    public void PutSetup() => File.WriteAllBytes(UpdateFiles.SetupPath(Folder, Next), Transport.Setup);

    /// <summary>Every later save fails: the temporary file cannot be created where a folder has its name.</summary>
    public void BlockSaves() => Directory.CreateDirectory(StatePath + ".tmp");

    /// <summary>The first timer fires, and the check it starts finds 0.2.0.</summary>
    public void CheckFinds()
    {
        Host.FireLast();
        Host.FinishPass();
    }

    public void Dispose() => _temp.Dispose();
}
