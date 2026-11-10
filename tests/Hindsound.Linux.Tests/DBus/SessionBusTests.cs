using Hindsound.Linux.DBus;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.DBus;

/// <summary>A bus out of reach: every helper answers, none throws, and the log says it once.</summary>
public sealed class SessionBusTests
{
    private static readonly BusMethod GetId = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetId");

    [Fact]
    public async Task Without_an_address_there_is_no_connection_and_the_log_says_it_once()
    {
        var log = new List<string>();
        using var bus = new SessionBus(() => null, log.Add);

        Assert.Null(await bus.ConnectionAsync());
        Assert.Null(await bus.ConnectionAsync());

        Assert.Equal(["no session bus: DBUS_SESSION_BUS_ADDRESS is not set"], log);
    }

    [Fact]
    public async Task A_bus_out_of_reach_gives_answers_not_exceptions()
    {
        var log = new List<string>();
        using var bus = new SessionBus(() => "unix:path=/nonexistent/hindsound-test-bus", log.Add);

        var answer = await bus.CallAsync(GetId, null, null, (Message message, object? _) => message.GetBodyReader().ReadString());
        var done = await bus.CallAsync(GetId, null, null);
        var watch = await bus.WatchSignalAsync(new BusSignal(null, null, "org.freedesktop.DBus", "NameOwnerChanged"), (Message message, object? _) => 0, _ => { });
        var exported = await bus.ExportAsync(new Nothing());
        var sent = await bus.EmitSignalAsync("/io/github/quelquun667/Hindsound", "io.github.quelquun667.Hindsound", "Changed", null, null);

        Assert.False(answer.Ok);
        Assert.Equal("no session bus", answer.Error);
        Assert.False(done.Ok);
        Assert.Null(watch);
        Assert.False(exported);
        Assert.False(sent);
        Assert.Single(log);
        Assert.StartsWith("the session bus could not be reached: ", log[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Once_disposed_the_bus_gives_nothing_and_says_nothing()
    {
        var log = new List<string>();
        var bus = new SessionBus(() => "unix:path=/nonexistent/hindsound-test-bus", log.Add);
        bus.Dispose();

        Assert.Null(await bus.ConnectionAsync());
        Assert.Empty(log);
    }

    private sealed class Nothing() : BusObject("/io/github/quelquun667/Hindsound/Nothing");
}
