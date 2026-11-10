using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Input;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// The notice of a desktop with no global shortcut, as a notification server on the session bus of the CI receives it: what
/// <see cref="ShortcutNotice.ShowOnce"/> sends, not only the request it is built from.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(PlatformDefaultsCollection.Name)]
public sealed class ShortcutNoticeOnLinuxTests
{
    private const string Service = "org.freedesktop.Notifications";
    private const uint DoNotQueue = 4;

    private static readonly BusMethod RequestName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName");
    private static readonly BusMethod ReleaseName = new("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ReleaseName");

    /// <summary>A notification server that records each Notify.</summary>
    private sealed class FakeServer : BusObject
    {
        public FakeServer()
            : base("/org/freedesktop/Notifications")
        {
            On(Service, "Notify", context =>
            {
                var reader = context.Request.GetBodyReader();
                Calls.Enqueue(new NotifyCall(
                    reader.ReadString(),
                    reader.ReadUInt32(),
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadArrayOfString(),
                    reader.ReadDictionaryOfStringToVariantValue(),
                    reader.ReadInt32()));
                using var writer = context.CreateReplyWriter("u");
                writer.WriteUInt32(7);
                context.Reply(writer.CreateMessage());
            });
        }

        public ConcurrentQueue<NotifyCall> Calls { get; } = new();
    }

    private sealed record NotifyCall(string App, uint Replaces, string Icon, string Summary, string Body, string[] Actions, Dictionary<string, VariantValue> Hints, int Expire);

    [Fact]
    public async Task The_notice_reaches_the_server_with_normal_urgency_and_no_sound()
    {
        var server = new FakeServer();
        using var serverBus = new SessionBus(() => DBusAddress.Session, _ => { });
        Assert.True(await serverBus.ExportAsync(server));
        var owner = await serverBus.CallAsync(RequestName, "su", (ref MessageWriter writer) =>
        {
            writer.WriteString(Service);
            writer.WriteUInt32(DoNotQueue);
        }, (Message message, object? _) => message.GetBodyReader().ReadUInt32());
        Assert.Equal(1u, owner.Value);

        try
        {
            using var appBus = new SessionBus(() => DBusAddress.Session, _ => { });
            var sent = await ShortcutNotice.SendAsync(appBus);
            Assert.True(sent.Ok, sent.Error);
        }
        finally
        {
            await serverBus.CallAsync(ReleaseName, "s", (ref MessageWriter writer) => writer.WriteString(Service));
        }

        var call = Assert.Single(server.Calls);
        Assert.Equal("Hindsound", call.App);
        Assert.Equal(0u, call.Replaces);
        Assert.Equal("Hindsound", call.Summary);
        Assert.Equal(HotkeyChoice.NoShortcutText, call.Body);
        Assert.Empty(call.Actions);
        Assert.Equal((byte)1, call.Hints["urgency"].GetByte());
        Assert.True(call.Hints["suppress-sound"].GetBool());
        Assert.Equal("io.github.quelquun667.Hindsound", call.Hints["desktop-entry"].GetString());
        Assert.Equal(-1, call.Expire);
    }
}
