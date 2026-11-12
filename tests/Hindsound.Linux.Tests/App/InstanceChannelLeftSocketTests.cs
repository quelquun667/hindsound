using Hindsound.App;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// A file left where the socket goes, as a dead instance leaves it: the next instance takes its place and says nothing about
/// it, and a second launch reaches it.
/// </summary>
public sealed class InstanceChannelLeftSocketTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N")[..8]);

    public InstanceChannelLeftSocketTests() => Directory.CreateDirectory(_folder);

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
    public void A_file_left_in_place_of_the_socket_is_replaced_and_the_log_stays_quiet()
    {
        File.WriteAllText(Place().Socket, "left over");
        var log = new List<string>();

        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        Assert.True(first.First);
        Assert.Empty(log);

        using var second = LinuxInstanceChannel.Take(Place(), log.Add);
        var telling = Task.Run(() => second.Tell(InstanceMessage.Show));
        var received = Receive(first);

        Assert.Equal(InstanceMessage.Show, received);
        Assert.Equal(true, telling.Wait(15_000) ? telling.Result : null);
        Assert.Empty(log);
    }

    private InstancePlace Place() => new(_folder, true, Path.Combine(_folder, "i.sock"), Path.Combine(_folder, "i.lock"));

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
