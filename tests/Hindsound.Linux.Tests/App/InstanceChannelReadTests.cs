using System.Net.Sockets;
using Hindsound.App;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// How the running instance reads the launches that connect to it: a read that starts late still gets the line that came,
/// a connection held in its read holds nobody behind it, and nothing a launch asked is run once the channel is closed.
/// </summary>
[Collection(InstanceChannelCollection.Name)]
public sealed class InstanceChannelReadTests : IDisposable
{
    // Longer than the 2 s the running instance used to give a connection to send its line.
    private const int Late = 3000;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N")[..8]);

    public InstanceChannelReadTests() => Directory.CreateDirectory(_folder);

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
    public void A_line_that_came_is_read_however_late_the_read_starts()
    {
        var log = new List<string>();
        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        using var second = LinuxInstanceChannel.Take(Place(), log.Add);
        first.BeforeRead = () => Task.Delay(Late);

        var telling = Task.Run(() => second.Tell(InstanceMessage.Show));
        var received = Receive(first);

        Assert.Equal(InstanceMessage.Show, received);
        Assert.Equal(true, Answer(telling));
        Assert.Empty(log);
    }

    [Fact]
    public void The_answer_keeps_a_time_of_its_own_past_the_read_time()
    {
        var log = new List<string>();
        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        using var second = LinuxInstanceChannel.Take(Place(), log.Add);
        first.BeforeAnswer = () => Task.Delay(LinuxInstanceChannel.ReadTimeoutMs + 1000);

        var telling = Task.Run(() => second.Tell(InstanceMessage.Show));
        var received = Receive(first);

        Assert.Equal(InstanceMessage.Show, received);
        Assert.Equal(true, Answer(telling));
        Assert.Empty(log);
    }

    [Fact]
    public void A_connection_held_in_its_read_does_not_hold_the_next_launch()
    {
        var log = new List<string>();
        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        using var second = LinuxInstanceChannel.Take(Place(), log.Add);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        first.BeforeRead = () => Interlocked.Increment(ref reads) == 1 ? hold.Task : Task.CompletedTask;

        // Connected before the channel listens, so the running instance accepts it first; its read never ends in this test.
        using var held = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        held.Connect(new UnixDomainSocketEndPoint(Place().Socket));
        try
        {
            var telling = Task.Run(() => second.Tell(InstanceMessage.Save));
            var received = Receive(first);

            Assert.Equal(InstanceMessage.Save, received);
            Assert.Equal(true, Answer(telling));
            Assert.Empty(log);
        }
        finally
        {
            hold.TrySetResult();
        }
    }

    [Fact]
    public void A_line_still_being_answered_when_the_channel_closes_is_not_run()
    {
        var first = LinuxInstanceChannel.Take(Place(), _ => { });
        using var second = LinuxInstanceChannel.Take(Place(), _ => { });
        var answering = new ManualResetEventSlim();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.BeforeAnswer = () =>
        {
            answering.Set();
            return hold.Task;
        };
        using var loop = new LinuxLoop();
        var received = new List<InstanceMessage>();
        first.Listen(loop, received.Add);

        var telling = Task.Run(() => second.Tell(InstanceMessage.Save));
        Assert.True(answering.Wait(10_000));
        first.Dispose();
        hold.SetResult();

        // The running instance closes the connection once it is done with the line, and only then does the launch return.
        Assert.NotNull(Answer(telling));
        loop.Quit();
        loop.Run();

        Assert.Empty(received);
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
