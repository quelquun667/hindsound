using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Linux.App;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Desktop;
using Hindsound.Settings;
using Hindsound.Ui;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The desktop parts on a real Linux: the notification against a notification server the test puts on the session bus of
/// the CI, the single instance in a folder of the user, the autostart folder and the folders of the app.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(PlatformDefaultsCollection.Name)]
public sealed class DesktopOnLinuxTests : IDisposable
{
    private const string Service = "org.freedesktop.Notifications";
    private const uint DoNotQueue = 4;
    private const uint ServerId = 42;

    private static readonly BusMethod RequestName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName");
    private static readonly BusMethod ReleaseName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ReleaseName");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N")[..8]);

    public DesktopOnLinuxTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>A notification server, as GNOME Shell or a notification daemon answers: it records each Notify.</summary>
    private sealed class FakeServer : BusObject
    {
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
                var reader = context.Request.GetBodyReader();
                var call = new NotifyCall(
                    reader.ReadString(),
                    reader.ReadUInt32(),
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadArrayOfString(),
                    reader.ReadDictionaryOfStringToVariantValue(),
                    reader.ReadInt32());
                Calls.Enqueue(call);
                using var writer = context.CreateReplyWriter("u");
                writer.WriteUInt32(ServerId);
                context.Reply(writer.CreateMessage());
            });
        }

        public ConcurrentQueue<NotifyCall> Calls { get; } = new();
    }

    private sealed record NotifyCall(string App, uint Replaces, string Icon, string Summary, string Body, string[] Actions, Dictionary<string, VariantValue> Hints, int Expire);

    private sealed class FakeFolder : IFolderShell
    {
        public ConcurrentQueue<string> Shown { get; } = new();

        public int ShowSelected(string folder, IReadOnlyList<string> files)
        {
            Shown.Enqueue(folder + "|" + string.Join(",", files));
            return 0;
        }
    }

    private static NotificationContent Content(string body, string? clip) =>
        new(NotificationKind.Silent, "Clip saved", body, [], 0, NotificationVisual.Bars, 0, []) { ClipPath = clip };

    // Runs the loop until the work is done, or fails past the guard. The work runs on the pool and reaches the loop by Post.
    private static void RunUntil(LinuxLoop loop, Func<Task> work)
    {
        Exception? failure = null;
        using var guard = new Timer(_ =>
        {
            failure ??= new TimeoutException("the bus did not answer in time");
            loop.Post(loop.Quit);
        }, null, 30_000, Timeout.Infinite);
        _ = Task.Run(async () =>
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
        });
        loop.Run();
        Assert.Null(failure);
    }

    private static async Task Until(Func<bool> done, string what)
    {
        for (var i = 0; i < 200; i++)
        {
            if (done())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(what);
    }

    private static async Task<SessionBus> ServeAsync(FakeServer server)
    {
        var bus = new SessionBus(() => DBusAddress.Session, _ => { });
        Assert.True(await bus.ExportAsync(server));
        var owner = await bus.CallAsync(RequestName, "su", (ref MessageWriter writer) =>
        {
            writer.WriteString(Service);
            writer.WriteUInt32(DoNotQueue);
        }, (Message message, object? _) => message.GetBodyReader().ReadUInt32());
        Assert.Equal(1u, owner.Value);
        return bus;
    }

    private static async Task ReleaseAsync(SessionBus bus)
    {
        await bus.CallAsync(ReleaseName, "s", (ref MessageWriter writer) => writer.WriteString(Service));
        bus.Dispose();
    }

    // Sent again until it lands: the watch of the app is set up off the loop, a moment after the first notification.
    private static async Task SignalUntil(SessionBus server, string member, BodyWriter body, Func<bool> landed)
    {
        for (var i = 0; i < 100 && !landed(); i++)
        {
            await server.EmitSignalAsync("/org/freedesktop/Notifications", Service, member, "u" + (member == "ActionInvoked" ? "s" : "u"), body);
            await Task.Delay(100);
        }

        Assert.True(landed(), member + " never reached the app");
    }

    [Fact]
    public void A_notification_reaches_the_server_silent_and_its_actions_come_back()
    {
        using var loop = new LinuxLoop();
        var server = new FakeServer();
        var folder = new FakeFolder();
        var log = new ConcurrentQueue<string>();
        using var appBus = new SessionBus(() => DBusAddress.Session, log.Enqueue);
        using var notification = new FreedesktopNotification(loop, log.Enqueue, () => appBus, folder, holdMs: 60_000);
        var clicks = 0;
        var finished = 0;
        notification.Clicked = () => Interlocked.Increment(ref clicks);
        notification.Finished = () => Interlocked.Increment(ref finished);
        const string clip = "/home/u/Music/Hindsound/a & b.mp3";

        RunUntil(loop, async () =>
        {
            var serverBus = await ServeAsync(server);
            try
            {
                loop.Post(() => notification.Present(Content("Saved", clip), default, 0));
                await Until(() => server.Calls.Count == 1, "no Notify");
                loop.Post(() => notification.Present(Content("Saved again", clip), default, 0));
                await Until(() => server.Calls.Count == 2, "no second Notify");

                await SignalUntil(serverBus, "ActionInvoked", (ref MessageWriter writer) =>
                {
                    writer.WriteUInt32(ServerId);
                    writer.WriteString("show-folder");
                }, () => !folder.Shown.IsEmpty);
                await SignalUntil(serverBus, "ActionInvoked", (ref MessageWriter writer) =>
                {
                    writer.WriteUInt32(ServerId);
                    writer.WriteString("default");
                }, () => Volatile.Read(ref clicks) > 0);
                await SignalUntil(serverBus, "NotificationClosed", (ref MessageWriter writer) =>
                {
                    writer.WriteUInt32(ServerId);
                    writer.WriteUInt32(2);
                }, () => Volatile.Read(ref finished) > 0);
            }
            finally
            {
                await ReleaseAsync(serverBus);
            }
        });

        var calls = server.Calls.ToArray();
        var first = calls[0];
        Assert.Equal("Hindsound", first.App);
        Assert.Equal(0u, first.Replaces);
        Assert.Equal("io.github.quelquun667.Hindsound", first.Icon);
        Assert.Equal("Clip saved", first.Summary);
        Assert.Equal("Saved", first.Body);
        Assert.Equal(["default", "Open", "show-folder", "Show in folder"], first.Actions);
        Assert.Equal((byte)0, first.Hints["urgency"].GetByte());
        Assert.True(first.Hints["suppress-sound"].GetBool());
        Assert.Equal("io.github.quelquun667.Hindsound", first.Hints["desktop-entry"].GetString());
        Assert.Equal(-1, first.Expire);

        // Replaced in place: one notification at a time, as on Windows.
        Assert.Equal(ServerId, calls[1].Replaces);
        Assert.Equal(ServerId, notification.LastId);
        Assert.Contains("/home/u/Music/Hindsound|" + clip, folder.Shown);
        Assert.True(clicks >= 1);
        Assert.Equal(1, finished);
        Assert.Null(notification.Showing);
    }

    [Fact]
    public void A_notification_counts_as_gone_after_its_hold_and_a_newer_one_gets_its_own()
    {
        using var loop = new LinuxLoop();
        var server = new FakeServer();
        using var appBus = new SessionBus(() => DBusAddress.Session, _ => { });
        using var notification = new FreedesktopNotification(loop, _ => { }, () => appBus, new FakeFolder(), holdMs: 1500);
        var finished = 0;
        var showingWhenOlderHoldPassed = false;
        notification.Finished = () => Interlocked.Increment(ref finished);

        RunUntil(loop, async () =>
        {
            var serverBus = await ServeAsync(server);
            try
            {
                loop.Post(() => notification.Present(Content("one", null), default, 0));
                await Task.Delay(750);
                loop.Post(() => notification.Present(Content("two", null), default, 0));
                await Task.Delay(1250);

                // The hold of the first has passed, not that of the second.
                var seen = new TaskCompletionSource<bool>();
                loop.Post(() => seen.SetResult(notification.Showing is { Body: "two" } && finished == 0));
                showingWhenOlderHoldPassed = await seen.Task;
                await Until(() => Volatile.Read(ref finished) == 1, "never finished");
                await Task.Delay(500);
            }
            finally
            {
                await ReleaseAsync(serverBus);
            }
        });

        Assert.True(showingWhenOlderHoldPassed);
        Assert.Equal(1, finished);
        Assert.Equal(2, server.Calls.Count);
    }

    [Fact]
    public void The_single_instance_makes_its_own_folder_for_the_user_alone_and_answers_on_it()
    {
        var place = InstancePlace.For(name => name == InstancePlace.CacheVariable ? _folder : null, null, SingleInstance.Name)!;
        using var first = LinuxInstanceChannel.Take(place, _ => { });
        using var second = LinuxInstanceChannel.Take(place, _ => { });
        using var loop = new LinuxLoop();
        InstanceMessage? received = null;
        first.Listen(loop, message => received = message);

        RunUntil(loop, async () =>
        {
            Assert.True(second.Tell(InstanceMessage.Save));
            await Until(() => received is not null, "no message");
        });

        Assert.True(first.First);
        Assert.False(second.First);
        Assert.Equal(InstanceMessage.Save, received);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(place.Folder));
        Assert.True(File.Exists(place.Socket));

        first.Dispose();
        Assert.False(File.Exists(place.Socket));
    }

    [Fact]
    public void The_autostart_entry_goes_to_the_autostart_folder_of_the_config_home()
    {
        var before = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _folder);
            var entry = new AutostartEntry(_ => { });
            Assert.Equal(Path.Combine(_folder, "autostart", "io.github.quelquun667.Hindsound.desktop"), entry.FilePath);

            entry.Enable("/home/u/Hindsound.AppImage");
            Assert.Equal(RunEntry.ThisProgram, entry.State("/home/u/Hindsound.AppImage"));
            Assert.Contains("Exec=\"/home/u/Hindsound.AppImage\"", File.ReadAllText(entry.FilePath));
            entry.Disable();
            Assert.False(File.Exists(entry.FilePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", before);
        }
    }

    [Fact]
    public void The_files_of_the_app_follow_the_folders_of_the_user()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".config");
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(home, ".local", "share");

        Assert.Equal(Path.Combine(config, "Hindsound", "settings.json"), SettingsStore.DefaultPath);
        Assert.Equal(Path.Combine(data, "Hindsound", "hindsound.log"), Journal.PathFor(null));
        Assert.Equal(Path.Combine(data, "Hindsound", "Rescued"), ClipSaver.DefaultRescueFolder);

        // The music folder of xdg-user-dirs, ~/Music when the user has none.
        var clips = ExportOptions.DefaultOutputFolder();
        Assert.StartsWith(home + "/", clips, StringComparison.Ordinal);
        Assert.EndsWith("/Hindsound", clips, StringComparison.Ordinal);
        if (!File.Exists(Path.Combine(config, "user-dirs.dirs")))
        {
            Assert.Equal(Path.Combine(home, "Music", "Hindsound"), clips);
        }
    }
}
