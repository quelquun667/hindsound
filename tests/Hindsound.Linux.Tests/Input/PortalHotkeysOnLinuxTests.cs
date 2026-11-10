using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Hindsound.Input;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Input;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// The portal layer against a fake GlobalShortcuts portal, put on the CI's session bus under a name of its own, on a
/// connection of its own. It answers each call as the portal's documentation says: a request handle at once, then the
/// Response signal on it.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class PortalHotkeysOnLinuxTests
{
    private const string FakeName = "io.github.quelquun667.Hindsound.FakePortal";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);
    private static readonly HotkeyGesture CtrlShiftF10 = new(HotkeyGesture.ModControl | HotkeyGesture.ModShift, 0x79);
    private static readonly HotkeyGesture CtrlShiftP = new(HotkeyGesture.ModControl | HotkeyGesture.ModShift, 'P');
    private static readonly BusMethod RequestName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName");

    private sealed class FakePortal : BusObject
    {
        private readonly SessionBus _bus;

        public FakePortal(SessionBus bus)
            : base(PortalHotkeys.PortalPath)
        {
            _bus = bus;
            On(PortalHotkeys.RegistryInterface, "Register", context =>
            {
                Calls.Enqueue("Register " + context.Request.GetBodyReader().ReadString());
                ReplyEmpty(context);
            });
            On("org.freedesktop.DBus.Properties", "Get", context =>
            {
                var reader = context.Request.GetBodyReader();
                Calls.Enqueue("Get " + reader.ReadString() + " " + reader.ReadString());
                using var writer = context.CreateReplyWriter("v");
                writer.WriteVariantUInt32(2);
                context.Reply(writer.CreateMessage());
            });
            On(PortalHotkeys.ShortcutsInterface, "CreateSession", CreateSession);
            On(PortalHotkeys.ShortcutsInterface, "BindShortcuts", BindShortcuts);
        }

        public ConcurrentQueue<string> Calls { get; } = new();

        public ConcurrentQueue<(string Session, List<(string Id, string? Description, string? Trigger)> Shortcuts)> Bound { get; } = new();

        public ConcurrentQueue<string> Sessions { get; } = new();

        private static void ReplyEmpty(MethodContext context)
        {
            using var writer = context.CreateReplyWriter(null!);
            context.Reply(writer.CreateMessage());
        }

        private static Dictionary<string, string> ReadOptions(ref Reader reader)
        {
            var options = new Dictionary<string, string>();
            foreach (var (key, value) in reader.ReadDictionaryOfStringToVariantValue())
            {
                if (value.Type == VariantValueType.String)
                {
                    options[key] = value.GetString();
                }
            }

            return options;
        }

        private static string ReplyHandle(MethodContext context, string token)
        {
            var handle = DBusNames.PortalRequestPath(context.Request.SenderAsString!, token);
            using var writer = context.CreateReplyWriter("o");
            writer.WriteObjectPath(handle);
            context.Reply(writer.CreateMessage());
            return handle;
        }

        private void CreateSession(MethodContext context)
        {
            var reader = context.Request.GetBodyReader();
            var options = ReadOptions(ref reader);
            Calls.Enqueue("CreateSession");
            var session = "/org/freedesktop/portal/desktop/session/" + context.Request.SenderAsString![1..].Replace('.', '_') + "/" + options["session_handle_token"];
            Sessions.Enqueue(session);
            var handle = ReplyHandle(context, options["handle_token"]);
            _ = _bus.EmitSignalAsync(handle, PortalHotkeys.RequestInterface, "Response", "ua{sv}", (ref MessageWriter writer) =>
            {
                writer.WriteUInt32(0);
                var results = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("session_handle");
                writer.WriteVariantString(session);
                writer.WriteDictionaryEnd(results);
            });
        }

        private void BindShortcuts(MethodContext context)
        {
            var reader = context.Request.GetBodyReader();
            var session = reader.ReadObjectPathAsString();
            var shortcuts = new List<(string Id, string? Description, string? Trigger)>();
            var end = reader.ReadArrayStart(DBusType.Struct);
            while (reader.HasNext(end))
            {
                reader.AlignStruct();
                var id = reader.ReadString();
                var properties = ReadOptions(ref reader);
                shortcuts.Add((id, properties.GetValueOrDefault("description"), properties.GetValueOrDefault("preferred_trigger")));
            }

            Assert.Equal("", reader.ReadString());
            var options = ReadOptions(ref reader);
            Calls.Enqueue("BindShortcuts");
            Bound.Enqueue((session, shortcuts));
            var handle = ReplyHandle(context, options["handle_token"]);

            // The user takes every key proposed.
            _ = _bus.EmitSignalAsync(handle, PortalHotkeys.RequestInterface, "Response", "ua{sv}", (ref MessageWriter writer) =>
            {
                writer.WriteUInt32(0);
                var results = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("shortcuts");
                writer.WriteSignature("a(sa{sv})");
                var list = writer.WriteArrayStart(DBusType.Struct);
                foreach (var shortcut in shortcuts)
                {
                    writer.WriteStructureStart();
                    writer.WriteString(shortcut.Id);
                    var properties = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("description");
                    writer.WriteVariantString(shortcut.Description ?? "");
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("trigger_description");
                    writer.WriteVariantString(shortcut.Trigger ?? "");
                    writer.WriteDictionaryEnd(properties);
                }

                writer.WriteArrayEnd(list);
                writer.WriteDictionaryEnd(results);
            });
        }

        public Task<bool> ActivateAsync(string session, string id) =>
            _bus.EmitSignalAsync(PortalHotkeys.PortalPath, PortalHotkeys.ShortcutsInterface, "Activated", "osta{sv}", (ref MessageWriter writer) =>
            {
                writer.WriteObjectPath(session);
                writer.WriteString(id);
                writer.WriteUInt64(1234);
                writer.WriteDictionaryEnd(writer.WriteDictionaryStart());
            });
    }

    private static async Task<T> Within<T>(Task<T> task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(Guard)));
        return await task;
    }

    private static async Task<(SessionBus PortalBus, FakePortal Portal)> StartFakeAsync()
    {
        var portalBus = new SessionBus(() => DBusAddress.Session, _ => { });
        var portal = new FakePortal(portalBus);
        Assert.True(await portalBus.ExportAsync(portal));
        var owner = await portalBus.CallAsync(RequestName, "su", (ref MessageWriter writer) =>
        {
            writer.WriteString(FakeName);
            writer.WriteUInt32(4);
        }, (Message message, object? _) => message.GetBodyReader().ReadUInt32());
        Assert.Equal(1u, owner.Value);
        return (portalBus, portal);
    }

    [Fact]
    public async Task The_app_registers_binds_its_keys_and_hears_them_pressed()
    {
        var (portalBus, portal) = await StartFakeAsync();
        using var ownedBus = portalBus;
        var log = new ConcurrentQueue<string>();
        using var appBus = new SessionBus(() => DBusAddress.Session, log.Enqueue);

        using var layer = await Within(PortalHotkeys.OpenAsync(appBus, log.Enqueue, FakeName));
        Assert.NotNull(layer);
        Assert.Equal(
            new[] { "Register " + DBusNames.AppId, "Get " + PortalHotkeys.ShortcutsInterface + " version", "CreateSession" },
            portal.Calls.ToArray());
        Assert.Contains("hotkeys: GlobalShortcuts portal, version 2", log);

        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        layer.BindingsChanged += () => changed.TrySetResult(true);
        var answers = layer.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, CtrlShiftF10), new HotkeyRequest(HotkeyAction.TogglePause, CtrlShiftP)]);
        Assert.All(answers, a => Assert.Equal(HotkeyBindingState.Pending, a.State));

        await Within(changed.Task);
        Assert.False(layer.Pending);
        Assert.Equal(CtrlShiftF10, layer.BoundKey(HotkeyAction.SaveDefault));
        Assert.Equal(CtrlShiftP, layer.BoundKey(HotkeyAction.TogglePause));
        Assert.True(portal.Bound.TryPeek(out var bound));
        Assert.True(portal.Sessions.TryPeek(out var session));
        Assert.Equal(session, bound.Session);
        Assert.Equal(new[] { ("save-clip", (string?)"Save a clip", (string?)"CTRL+SHIFT+F10"), ("pause", "Pause or resume", "CTRL+SHIFT+p") }, bound.Shortcuts);

        // A press for another session is not ours; the one for ours comes through.
        var pressed = new ConcurrentQueue<HotkeyAction>();
        var heard = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        layer.Pressed += action =>
        {
            pressed.Enqueue(action);
            heard.TrySetResult(true);
        };
        Assert.True(await portal.ActivateAsync("/org/freedesktop/portal/desktop/session/other/x", "pause"));
        Assert.True(await portal.ActivateAsync(session!, "save-clip"));
        await Within(heard.Task);
        await Task.Delay(200);
        Assert.Equal(new[] { HotkeyAction.SaveDefault }, pressed.ToArray());
    }

    [Fact]
    public async Task New_keys_are_bound_in_a_new_session()
    {
        var (portalBus, portal) = await StartFakeAsync();
        using var ownedBus = portalBus;
        using var appBus = new SessionBus(() => DBusAddress.Session, _ => { });
        using var layer = await Within(PortalHotkeys.OpenAsync(appBus, _ => { }, FakeName));
        Assert.NotNull(layer);

        var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        layer.BindingsChanged += () =>
        {
            if (!first.TrySetResult(true))
            {
                second.TrySetResult(true);
            }
        };
        layer.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, CtrlShiftF10)]);
        await Within(first.Task);
        layer.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, CtrlShiftP)]);
        await Within(second.Task);

        var binds = portal.Bound.ToArray();
        Assert.Equal(2, binds.Length);
        Assert.NotEqual(binds[0].Session, binds[1].Session);
        Assert.Equal(2, portal.Sessions.Count);
        Assert.Equal(CtrlShiftP, layer.BoundKey(HotkeyAction.SaveDefault));
    }

    [Fact]
    public async Task A_bus_without_the_portal_gives_no_layer()
    {
        var log = new ConcurrentQueue<string>();
        using var appBus = new SessionBus(() => DBusAddress.Session, log.Enqueue);

        var layer = await Within(PortalHotkeys.OpenAsync(appBus, log.Enqueue, "io.github.quelquun667.Hindsound.NoPortal"));

        Assert.Null(layer);
        Assert.Contains(log, line => line.StartsWith("hotkeys: no GlobalShortcuts portal (", StringComparison.Ordinal));
    }
}
