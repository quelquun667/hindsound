using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Hindsound.Input;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Input;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// The portal layer when the portal goes away while the bus stays up: the portal restarts, or closes the session itself.
/// The shortcuts are bound again in a new session, without the app being started again. A fake portal stands in, under a
/// name of its own, on a connection of its own; it answers as the portal's documentation says.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class PortalRestartOnLinuxTests
{
    private const string FakeName = "io.github.quelquun667.Hindsound.FakePortalAgain";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);
    private static readonly HotkeyGesture CtrlShiftF10 = new(HotkeyGesture.ModControl | HotkeyGesture.ModShift, 0x79);
    private static readonly BusMethod RequestName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName");
    private static readonly BusMethod ReleaseName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ReleaseName");

    private sealed class Portal : BusObject
    {
        private readonly SessionBus _bus;

        public Portal(SessionBus bus, bool registry)
            : base(PortalHotkeys.PortalPath)
        {
            _bus = bus;
            if (registry)
            {
                On(PortalHotkeys.RegistryInterface, "Register", context =>
                {
                    Calls.Enqueue("Register");
                    using var writer = context.CreateReplyWriter(null!);
                    context.Reply(writer.CreateMessage());
                });
            }

            On("org.freedesktop.DBus.Properties", "Get", context =>
            {
                using var writer = context.CreateReplyWriter("v");
                writer.WriteVariantUInt32(1);
                context.Reply(writer.CreateMessage());
            });
            On(PortalHotkeys.ShortcutsInterface, "CreateSession", CreateSession);
            On(PortalHotkeys.ShortcutsInterface, "BindShortcuts", BindShortcuts);
        }

        public ConcurrentQueue<string> Calls { get; } = new();

        public ConcurrentQueue<string> Sessions { get; } = new();

        // The session each BindShortcuts was for.
        public ConcurrentQueue<string> Bound { get; } = new();

        private static Dictionary<string, string> Strings(ref Reader reader)
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

        private string Reply(MethodContext context, string token)
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
            var options = Strings(ref reader);
            Calls.Enqueue("CreateSession");
            var session = "/org/freedesktop/portal/desktop/session/" + context.Request.SenderAsString![1..].Replace('.', '_') + "/" + options["session_handle_token"];
            Sessions.Enqueue(session);
            var handle = Reply(context, options["handle_token"]);
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
            var ids = new List<string>();
            var end = reader.ReadArrayStart(DBusType.Struct);
            while (reader.HasNext(end))
            {
                reader.AlignStruct();
                ids.Add(reader.ReadString());
                Strings(ref reader);
            }

            reader.ReadString();
            var options = Strings(ref reader);
            Calls.Enqueue("BindShortcuts");
            Bound.Enqueue(session);
            var handle = Reply(context, options["handle_token"]);
            _ = _bus.EmitSignalAsync(handle, PortalHotkeys.RequestInterface, "Response", "ua{sv}", (ref MessageWriter writer) =>
            {
                writer.WriteUInt32(0);
                var results = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("shortcuts");
                writer.WriteSignature("a(sa{sv})");
                var list = writer.WriteArrayStart(DBusType.Struct);
                foreach (var id in ids)
                {
                    writer.WriteStructureStart();
                    writer.WriteString(id);
                    var properties = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("trigger_description");
                    writer.WriteVariantString("CTRL+SHIFT+F10");
                    writer.WriteDictionaryEnd(properties);
                }

                writer.WriteArrayEnd(list);
                writer.WriteDictionaryEnd(results);
            });
        }

        public Task<bool> CloseSessionAsync(string session) =>
            _bus.EmitSignalAsync(session, PortalHotkeys.SessionInterface, "Closed", "a{sv}", (ref MessageWriter writer) => writer.WriteDictionaryEnd(writer.WriteDictionaryStart()));

        public Task<bool> ActivateAsync(string session, string id) =>
            _bus.EmitSignalAsync(PortalHotkeys.PortalPath, PortalHotkeys.ShortcutsInterface, "Activated", "osta{sv}", (ref MessageWriter writer) =>
            {
                writer.WriteObjectPath(session);
                writer.WriteString(id);
                writer.WriteUInt64(1);
                writer.WriteDictionaryEnd(writer.WriteDictionaryStart());
            });
    }

    private static async Task<T> Within<T>(Task<T> task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(Guard)));
        return await task;
    }

    private static async Task Until(Func<bool> done, string what)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(done(), what);
    }

    private static async Task<(SessionBus Bus, Portal Portal)> StartAsync(bool registry = true)
    {
        var bus = new SessionBus(() => DBusAddress.Session, _ => { });
        var portal = new Portal(bus, registry);
        Assert.True(await bus.ExportAsync(portal));
        var owner = await bus.CallAsync(RequestName, "su", (ref MessageWriter writer) =>
        {
            writer.WriteString(FakeName);
            writer.WriteUInt32(4);
        }, (Message message, object? _) => message.GetBodyReader().ReadUInt32());
        Assert.Equal(1u, owner.Value);
        return (bus, portal);
    }

    private static async Task StopAsync(SessionBus bus)
    {
        await bus.CallAsync(ReleaseName, "s", (ref MessageWriter writer) => writer.WriteString(FakeName));
        bus.Dispose();
    }

    private static async Task<PortalHotkeys> BoundLayerAsync(SessionBus appBus, Portal portal, ConcurrentQueue<string> log)
    {
        var layer = await Within(PortalHotkeys.OpenAsync(appBus, log.Enqueue, FakeName));
        Assert.NotNull(layer);
        layer.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, CtrlShiftF10)]);
        await Until(() => portal.Bound.Count == 1 && !layer.Pending, "the first bind never ended");
        return layer;
    }

    [Fact]
    public async Task A_portal_that_starts_again_gets_the_app_id_a_new_session_and_the_keys_again()
    {
        var log = new ConcurrentQueue<string>();
        using var appBus = new SessionBus(() => DBusAddress.Session, log.Enqueue);
        var (firstBus, first) = await StartAsync();
        using var layer = await BoundLayerAsync(appBus, first, log);

        await StopAsync(firstBus);
        var (secondBus, second) = await StartAsync();
        try
        {
            await Until(() => second.Bound.Count == 1, "the keys were never bound with the new portal");
            Assert.Equal(["Register", "CreateSession", "BindShortcuts"], second.Calls.ToArray());
            Assert.True(second.Sessions.TryPeek(out var session));
            Assert.Equal(session, second.Bound.Single());
            await Until(() => !layer.Pending, "the new bind never ended");
            Assert.Equal(CtrlShiftF10, layer.BoundKey(HotkeyAction.SaveDefault));

            var pressed = new TaskCompletionSource<HotkeyAction>(TaskCreationOptions.RunContinuationsAsynchronously);
            layer.Pressed += action => pressed.TrySetResult(action);
            Assert.True(await second.ActivateAsync(session!, "save-clip"));
            Assert.Equal(HotkeyAction.SaveDefault, await Within(pressed.Task));
            Assert.Contains("hotkeys: the portal left", log);
            Assert.Contains("hotkeys: the portal started again, the shortcuts are bound again", log);
        }
        finally
        {
            await StopAsync(secondBus);
        }
    }

    [Fact]
    public async Task A_session_the_portal_closes_is_replaced_by_a_new_one_with_the_same_keys()
    {
        var log = new ConcurrentQueue<string>();
        using var appBus = new SessionBus(() => DBusAddress.Session, log.Enqueue);
        var (portalBus, portal) = await StartAsync();
        try
        {
            using var layer = await BoundLayerAsync(appBus, portal, log);
            Assert.True(portal.Sessions.TryPeek(out var closed));

            // A Closed for a session that is not the app's changes nothing.
            Assert.True(await portal.CloseSessionAsync("/org/freedesktop/portal/desktop/session/other/x"));
            await Task.Delay(500);
            Assert.Single(portal.Sessions);

            Assert.True(await portal.CloseSessionAsync(closed!));
            await Until(() => portal.Bound.Count == 2, "the keys were never bound in a new session");
            var sessions = portal.Sessions.ToArray();
            Assert.Equal(2, sessions.Length);
            Assert.Equal(sessions[1], portal.Bound.ToArray()[1]);
            Assert.NotEqual(closed, sessions[1]);

            // The app id is registered once per portal: a closed session does not register it again.
            Assert.Single(portal.Calls, call => call == "Register");
            Assert.Contains("hotkeys: the portal closed the session of the shortcuts, they are bound again in a new one", log);
            await Until(() => !layer.Pending, "the new bind never ended");
            Assert.Equal(CtrlShiftF10, layer.BoundKey(HotkeyAction.SaveDefault));
        }
        finally
        {
            await StopAsync(portalBus);
        }
    }

    [Fact]
    public async Task A_portal_without_the_registry_is_logged_with_what_it_needs_to_know_the_app()
    {
        var log = new ConcurrentQueue<string>();
        using var appBus = new SessionBus(() => DBusAddress.Session, log.Enqueue);
        var (portalBus, _) = await StartAsync(registry: false);
        try
        {
            using var layer = await Within(PortalHotkeys.OpenAsync(appBus, log.Enqueue, FakeName));
            Assert.NotNull(layer);
            Assert.Contains(PortalHotkeys.UnregisteredLine("org.freedesktop.DBus.Error.UnknownMethod"), log);
            Assert.DoesNotContain(log, line => line.Contains("takes it from the .desktop file", StringComparison.Ordinal));
        }
        finally
        {
            await StopAsync(portalBus);
        }
    }
}
