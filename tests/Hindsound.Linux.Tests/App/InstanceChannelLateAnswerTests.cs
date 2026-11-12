using Hindsound.App;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// A running instance that answers late, as on a busy machine where its threads come late: a second launch whose message
/// arrived is still told so, and does not write that the running one could not be told.
/// </summary>
[Collection(InstanceChannelCollection.Name)]
public sealed class InstanceChannelLateAnswerTests : IDisposable
{
    // Longer than the time the running instance gives a launch that says nothing.
    private const int Late = 3000;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N")[..8]);

    public InstanceChannelLateAnswerTests() => Directory.CreateDirectory(_folder);

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
    public void A_second_launch_waits_for_a_first_one_that_accepts_late()
    {
        var log = new List<string>();
        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        using var second = LinuxInstanceChannel.Take(Place(), log.Add);

        // The socket of the first already takes connections; it only starts reading them past the delay.
        var telling = Task.Run(() => second.Tell(InstanceMessage.Save));
        Thread.Sleep(Late);
        var received = Receive(first);

        Assert.Equal(InstanceMessage.Save, received);
        Assert.Equal(true, Answer(telling));
        Assert.Empty(log);
    }

    [Fact]
    public void A_whole_line_is_answered_however_late_the_answer_goes()
    {
        var log = new List<string>();
        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        using var second = LinuxInstanceChannel.Take(Place(), log.Add);
        first.BeforeAnswer = () => Task.Delay(Late);

        var telling = Task.Run(() => second.Tell(InstanceMessage.Show));
        var received = Receive(first);

        Assert.Equal(InstanceMessage.Show, received);
        Assert.Equal(true, Answer(telling));
        Assert.Empty(log);
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
