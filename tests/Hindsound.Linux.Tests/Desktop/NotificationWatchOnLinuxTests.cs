using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Desktop;
using Hindsound.Ui;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The first notification of a run is what connects the bus: the watch of its actions is put on once, so one click on
/// "Show in folder" opens the folder once.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(PlatformDefaultsCollection.Name)]
public sealed class NotificationWatchOnLinuxTests
{
    private const string Service = "org.freedesktop.Notifications";
    private const uint ServerId = 77;

    private static readonly BusMethod RequestName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName");
    private static readonly BusMethod ReleaseName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ReleaseName");

    private sealed class FakeServer : BusObject
    {
        private int _notified;

        public FakeServer()
            : base("/org/freedesktop/Notifications")
        {
            On(Service, "GetCapabilities", context =>
            {
                using var writer = context.CreateReplyWriter("as");
                writer.WriteArray(new[] { "actions", "body" });
                context.Reply(writer.CreateMessage());
            });
            On(Service, "Notify", context =>
            {
                Interlocked.Increment(ref _notified);
                using var writer = context.CreateReplyWriter("u");
                writer.WriteUInt32(ServerId);
                context.Reply(writer.CreateMessage());
            });
        }

        public int Notified => Volatile.Read(ref _notified);
    }

    private sealed class FakeFolder : IFolderShell
    {
        public ConcurrentQueue<string> Shown { get; } = new();

        public int ShowSelected(string folder, IReadOnlyList<string> files)
        {
            Shown.Enqueue(folder);
            return 0;
        }
    }

    private static async Task Until(Func<bool> done, string what)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(done(), what);
    }

    private static Task<bool> ShowFolder(SessionBus server) =>
        server.EmitSignalAsync("/org/freedesktop/Notifications", Service, "ActionInvoked", "us", (ref MessageWriter writer) =>
        {
            writer.WriteUInt32(ServerId);
            writer.WriteString("show-folder");
        });

    [Fact]
    public void One_click_on_show_in_folder_opens_the_folder_once_when_the_notification_connected_the_bus()
    {
        using var loop = new LinuxLoop();
        var server = new FakeServer();
        var folder = new FakeFolder();
        Exception? failure = null;

        // Never connected before the first notification: no tray, no shortcuts.
        using var appBus = new SessionBus(() => DBusAddress.Session, _ => { });
        using var notification = new FreedesktopNotification(loop, _ => { }, () => appBus, folder, holdMs: 60_000);
        using var guard = new Timer(_ =>
        {
            failure ??= new TimeoutException("the bus did not answer in time");
            loop.Post(loop.Quit);
        }, null, 40_000, Timeout.Infinite);

        _ = Task.Run(async () =>
        {
            using var serverBus = new SessionBus(() => DBusAddress.Session, _ => { });
            try
            {
                Assert.True(await serverBus.ExportAsync(server));
                var owner = await serverBus.CallAsync(RequestName, "su", (ref MessageWriter writer) =>
                {
                    writer.WriteString(Service);
                    writer.WriteUInt32(4);
                }, (Message message, object? _) => message.GetBodyReader().ReadUInt32());
                Assert.Equal(1u, owner.Value);

                loop.Post(() => notification.Present(new NotificationContent(NotificationKind.Silent, "Clip saved", "Saved", [], 0, NotificationVisual.Bars, 0, []) { ClipPath = "/home/u/Music/Hindsound/a.mp3" }, default, 0));
                await Until(() => server.Notified == 1, "no Notify");

                // Sent until the watch is up, then left to settle: every watch the app put on has had its copy.
                for (var i = 0; i < 100 && folder.Shown.IsEmpty; i++)
                {
                    await ShowFolder(serverBus);
                    await Task.Delay(100);
                }

                Assert.False(folder.Shown.IsEmpty, "ActionInvoked never reached the app");
                await Task.Delay(1000);
                folder.Shown.Clear();

                Assert.True(await ShowFolder(serverBus));
                await Until(() => !folder.Shown.IsEmpty, "the one click never reached the app");
                await Task.Delay(1000);
                Assert.Single(folder.Shown);

                await serverBus.CallAsync(ReleaseName, "s", (ref MessageWriter writer) => writer.WriteString(Service));
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            loop.Post(loop.Quit);
        });

        loop.Run();
        Assert.Null(failure);
    }
}
