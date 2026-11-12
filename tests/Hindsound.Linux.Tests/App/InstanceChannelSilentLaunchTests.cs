using System.Diagnostics;
using System.Net.Sockets;
using Hindsound.App;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// A launch that connects and says nothing holds the running instance for its read time only: the launch queued behind it
/// is still read and answered well within the time a second launch waits for its answer.
/// </summary>
[Collection(InstanceChannelCollection.Name)]
public sealed class InstanceChannelSilentLaunchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N")[..8]);

    public InstanceChannelSilentLaunchTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_launch_that_says_nothing_does_not_hold_the_next_one_past_its_wait()
    {
        var log = new List<string>();
        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        using var second = LinuxInstanceChannel.Take(Place(), log.Add);

        // Connected first, so the running instance accepts it first; it stays open and silent to the end of the test.
        using var silent = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        silent.Connect(new UnixDomainSocketEndPoint(Path.Combine(_folder, "i.sock")));

        var clock = Stopwatch.StartNew();
        var telling = Task.Run(() => second.Tell(InstanceMessage.Save));
        var received = Receive(first);

        Assert.Equal(InstanceMessage.Save, received);
        Assert.Equal(true, Answer(telling));
        clock.Stop();

        // The read time of the silent launch, plus room for a busy machine, stays under the 10 s the second launch waits.
        Assert.InRange(clock.ElapsedMilliseconds, 0, 8000);
        Assert.DoesNotContain(log, line => line.Contains("did not answer", StringComparison.Ordinal));
    }

    private InstancePlace Place() => new(_folder, true, Path.Combine(_folder, "i.sock"), Path.Combine(_folder, "i.lock"));

    // What the second launch was told: null when it neither gave up nor got an answer in time.
    private static bool? Answer(Task<bool> telling) => telling.Wait(15_000) ? telling.Result : null;

    // Runs the loop until a message arrives, or fails past the guard.
    private static InstanceMessage? Receive(LinuxInstanceChannel first)
    {
        using var loop = new LinuxLoop();
        InstanceMessage? received = null;
        using var guard = new Timer(_ => loop.Post(loop.Quit), null, 15_000, Timeout.Infinite);
        first.Listen(loop, message =>
        {
            received = message;
            loop.Quit();
        });
        loop.Run();
        return received;
    }
}
