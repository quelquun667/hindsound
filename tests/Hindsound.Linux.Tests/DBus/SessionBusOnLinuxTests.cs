using System.Runtime.Versioning;
using Hindsound.Linux.App;
using Hindsound.Linux.DBus;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.DBus;

/// <summary>
/// The loop and the real session bus, as the CI gives them (dbus-run-session). What a connection's thread hears comes back
/// to the loop through Post, as every part of the app does it.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class SessionBusOnLinuxTests
{
    private const string TestName = "io.github.quelquun667.Hindsound.Test";
    private const string TestPath = "/io/github/quelquun667/Hindsound/Test";
    private const uint DoNotQueue = 4;
    private const uint PrimaryOwner = 1;

    private static readonly BusMethod GetId = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetId");
    private static readonly BusMethod RequestName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName");

    // Runs the loop until done, or fails past the guard.
    private static void RunUntil(LinuxLoop loop, Func<Task> work)
    {
        Exception? failure = null;
        using var guard = new Timer(_ =>
        {
            failure ??= new TimeoutException("the bus did not answer in time");
            loop.Quit();
        }, null, 20_000, Timeout.Infinite);
        loop.Post(() => _ = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            loop.Post(loop.Quit);
        }));
        loop.Run();
        Assert.Null(failure);
    }

    [Fact]
    public void The_loop_runs_and_the_session_bus_answers()
    {
        var log = new List<string>();
        using var loop = new LinuxLoop();
        using var bus = new SessionBus(() => DBusAddress.Session, line => loop.Post(() => log.Add(line)));
        string? id = null;

        RunUntil(loop, async () =>
        {
            var answer = await bus.CallAsync(GetId, null, null, (Message message, object? _) => message.GetBodyReader().ReadString());
            loop.Post(() => id = answer.Value);
        });

        Assert.False(string.IsNullOrEmpty(id));
        Assert.Contains("connected to the session bus", log);
    }

    private sealed class Echo : BusObject
    {
        public Echo()
            : base(TestPath) => On(TestName, "Echo", context =>
            {
                var text = context.Request.GetBodyReader().ReadString();
                using var writer = context.CreateReplyWriter("s");
                writer.WriteString(text + " back");
                context.Reply(writer.CreateMessage());
            });
    }

    [Fact]
    public void An_exported_object_answers_a_call_with_arguments()
    {
        using var loop = new LinuxLoop();
        using var bus = new SessionBus(() => DBusAddress.Session, _ => { });
        uint owner = 0;
        string? echoed = null;

        RunUntil(loop, async () =>
        {
            Assert.True(await bus.ExportAsync(new Echo()));
            var requested = await bus.CallAsync(RequestName, "su", (ref MessageWriter writer) =>
            {
                writer.WriteString(TestName);
                writer.WriteUInt32(DoNotQueue);
            }, (Message message, object? _) => message.GetBodyReader().ReadUInt32());
            var answer = await bus.CallAsync(new BusMethod(TestName, TestPath, TestName, "Echo"), "s", (ref MessageWriter writer) => writer.WriteString("ping"), (Message message, object? _) => message.GetBodyReader().ReadString());
            loop.Post(() =>
            {
                owner = requested.Value;
                echoed = answer.Value;
            });
        });

        Assert.Equal(PrimaryOwner, owner);
        Assert.Equal("ping back", echoed);
    }
}
