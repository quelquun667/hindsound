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
/// The watch of the clicks could not be put on the bus: it is put again by the next notification, or on the next
/// connection, and then a click on "Show in folder" opens the folder once, from the last notification as from an older one
/// still in the server's list.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(PlatformDefaultsCollection.Name)]
public sealed class NotificationWatchRetryOnLinuxTests
{
    private const string Service = "org.freedesktop.Notifications";

    private static readonly BusMethod RequestName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName");
    private static readonly BusMethod ReleaseName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ReleaseName");

    private static readonly NotificationContent First = Clip("/home/u/Music/Hindsound/first/a.mp3");
    private static readonly NotificationContent Second = Clip("/home/u/Music/Hindsound/second/b.mp3");
    private static readonly NotificationContent Third = Clip("/home/u/Music/Hindsound/third/c.mp3");

    // Each notification gets an id of its own, 1 for the first, as a server that keeps them in a list.
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
                var id = (uint)Interlocked.Increment(ref _notified);
                using var writer = context.CreateReplyWriter("u");
                writer.WriteUInt32(id);
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

    // While Failing is set, every watch of the clicks is taken back as soon as it is put, as a put the bus refused.
    private sealed class FailingPuts
    {
        private int _failed;
        private volatile bool _failing = true;

        public bool Failing
        {
            get => _failing;
            set => _failing = value;
        }

        public int Failed => Volatile.Read(ref _failed);

        public async Task<IDisposable?> Put(BusSignal signal, Task<IDisposable?> put)
        {
            var watch = await put;
            if (!Failing || !signal.Equals(FreedesktopNotification.ActionInvoked))
            {
                return watch;
            }

            watch?.Dispose();
            Interlocked.Increment(ref _failed);
            return null;
        }
    }

    private static NotificationContent Clip(string path) =>
        new(NotificationKind.Silent, "Clip saved", Path.GetFileName(path), [], 0, NotificationVisual.Bars, 0, []) { ClipPath = path };

    private static async Task Until(Func<bool> done, string what)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(done(), what);
    }

    private static Task<bool> ShowFolder(SessionBus server, uint id) =>
        server.EmitSignalAsync("/org/freedesktop/Notifications", Service, "ActionInvoked", "us", (ref MessageWriter writer) =>
        {
            writer.WriteUInt32(id);
            writer.WriteString("show-folder");
        });

    // Sent until the watch is up, then left to settle; then one click must open the folder of notification id once.
    private static async Task OneClickOpensOnce(SessionBus server, FakeFolder folder, uint id, NotificationContent clicked)
    {
        for (var i = 0; i < 100 && folder.Shown.IsEmpty; i++)
        {
            await ShowFolder(server, id);
            await Task.Delay(100);
        }

        Assert.False(folder.Shown.IsEmpty, "ActionInvoked never reached the app");
        await Task.Delay(1000);
        folder.Shown.Clear();

        Assert.True(await ShowFolder(server, id));
        await Until(() => !folder.Shown.IsEmpty, "the one click never reached the app");
        await Task.Delay(1000);
        Assert.Equal(Path.GetDirectoryName(clicked.ClipPath), Assert.Single(folder.Shown));
        folder.Shown.Clear();
    }

    private static void Run(Func<LinuxLoop, FreedesktopNotification, SessionBus, SessionBus, FakeServer, FakeFolder, FailingPuts, Task> test)
    {
        using var loop = new LinuxLoop();
        var server = new FakeServer();
        var folder = new FakeFolder();
        var puts = new FailingPuts();
        Exception? failure = null;

        using var appBus = new SessionBus(() => DBusAddress.Session, _ => { });
        using var notification = new FreedesktopNotification(loop, _ => { }, () => appBus, folder, holdMs: 60_000) { PutWatch = puts.Put };
        using var guard = new Timer(_ =>
        {
            failure ??= new TimeoutException("the bus did not answer in time");
            loop.Post(loop.Quit);
        }, null, 60_000, Timeout.Infinite);

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

                await test(loop, notification, appBus, serverBus, server, folder, puts);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
            finally
            {
                await serverBus.CallAsync(ReleaseName, "s", (ref MessageWriter writer) => writer.WriteString(Service));
            }

            loop.Post(loop.Quit);
        });

        loop.Run();
        Assert.Null(failure);
    }

    private static async Task FirstNoticeWithoutWatch(LinuxLoop loop, FreedesktopNotification notification, SessionBus serverBus, FakeServer server, FakeFolder folder, FailingPuts puts)
    {
        loop.Post(() => notification.Present(First, default, 0));
        await Until(() => server.Notified == 1 && puts.Failed >= 1, "no Notify, or no put of the watch");

        // The put failed: a click now has nothing to reach.
        await Task.Delay(1000);
        Assert.True(await ShowFolder(serverBus, 1));
        await Task.Delay(1000);
        Assert.Empty(folder.Shown);
        puts.Failing = false;
    }

    [Fact]
    public void After_a_failed_put_the_next_notification_puts_the_watch_once_and_an_older_notification_still_opens_its_folder()
    {
        Run(async (loop, notification, appBus, serverBus, server, folder, puts) =>
        {
            await FirstNoticeWithoutWatch(loop, notification, serverBus, server, folder, puts);

            loop.Post(() => notification.Present(Second, default, 0));
            await Until(() => server.Notified == 2, "no second Notify");
            await OneClickOpensOnce(serverBus, folder, 2, Second);

            // From the server's list: the first notification, sent before the watch was up.
            await OneClickOpensOnce(serverBus, folder, 1, First);

            // A third notification on the same connection puts no second watch.
            loop.Post(() => notification.Present(Third, default, 0));
            await Until(() => server.Notified == 3, "no third Notify");
            await Task.Delay(1000);
            await OneClickOpensOnce(serverBus, folder, 3, Third);
        });
    }

    [Fact]
    public void After_a_failed_put_a_new_connection_puts_the_watch_once()
    {
        Run(async (loop, notification, appBus, serverBus, server, folder, puts) =>
        {
            await FirstNoticeWithoutWatch(loop, notification, serverBus, server, folder, puts);

            // The connection is lost; the next request for it connects again, and the watch is put from there.
            var lost = await appBus.ConnectionAsync();
            Assert.NotNull(lost);
            lost.Dispose();
            DBusConnection? again = null;
            for (var i = 0; i < 100 && (again is null || ReferenceEquals(again, lost)); i++)
            {
                await Task.Delay(50);
                again = await appBus.ConnectionAsync();
            }

            Assert.NotNull(again);
            Assert.NotSame(lost, again);
            await OneClickOpensOnce(serverBus, folder, 1, First);
        });
    }
}
