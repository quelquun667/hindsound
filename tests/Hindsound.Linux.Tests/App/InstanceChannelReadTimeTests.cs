using System.Net.Sockets;
using Hindsound.App;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// Each connection is read on its own, so the read time is all that lets go of a launch that connects and says nothing:
/// once it is over, the running instance closes that connection and runs nothing for it.
/// </summary>
public sealed class InstanceChannelReadTimeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N")[..8]);

    public InstanceChannelReadTimeTests() => Directory.CreateDirectory(_folder);

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
    public void A_launch_that_stays_silent_is_let_go_after_its_read_time_and_nothing_is_run()
    {
        var log = new List<string>();
        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        using var loop = new LinuxLoop();
        var received = new List<InstanceMessage>();
        first.Listen(loop, received.Add);

        using var silent = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        silent.Connect(new UnixDomainSocketEndPoint(Place().Socket));

        // Past the read time, with room for a busy machine: the running instance must have closed the connection by then.
        silent.ReceiveTimeout = LinuxInstanceChannel.ReadTimeoutMs + 5000;
        int got;
        try
        {
            got = silent.Receive(new byte[8]);
        }
        catch (SocketException)
        {
            got = -1;
        }

        Assert.Equal(0, got);

        // What the read posted before it closed the connection is in the loop already: run it all, then look.
        loop.Quit();
        loop.Run();
        Assert.Empty(received);
        Assert.Empty(log);
    }

    private InstancePlace Place() => new(_folder, true, Path.Combine(_folder, "i.sock"), Path.Combine(_folder, "i.lock"));
}
