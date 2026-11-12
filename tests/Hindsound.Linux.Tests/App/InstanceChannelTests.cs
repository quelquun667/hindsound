using System.Text;
using Hindsound.App;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// The single instance of Linux: where its socket goes, what travels on it, and the channel itself. Unix sockets and an
/// exclusive lock file exist on Windows too, so the channel runs here as well, in a folder of the test's own.
/// </summary>
public sealed class InstanceChannelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N")[..8]);

    public InstanceChannelTests() => Directory.CreateDirectory(_folder);

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

    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void The_runtime_folder_comes_first_and_holds_the_socket_itself()
    {
        var place = InstancePlace.For(Env(("XDG_RUNTIME_DIR", "/run/user/1000/"), ("XDG_CACHE_HOME", "/c")), "/home/u", SingleInstance.Name);

        Assert.NotNull(place);
        Assert.Equal("/run/user/1000", place.Folder);
        Assert.False(place.OwnFolder);
        Assert.Equal("/run/user/1000/" + InstancePlace.FileNameFor(SingleInstance.Name) + ".sock", place.Socket);
        Assert.Equal("/run/user/1000/" + InstancePlace.FileNameFor(SingleInstance.Name) + ".lock", place.Lock);
    }

    [Fact]
    public void Without_a_runtime_folder_the_cache_holds_a_folder_of_the_app()
    {
        var fromCache = InstancePlace.For(Env(("XDG_CACHE_HOME", "/var/cache/u")), "/home/u", "k");
        var fromHome = InstancePlace.For(Env(("XDG_RUNTIME_DIR", "relative/dir")), "/home/u", "k");

        Assert.Equal("/var/cache/u/hindsound", fromCache!.Folder);
        Assert.True(fromCache.OwnFolder);
        Assert.Equal("/home/u/.cache/hindsound", fromHome!.Folder);
        Assert.True(fromHome.OwnFolder);
    }

    [Fact]
    public void No_folder_or_a_path_too_long_for_a_socket_gives_no_place()
    {
        Assert.Null(InstancePlace.For(Env(), null, "k"));
        Assert.Null(InstancePlace.For(Env(), "relative", "k"));

        var longest = "/" + new string('a', InstancePlace.MaxSocketBytes - 1 - "/".Length - InstancePlace.FileNameFor("k").Length - ".sock".Length);
        var fits = InstancePlace.For(Env(("XDG_RUNTIME_DIR", longest)), null, "k");
        Assert.NotNull(fits);
        Assert.Equal(InstancePlace.MaxSocketBytes, Encoding.UTF8.GetByteCount(fits.Socket));
        Assert.Null(InstancePlace.For(Env(("XDG_RUNTIME_DIR", longest + "a")), null, "k"));
    }

    [Fact]
    public void The_file_name_is_short_and_follows_the_key()
    {
        var key = SingleInstance.NameFor(AppOptions.Parse(["--settings", "/tmp/x/settings.json"]), ignoreCase: false);

        Assert.Equal(InstancePlace.FileNameFor(key), InstancePlace.FileNameFor(key));
        Assert.NotEqual(InstancePlace.FileNameFor(key), InstancePlace.FileNameFor(SingleInstance.Name));
        Assert.Equal("hindsound-".Length + 16, InstancePlace.FileNameFor(key).Length);
        Assert.Matches("^hindsound-[0-9a-f]{16}$", InstancePlace.FileNameFor(key));
    }

    [Theory]
    [InlineData("Show", "show\n")]
    [InlineData("Save", "save\n")]
    public void Each_message_is_one_line_and_reads_back(string name, string line)
    {
        var message = Enum.Parse<InstanceMessage>(name);
        Assert.Equal(line, Encoding.ASCII.GetString(InstanceWire.Encode(message)));
        Assert.Equal(message, InstanceWire.Decode(InstanceWire.Encode(message)));
        Assert.True(InstanceWire.Complete(InstanceWire.Encode(message)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("show")]
    [InlineData("SAVE\n")]
    [InlineData("save\nsave\n")]
    [InlineData("quit\n")]
    public void Anything_else_on_the_socket_is_no_message(string line) =>
        Assert.Null(InstanceWire.Decode(Encoding.ASCII.GetBytes(line)));

    private InstancePlace Place() => new(_folder, true, Path.Combine(_folder, "i.sock"), Path.Combine(_folder, "i.lock"));

    // Runs the loop until a message arrives, or fails past the guard.
    private static InstanceMessage? Receive(LinuxInstanceChannel first, Func<bool> tell, out bool told)
    {
        using var loop = new LinuxLoop();
        InstanceMessage? received = null;
        var answer = false;
        using var guard = new Timer(_ => loop.Post(loop.Quit), null, 10_000, Timeout.Infinite);
        first.Listen(loop, message =>
        {
            received = message;
            loop.Quit();
        });
        var telling = Task.Run(() => answer = tell());
        loop.Run();
        telling.Wait(10_000);
        told = answer;
        return received;
    }

    [Fact]
    public void The_first_launch_holds_the_place_and_a_second_one_tells_it_to_save()
    {
        var log = new List<string>();
        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        using var second = LinuxInstanceChannel.Take(Place(), log.Add);

        Assert.True(first.First);
        Assert.False(second.First);
        Assert.Equal(InstanceMessage.Save, Receive(first, () => second.Tell(InstanceMessage.Save), out var told));
        Assert.True(told);
        Assert.Empty(log);
    }

    [Fact]
    public void A_plain_second_launch_tells_show()
    {
        using var first = LinuxInstanceChannel.Take(Place(), _ => { });
        using var second = LinuxInstanceChannel.Take(Place(), _ => { });

        Assert.Equal(InstanceMessage.Show, Receive(first, () => second.Tell(InstanceMessage.Show), out var told));
        Assert.True(told);
    }

    [Fact]
    public void The_first_instance_tells_nobody()
    {
        using var first = LinuxInstanceChannel.Take(Place(), _ => { });

        Assert.False(first.Tell(InstanceMessage.Show));
    }

    [Fact]
    public void Once_the_first_is_gone_the_next_launch_is_the_first_again()
    {
        var first = LinuxInstanceChannel.Take(Place(), _ => { });
        Assert.True(first.First);
        first.Dispose();

        using var next = LinuxInstanceChannel.Take(Place(), _ => { });
        using var other = LinuxInstanceChannel.Take(Place(), _ => { });

        Assert.True(next.First);
        Assert.False(other.First);
        Assert.Equal(InstanceMessage.Save, Receive(next, () => other.Tell(InstanceMessage.Save), out var told));
        Assert.True(told);
    }

    [Fact]
    public void A_socket_left_behind_by_a_dead_instance_does_not_stop_the_next_one()
    {
        File.WriteAllText(Place().Socket, "left over");

        var log = new List<string>();
        using var first = LinuxInstanceChannel.Take(Place(), log.Add);
        using var second = LinuxInstanceChannel.Take(Place(), _ => { });

        Assert.True(first.First);

        // The file left behind must give way to the socket: a failed bind says so here, before the exchange, which tells
        // that failure apart from a line read too late.
        Assert.DoesNotContain(log, line => line.StartsWith("single instance: no socket", StringComparison.Ordinal));
        Assert.Equal(InstanceMessage.Show, Receive(first, () => second.Tell(InstanceMessage.Show), out _));
    }

    [Fact]
    public void A_second_launch_with_nobody_listening_says_it_could_not_tell()
    {
        // The lock is held, but the holder has no socket: as when the first instance is still starting or stuck.
        using var held = new FileStream(Place().Lock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var log = new List<string>();
        using var second = LinuxInstanceChannel.Take(Place(), log.Add);

        Assert.False(second.First);
        Assert.False(second.Tell(InstanceMessage.Save));
        Assert.Contains(log, line => line.StartsWith("single instance: the running one did not answer", StringComparison.Ordinal));
    }

    [Fact]
    public void A_folder_that_cannot_be_made_counts_as_the_first_launch()
    {
        var blocked = Path.Combine(_folder, "file");
        File.WriteAllText(blocked, "");
        var place = new InstancePlace(Path.Combine(blocked, "sub"), true, Path.Combine(blocked, "sub", "i.sock"), Path.Combine(blocked, "sub", "i.lock"));
        var log = new List<string>();

        using var channel = LinuxInstanceChannel.Take(place, log.Add);

        Assert.True(channel.First);
        Assert.Contains(log, line => line.StartsWith("single instance: the lock could not be made", StringComparison.Ordinal));
    }
}
