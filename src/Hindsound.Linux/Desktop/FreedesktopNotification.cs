using Hindsound.App;
using Hindsound.Linux.DBus;
using Hindsound.Ui;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// The notification, through org.freedesktop.Notifications: silent, low urgency, replaced in place, with the action
/// "Show in folder" when it is about a clip. The server draws it where it likes, so the spot the app chose is not used.
/// The server keeps a notification in its list long after it left the screen, so the app counts it gone after
/// <see cref="HoldMs"/>, or sooner when the server says it closed: the notices of the app never wait on that list.
/// </summary>
internal sealed class FreedesktopNotification : INotificationView
{
    public const int HoldMs = 5000;

    // The clips of the last notifications, for a click on "Show in folder" after a newer one: one id per notification.
    private const int ClipsKept = 8;

    internal static readonly BusMethod Notify = new(Service, ServicePath, Service, "Notify");
    internal static readonly BusMethod CloseNotification = new(Service, ServicePath, Service, "CloseNotification");
    internal static readonly BusMethod GetCapabilities = new(Service, ServicePath, Service, "GetCapabilities");
    internal static readonly BusSignal ActionInvoked = new(Service, ServicePath, Service, "ActionInvoked");
    internal static readonly BusSignal NotificationClosed = new(Service, ServicePath, Service, "NotificationClosed");

    private const string Service = "org.freedesktop.Notifications";
    private const string ServicePath = "/org/freedesktop/Notifications";
    private const string BodyMarkup = "body-markup";

    private readonly IAppLoop _loop;
    private readonly Action<string> _log;
    private readonly Func<SessionBus> _bus;
    private readonly IFolderShell _folder;
    private readonly int _holdMs;
    private readonly List<(uint Id, string Clip)> _clips = [];
    private readonly object _watchGate = new();
    private readonly List<IDisposable> _watches = [];
    private readonly ConnectionClaim _watchedOn = new();

    // Only the chain of sends, one after the other, touches these: each send reads the id the one before it got.
    private Task _chain = Task.CompletedTask;
    private uint _lastId;
    private bool? _markup;

    private int _shown;
    private bool _watching;
    private bool _failureSaid;
    private bool _disposed;

    public FreedesktopNotification(IAppLoop loop, Action<string> log)
        : this(loop, log, () => SessionBus.Shared, new LinuxFolderShell(log), HoldMs)
    {
    }

    internal FreedesktopNotification(IAppLoop loop, Action<string> log, Func<SessionBus> bus, IFolderShell folder, int holdMs)
    {
        _loop = loop;
        _log = log;
        _bus = bus;
        _folder = folder;
        _holdMs = holdMs;
    }

    public NotificationContent? Showing { get; private set; }

    public Action? Clicked { get; set; }

    public Action? Finished { get; set; }

    /// <summary>The id the server gave the last notification, for a test; 0 before any.</summary>
    internal uint LastId => Volatile.Read(ref _lastId);

    public void Present(NotificationContent content, NotificationSpot spot, uint accent)
    {
        if (_disposed)
        {
            return;
        }

        Showing = content;
        var shown = ++_shown;
        if (!_watching)
        {
            _watching = true;
            _ = Task.Run(WatchAsync);
        }

        _chain = _chain.ContinueWith(_ => SendAsync(content, shown), TaskScheduler.Default).Unwrap();
        _loop.After(_holdMs, () => FinishIfStill(shown));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_watchGate)
        {
            foreach (var watch in _watches)
            {
                watch.Dispose();
            }

            _watches.Clear();
        }

        // A notice still up goes with the app, as it does on Windows; the clips of earlier ones stay in the server's list.
        if (Showing is not null && LastId is var id and not 0)
        {
            _ = Task.Run(() => Bus()?.CallAsync(CloseNotification, "u", (ref MessageWriter writer) => writer.WriteUInt32(id)));
        }

        Showing = null;
    }

    private async Task SendAsync(NotificationContent content, int shown)
    {
        if (Bus() is not { } bus)
        {
            _loop.Post(() => Failed(shown, "no session bus"));
            return;
        }

        _markup ??= await ReadsMarkupAsync(bus).ConfigureAwait(false);
        var request = NotificationRequest.For(content, Volatile.Read(ref _lastId), _markup.Value);
        var answer = await bus.CallAsync(Notify, NotificationRequest.Signature, (ref MessageWriter writer) => Write(ref writer, request), (Message message, object? _) => message.GetBodyReader().ReadUInt32()).ConfigureAwait(false);
        if (!answer.Ok)
        {
            _loop.Post(() => Failed(shown, answer.Error ?? "no answer"));
            return;
        }

        var id = answer.Value;
        Volatile.Write(ref _lastId, id);
        if (content.ClipPath is { } clip)
        {
            _loop.Post(() => KeepClip(id, clip));
        }
    }

    /// <summary>Writes <paramref name="request"/> as the body of Notify.</summary>
    internal static void Write(ref MessageWriter writer, NotificationRequest request)
    {
        writer.WriteString(request.AppName);
        writer.WriteUInt32(request.ReplacesId);
        writer.WriteString(request.Icon);
        writer.WriteString(request.Summary);
        writer.WriteString(request.Body);
        writer.WriteArray(request.Actions.ToArray());
        var hints = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("urgency");
        writer.WriteVariantByte(request.Urgency);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("suppress-sound");
        writer.WriteVariantBool(request.SuppressSound);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("desktop-entry");
        writer.WriteVariantString(request.DesktopEntry);
        writer.WriteDictionaryEnd(hints);
        writer.WriteInt32(request.ExpireTimeout);
    }

    private async Task<bool> ReadsMarkupAsync(SessionBus bus)
    {
        var answer = await bus.CallAsync(GetCapabilities, null, null, (Message message, object? _) => message.GetBodyReader().ReadArrayOfString()).ConfigureAwait(false);
        return answer is { Ok: true, Value: { } capabilities } && capabilities.Contains(BodyMarkup);
    }

    private async Task WatchAsync()
    {
        if (Bus() is not { } bus)
        {
            return;
        }

        // A new connection after a lost one has no watch: it is put back from there. The first one may come through both
        // paths, when this asks for it: only the first to claim it watches, or each signal would arrive twice.
        bus.Connected += connection => { _ = WatchOnceAsync(bus); };
        await WatchOnceAsync(bus).ConfigureAwait(false);
    }

    private Task WatchOnceAsync(SessionBus bus) => _watchedOn.WatchOnceAsync(bus.ConnectionAsync, () => PutWatchesAsync(bus));

    private async Task PutWatchesAsync(SessionBus bus)
    {
        var action = await bus.WatchSignalAsync(ActionInvoked, ReadIdAndKey, pair => _loop.Post(() => OnAction(pair.Id, pair.Key))).ConfigureAwait(false);
        var closed = await bus.WatchSignalAsync(NotificationClosed, ReadIdAndReason, pair => _loop.Post(() => OnClosed(pair.Id))).ConfigureAwait(false);
        lock (_watchGate)
        {
            foreach (var watch in new[] { action, closed })
            {
                if (watch is null)
                {
                    continue;
                }

                if (_disposed)
                {
                    watch.Dispose();
                }
                else
                {
                    _watches.Add(watch);
                }
            }
        }
    }

    private static (uint Id, string Key) ReadIdAndKey(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadUInt32(), reader.ReadString());
    }

    private static (uint Id, uint Reason) ReadIdAndReason(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadUInt32(), reader.ReadUInt32());
    }

    private void OnAction(uint id, string key)
    {
        if (_disposed || id == 0)
        {
            return;
        }

        string? clip = _clips.FindLast(entry => entry.Id == id).Clip;
        if (id != LastId && clip is null)
        {
            // Another app's notification: the signal goes to every listener.
            return;
        }

        switch (NotificationRequest.ActionOf(key))
        {
            case NotificationAction.Click:
                Clicked?.Invoke();
                break;

            case NotificationAction.ShowFolder when clip is not null:
                ShowInFolder(clip);
                break;
        }
    }

    private void OnClosed(uint id)
    {
        if (id != 0 && id == LastId)
        {
            FinishIfStill(_shown);
        }
    }

    // Off the loop: the file manager is reached over the bus, or started.
    private void ShowInFolder(string clip)
    {
        var folder = Path.GetDirectoryName(clip);
        if (folder is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            var result = _folder.ShowSelected(folder, [clip]);
            if (result < 0)
            {
                _log($"show in folder from the notification: failed (0x{result:X8})");
            }
        });
    }

    private void KeepClip(uint id, string clip)
    {
        _clips.RemoveAll(entry => entry.Id == id);
        _clips.Add((id, clip));
        if (_clips.Count > ClipsKept)
        {
            _clips.RemoveAt(0);
        }
    }

    private void Failed(int shown, string error)
    {
        if (!_failureSaid)
        {
            _failureSaid = true;
            _log($"notification: not shown ({error})");
        }

        // Nothing is on screen: the next notice must not wait the whole hold for it.
        FinishIfStill(shown);
    }

    private void FinishIfStill(int shown)
    {
        if (_disposed || shown != _shown || Showing is null)
        {
            return;
        }

        Showing = null;
        Finished?.Invoke();
    }

    private SessionBus? Bus()
    {
        try
        {
            return _bus();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
