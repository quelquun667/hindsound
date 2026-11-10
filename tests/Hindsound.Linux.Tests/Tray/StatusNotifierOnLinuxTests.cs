using System.Runtime.Versioning;
using Hindsound.Linux.App;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Tray;
using Hindsound.Ui;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.Tray;

/// <summary>
/// The icon on the real session bus of the CI, with a StatusNotifierWatcher of the test's own on a second connection, as a
/// panel would be: the watcher is told the item's name, reads its properties and its menu, opens it and clicks a line.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class StatusNotifierOnLinuxTests
{
    private const uint DoNotQueue = 4;
    private const uint Accent = 0xA3E635;
    private const string Item = StatusNotifierItemObject.Interface;
    private const string Menu = DBusMenuObject.Interface;
    private const string Properties = "org.freedesktop.DBus.Properties";

    private static readonly BusMethod RequestName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName");
    private static readonly BusMethod ReleaseName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ReleaseName");

    private sealed class FakeWatcher : BusObject
    {
        public FakeWatcher()
            : base(StatusNotifierTray.WatcherPath) => On(StatusNotifierTray.WatcherName, "RegisterStatusNotifierItem", context =>
            {
                Registered.TrySetResult(context.Request.GetBodyReader().ReadString());
                TrayWire.ReplyEmpty(context);
            });

        public TaskCompletionSource<string> Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Runs the loop until done, or fails past the guard.
    private static void RunUntil(LinuxLoop loop, Func<Task> work)
    {
        Exception? failure = null;
        using var guard = new Timer(_ =>
        {
            failure ??= new TimeoutException("the bus did not answer in time");
            loop.Quit();
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

    private static Task<T> OnLoop<T>(LinuxLoop loop, Func<T> action)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        loop.Post(() => done.SetResult(action()));
        return done.Task;
    }

    private static Task<BusAnswer<uint>> Own(SessionBus bus, BusMethod method, string name) =>
        bus.CallAsync(method, method == RequestName ? "su" : "s", (ref MessageWriter writer) =>
        {
            writer.WriteString(name);
            if (method == RequestName)
            {
                writer.WriteUInt32(DoNotQueue);
            }
        }, (Message message, object? _) => message.GetBodyReader().ReadUInt32());

    private static string ReadVariantString(Message message, object? _)
    {
        var reader = message.GetBodyReader();
        Assert.Equal("s", reader.ReadSignatureAsString());
        return reader.ReadString();
    }

    private static List<(int Width, int Height, int Bytes)> ReadPixmaps(Message message, object? _)
    {
        var reader = message.GetBodyReader();
        Assert.Equal("a(iiay)", reader.ReadSignatureAsString());
        var pixmaps = new List<(int, int, int)>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            pixmaps.Add((reader.ReadInt32(), reader.ReadInt32(), reader.ReadArrayOfByte().Length));
        }

        return pixmaps;
    }

    // The children of the root as (id, label or type, toggle-state or -1).
    private static (uint Revision, string RootDisplay, List<(int Id, string Text, int Toggle)> Children) ReadLayout(Message message, object? _)
    {
        var reader = message.GetBodyReader();
        var revision = reader.ReadUInt32();
        reader.AlignStruct();
        Assert.Equal(0, reader.ReadInt32());
        var root = reader.ReadDictionaryOfStringToVariantValue();
        var children = new List<(int, string, int)>();
        var end = reader.ReadArrayStart(DBusType.Variant);
        while (reader.HasNext(end))
        {
            Assert.Equal(TrayWire.NodeSignature, reader.ReadSignatureAsString());
            reader.AlignStruct();
            var id = reader.ReadInt32();
            var properties = reader.ReadDictionaryOfStringToVariantValue();
            var grandChildren = reader.ReadArrayStart(DBusType.Variant);
            Assert.False(reader.HasNext(grandChildren));
            var text = properties.TryGetValue("label", out var label) ? label.GetString() : properties["type"].GetString();
            var toggle = properties.TryGetValue("toggle-state", out var state) ? state.GetInt32() : -1;
            children.Add((id, text, toggle));
        }

        return (revision, root["children-display"].GetString(), children);
    }

    [Fact]
    public void A_watcher_gets_the_item_reads_it_opens_its_menu_and_a_click_reaches_the_app()
    {
        using var loop = new LinuxLoop();
        var log = new List<string>();
        using var appBus = new SessionBus(() => DBusAddress.Session, line => loop.Post(() => log.Add(line)));
        using var panelBus = new SessionBus(() => DBusAddress.Session, _ => { });
        var watcher = new FakeWatcher();
        StatusNotifierTray? tray = null;
        var gestures = new List<TrayGesture>();
        var chosen = new TaskCompletionSource<TrayCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var added = false;
        (uint Revision, string RootDisplay, List<(int Id, string Text, int Toggle)> Children) layout = default;
        var aboutToShow = false;
        string? title = null;
        List<(int Width, int Height, int Bytes)>? pixmaps = null;
        string? registeredName = null;

        RunUntil(loop, async () =>
        {
            Assert.True(await panelBus.ExportAsync(watcher));
            Assert.Equal(1u, (await Own(panelBus, RequestName, StatusNotifierTray.WatcherName)).Value);

            added = await OnLoop(loop, () =>
            {
                tray = new StatusNotifierTray(loop, line => loop.Post(() => log.Add(line)), appBus);
                tray.Gesture = gesture =>
                {
                    gestures.Add(gesture);
                    if (gesture == TrayGesture.Menu)
                    {
                        tray.ShowMenu(TrayMenu.WindowlessItems(paused: false, startsWithSession: true, clipSeconds: 30, closingAfterClip: false));
                    }
                };
                tray.Chosen = command => chosen.TrySetResult(command);
                tray.Lost = () => lost.TrySetResult();
                return tray.Add(TrayState.Active, Accent, "Hindsound: listening");
            });
            registeredName = await watcher.Registered.Task;
            var name = registeredName;

            var shown = await panelBus.CallAsync(new BusMethod(name, DBusMenuObject.ObjectPath, Menu, "AboutToShow"), "i", (ref MessageWriter writer) => writer.WriteInt32(0), (Message message, object? _) => message.GetBodyReader().ReadBool());
            aboutToShow = shown.Value;
            layout = (await panelBus.CallAsync(new BusMethod(name, DBusMenuObject.ObjectPath, Menu, "GetLayout"), "iias", (ref MessageWriter writer) =>
            {
                writer.WriteInt32(0);
                writer.WriteInt32(-1);
                writer.WriteArray(Array.Empty<string>());
            }, ReadLayout)).Value;
            title = (await panelBus.CallAsync(new BusMethod(name, StatusNotifierItemObject.ObjectPath, Properties, "Get"), "ss", (ref MessageWriter writer) =>
            {
                writer.WriteString(Item);
                writer.WriteString("Title");
            }, ReadVariantString)).Value;
            pixmaps = (await panelBus.CallAsync(new BusMethod(name, StatusNotifierItemObject.ObjectPath, Properties, "Get"), "ss", (ref MessageWriter writer) =>
            {
                writer.WriteString(Item);
                writer.WriteString("IconPixmap");
            }, ReadPixmaps)).Value;

            // A click on the third line, Pause capture.
            Assert.True((await panelBus.CallAsync(new BusMethod(name, DBusMenuObject.ObjectPath, Menu, "Event"), "isvu", (ref MessageWriter writer) =>
            {
                writer.WriteInt32(3);
                writer.WriteString("clicked");
                writer.WriteVariantInt32(0);
                writer.WriteUInt32(0);
            })).Ok);
            Assert.Equal(TrayCommand.TogglePause, await chosen.Task);

            // The watcher starts again: the app is told to add the icon again.
            await Own(panelBus, ReleaseName, StatusNotifierTray.WatcherName);
            await Own(panelBus, RequestName, StatusNotifierTray.WatcherName);
            await lost.Task;
            await OnLoop(loop, () =>
            {
                tray!.Dispose();
                return 0;
            });
        });

        Assert.True(added);
        Assert.Equal(tray!.ItemName, registeredName);
        Assert.Contains(TrayGesture.Menu, gestures);
        Assert.True(aboutToShow);
        Assert.Equal("submenu", layout.RootDisplay);
        Assert.Equal(1u, layout.Revision);
        Assert.Equal(
            [(1, "Save clip", -1), (2, "separator", -1), (3, "Pause capture", -1), (4, "Open clips folder", -1), (5, "Start with the session", 1), (6, "separator", -1), (7, "Quit", -1)],
            layout.Children);
        Assert.Equal("Hindsound", title);
        Assert.Equal([(22, 22, 22 * 22 * 4), (32, 32, 32 * 32 * 4), (48, 48, 48 * 48 * 4)], pixmaps);
        Assert.Contains("the icon: registered with the StatusNotifierWatcher", log);
    }

    [Fact]
    public void Without_a_watcher_the_icon_is_not_there_and_the_log_says_why()
    {
        using var loop = new LinuxLoop();
        var log = new List<string>();
        using var appBus = new SessionBus(() => DBusAddress.Session, line => loop.Post(() => log.Add(line)));
        var added = true;

        RunUntil(loop, async () =>
        {
            added = await OnLoop(loop, () =>
            {
                using var tray = new StatusNotifierTray(loop, line => loop.Post(() => log.Add(line)), appBus);
                return tray.Add(TrayState.Active, Accent, "Hindsound");
            });
        });

        Assert.False(added);
        Assert.Contains(log, line => line.StartsWith("the icon: unavailable, no StatusNotifierWatcher took it", StringComparison.Ordinal));
    }
}
