using System.Net.Sockets;
using Hindsound.App;

namespace Hindsound.Linux.App;

/// <summary>
/// The single instance of Linux, named from <see cref="SingleInstance.NameFor(AppOptions)"/>. The running instance holds a
/// lock file and listens on a Unix socket next to it; a second <c>hindsound</c> tells it <see cref="InstanceMessage.Show"/>,
/// and <c>hindsound --save</c> tells it <see cref="InstanceMessage.Save"/>. The lock, not the socket, decides who runs: the
/// system lets it go when a process dies, where a socket file stays behind.
/// </summary>
internal sealed class LinuxInstanceChannel : IInstanceChannel
{
    private const int ExchangeTimeoutMs = 2000;
    private const int Backlog = 8;

    private readonly Action<string> _log;
    private readonly InstancePlace? _place;
    private readonly FileStream? _lock;
    private readonly Socket? _listener;
    private readonly CancellationTokenSource _stop = new();
    private int _listening;
    private int _disposed;

    private LinuxInstanceChannel(Action<string> log, InstancePlace? place, bool first, FileStream? held, Socket? listener)
    {
        _log = log;
        _place = place;
        First = first;
        _lock = held;
        _listener = listener;
    }

    public bool First { get; }

    /// <summary>Holds the name for <paramref name="options"/> for as long as the channel lives.</summary>
    public static LinuxInstanceChannel Take(AppOptions options, Action<string> log)
    {
        var home = Hindsound.Settings.UserFolders.Find(Environment.SpecialFolder.UserProfile);
        if (InstancePlace.For(Environment.GetEnvironmentVariable, home, SingleInstance.NameFor(options)) is not { } place)
        {
            log("single instance: no folder fits a socket, so every launch counts as the first");
            return new LinuxInstanceChannel(log, null, first: true, held: null, listener: null);
        }

        return Take(place, log);
    }

    /// <summary>Takes <paramref name="place"/>: the first holder of its lock is the one instance.</summary>
    internal static LinuxInstanceChannel Take(InstancePlace place, Action<string> log)
    {
        FileStream held;
        try
        {
            if (place.OwnFolder)
            {
                MakeOwnFolder(place.Folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Alone(place, log, ex);
        }

        try
        {
            // FileShare.None takes an exclusive lock on the file, and a second process fails to open it.
            held = new FileStream(place.Lock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or UnauthorizedAccessException)
        {
            return Alone(place, log, ex);
        }
        catch (IOException)
        {
            return new LinuxInstanceChannel(log, place, first: false, held: null, listener: null);
        }

        return new LinuxInstanceChannel(log, place, first: true, held, Bind(place, log));
    }

    public bool Tell(InstanceMessage message)
    {
        if (First || _place is null)
        {
            return false;
        }

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.SendTimeout = ExchangeTimeoutMs;
            socket.ReceiveTimeout = ExchangeTimeoutMs;
            socket.Connect(new UnixDomainSocketEndPoint(_place.Socket));
            socket.Send(InstanceWire.Encode(message));
            var answer = new byte[InstanceWire.Ack.Length];
            var read = 0;
            while (read < answer.Length && socket.Receive(answer, read, answer.Length - read, SocketFlags.None) is > 0 and var got)
            {
                read += got;
            }

            return answer.AsSpan(0, read).SequenceEqual(InstanceWire.Ack);
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            _log($"single instance: the running one did not answer ({(ex as SocketException)?.SocketErrorCode.ToString() ?? ex.GetType().Name})");
            return false;
        }
    }

    public void Listen(IAppLoop loop, Action<InstanceMessage> received)
    {
        if (_listener is null || Interlocked.Exchange(ref _listening, 1) == 1)
        {
            return;
        }

        var token = _stop.Token;
        _ = Task.Run(() => AcceptAsync(_listener, loop, received, token));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _stop.Cancel();
        _listener?.Dispose();

        // Only the holder of the lock removes the socket: it is its own. The lock file stays, empty, for the next launch.
        if (_lock is not null && _place is not null)
        {
            TryDelete(_place.Socket);
        }

        _lock?.Dispose();
        _stop.Dispose();
    }

    // Nothing can tell a second launch apart: better two instances than none.
    private static LinuxInstanceChannel Alone(InstancePlace place, Action<string> log, Exception ex)
    {
        log($"single instance: the lock could not be made ({ex.GetType().Name}), so this launch counts as the first");
        return new LinuxInstanceChannel(log, place, first: true, held: null, listener: null);
    }

    private static Socket? Bind(InstancePlace place, Action<string> log)
    {
        // Left by an instance that died: the lock is ours now, so nobody listens on it.
        TryDelete(place.Socket);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(place.Socket));
            listener.Listen(Backlog);
            return listener;
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            listener.Dispose();
            log($"single instance: no socket ({(ex as SocketException)?.SocketErrorCode.ToString() ?? ex.GetType().Name}), so a second launch cannot reach this one");
            return null;
        }
    }

    private async Task AcceptAsync(Socket listener, IAppLoop loop, Action<InstanceMessage> received, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            using (client)
            {
                if (await ReadAsync(client, token).ConfigureAwait(false) is { } message)
                {
                    loop.Post(() => received(message));
                }
            }
        }
    }

    // One line at most, within the timeout: a launch that says nothing must not hold the next one back.
    private async Task<InstanceMessage?> ReadAsync(Socket client, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(ExchangeTimeoutMs);
        var buffer = new byte[InstanceWire.MaxLength];
        var read = 0;
        try
        {
            while (read < buffer.Length && !InstanceWire.Complete(buffer.AsSpan(0, read)))
            {
                var got = await client.ReceiveAsync(buffer.AsMemory(read), SocketFlags.None, timeout.Token).ConfigureAwait(false);
                if (got == 0)
                {
                    break;
                }

                read += got;
            }

            if (InstanceWire.Decode(buffer.AsSpan(0, read)) is not { } message)
            {
                _log("single instance: a launch sent something unknown");
                return null;
            }

            await client.SendAsync(InstanceWire.Ack, SocketFlags.None, timeout.Token).ConfigureAwait(false);
            return message;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // The line came whole, and only the answer was lost: the launch still asked.
            return InstanceWire.Decode(buffer.AsSpan(0, read));
        }
    }

    private static void MakeOwnFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }

        // The user alone: anyone who can reach the socket can make the app save a clip.
        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bind says what this costs.
        }
    }
}
