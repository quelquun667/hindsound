using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Hindsound.Updates;

/// <summary>
/// A plain HTTPS GET on winhttp.dll, so the exe does not carry a managed HTTP stack. WinHTTP's own redirects are turned off:
/// only here can each hop be held against the allowed hosts. No cookie, no credential, a fixed User-Agent, nothing else sent.
/// </summary>
internal sealed partial class WinHttpTransport : IUpdateTransport
{
    private const string UserAgent = "Hindsound";

    private const uint AccessTypeAutomaticProxy = 4;
    private const uint FlagSecure = 0x00800000;

    private const uint OptionReceiveTimeout = 6;
    private const uint OptionReceiveResponseTimeout = 7;
    private const uint OptionConnectRetries = 4;
    private const uint OptionDisableFeature = 63;
    private const uint DisableCookies = 1;
    private const uint DisableRedirects = 2;
    private const uint DisableAuthentication = 4;

    private const uint QueryContentLength = 5;
    private const uint QueryStatusCode = 19;
    private const uint QueryLocation = 33;
    private const uint QueryFlagNumber = 0x20000000;

    private const int ErrorTimeout = 12002;

    // GitHub's asset addresses carry a long token: about 800 characters (06/11/2026).
    private const int HeaderBufferBytes = 16 * 1024;

    public TransportResult Get(string url, TransferLimits limits, BodySink sink, Func<bool> shouldStop) =>
        Run(url, limits, sink, shouldStop, TransferLoops.FollowRedirects);

    /// <summary>
    /// For the tests on a silent local server: the delays and handles, around a different redirect loop. Only the tests pass
    /// requestFlags, 0 for a local server in plain HTTP; <see cref="Get"/> always asks for TLS.
    /// </summary>
    internal TransportResult Run(
        string url,
        TransferLimits limits,
        BodySink sink,
        Func<bool> shouldStop,
        Func<string, Func<Uri, HopOutcome>, Func<bool>, TransportResult> follow,
        uint requestFlags = FlagSecure)
    {
        var watch = Stopwatch.StartNew();
        KeepWinHttpSetUp();
        var session = WinHttpOpen(UserAgent, AccessTypeAutomaticProxy, null, null, 0);
        if (session == 0)
        {
            return LastError();
        }

        try
        {
            return follow(url, uri => OneHop(session, uri, limits, sink, shouldStop, watch, requestFlags), shouldStop);
        }
        finally
        {
            WinHttpCloseHandle(session);
        }
    }

    // Each time a session opens after the last one was closed, WinHTTP sets itself up again and keeps one handle more for good:
    // 100 GETs, 100 handles more (07/11/2026), and a check a day for weeks would add up. One idle session, opened once and never
    // closed, keeps it set up. It is never used: each GET still has its own session, its own proxy answer and its own
    // connections, all closed when it ends. Failing to open it costs nothing but those handles.
    private static readonly Lock KeepLock = new();
    private static nint _kept;

    private static void KeepWinHttpSetUp()
    {
        lock (KeepLock)
        {
            if (_kept == 0)
            {
                _kept = WinHttpOpen(UserAgent, AccessTypeAutomaticProxy, null, null, 0);
            }
        }
    }

    private static HopOutcome OneHop(nint session, Uri uri, TransferLimits limits, BodySink sink, Func<bool> shouldStop, Stopwatch watch, uint requestFlags)
    {
        var patience = Patience(limits, watch.Elapsed);
        if (patience <= 0)
        {
            return new HopOutcome(new TransportResult(TransportStatus.TimedOut));
        }

        var connection = WinHttpConnect(session, uri.IdnHost, (ushort)uri.Port, 0);
        var request = connection == 0 ? 0 : WinHttpOpenRequest(connection, "GET", uri.PathAndQuery, null, null, 0, requestFlags);
        var watchdog = new Watchdog(() => WinHttpCloseHandle(request));
        try
        {
            if (request == 0 || !Configure(request, patience))
            {
                return new HopOutcome(LastError());
            }

            if (watchdog.Run(patience, () => WinHttpSendRequest(request, 0, 0, 0, 0, 0, 0)) is { } notSent)
            {
                return new HopOutcome(notSent);
            }

            // Connecting took part of the time: what is left, and no more, goes to the answer.
            var answerPatience = (uint)Math.Max(Patience(limits, watch.Elapsed), 1);
            if (!WinHttpSetOption(request, OptionReceiveTimeout, ref answerPatience, sizeof(uint))
                || !WinHttpSetOption(request, OptionReceiveResponseTimeout, ref answerPatience, sizeof(uint)))
            {
                return new HopOutcome(LastError());
            }

            if (watchdog.Run((int)answerPatience, () => WinHttpReceiveResponse(request, 0)) is { } noAnswer)
            {
                return new HopOutcome(noAnswer);
            }

            uint size = sizeof(int);
            if (!WinHttpQueryStatus(request, QueryStatusCode | QueryFlagNumber, 0, out var status, ref size, 0))
            {
                return new HopOutcome(LastError());
            }

            if (TransferLoops.IsRedirect(status))
            {
                // A redirect without an address cannot be followed: FollowRedirects blocks the empty text.
                return new HopOutcome(default, QueryText(request, QueryLocation) ?? string.Empty);
            }

            if (status != 200)
            {
                return new HopOutcome(new TransportResult(TransportStatus.Failed, status));
            }

            if (TransferLoops.AnnouncedTooLarge(QueryText(request, QueryContentLength), limits.MaxBytes))
            {
                return new HopOutcome(new TransportResult(TransportStatus.TooLarge));
            }

            return new HopOutcome(TransferLoops.PumpBody(
                (buffer, wait) => ReadSome(request, watchdog, buffer, wait),
                limits,
                sink,
                shouldStop,
                () => watch.Elapsed));
        }
        finally
        {
            // A request the watchdog closed is not closed twice; the connection goes after its request, as always.
            watchdog.Release();
            WinHttpCloseHandle(connection);
        }
    }

    /// <summary>
    /// Holds each blocking call of a request to its own patience. WinHTTP checks the waits of a request against its timeouts only
    /// on a coarse clock of about 3.9 s, and not only the TLS handshake held inside WinHttpSendRequest: WinHttpReceiveResponse and
    /// WinHttpQueryDataAvailable wait on the same clock. For the handshake, a receive timeout of 0.5, 1 or 2 s waited 3.8 to 4.0 s,
    /// 4 s waited 7.8 s and 8 s 11.8 s, the other timeouts changing nothing, on one connection and one ClientHello, with no retry
    /// and no fallback (22/11/2026); 6.0 s for 2 s on the CI. No option shortens it. Closing the request from another thread ends
    /// the call at once (2.005 to 2.017 s for 2 s), and that end is the patience running out. The close and the timer are
    /// injected, so the decision is tested without a network.
    /// </summary>
    internal sealed class Watchdog(Action close, Func<int, Action, IDisposable>? startTimer = null)
    {
        private readonly Lock _gate = new();
        private readonly Func<int, Action, IDisposable> _startTimer = startTimer ?? DefaultTimer;
        private int _generation;
        private int _armed;
        private bool _fired;
        private bool _closed;

        /// <summary>The watchdog closed the request: nothing may use or close it again.</summary>
        public bool Fired
        {
            get
            {
                lock (_gate)
                {
                    return _fired;
                }
            }
        }

        /// <summary>Null when the call succeeded in time; TimedOut when its patience ran out, whatever it returned; else its error.</summary>
        public TransportResult? Run(int patience, Func<bool> call)
        {
            int generation;
            lock (_gate)
            {
                if (_fired)
                {
                    return new TransportResult(TransportStatus.TimedOut);
                }

                generation = _armed = ++_generation;
            }

            // A timer of an earlier call that fires late finds another generation and leaves the request alone.
            using var timer = _startTimer(patience, () => Fire(generation));
            TransportResult? error;
            try
            {
                error = call() ? null : LastError();
            }
            finally
            {
                // Whatever the call did, even throw: a disarmed watchdog closes nothing.
                lock (_gate)
                {
                    _armed = 0;
                }
            }

            lock (_gate)
            {
                return _fired ? new TransportResult(TransportStatus.TimedOut) : error;
            }
        }

        /// <summary>Closes the request, once: not after the watchdog closed it, not twice.</summary>
        public void Release()
        {
            lock (_gate)
            {
                if (_closed)
                {
                    return;
                }

                _closed = true;
                close();
            }
        }

        private static IDisposable DefaultTimer(int patience, Action fire) =>
            new Timer(_ => fire(), null, patience, Timeout.Infinite);

        private void Fire(int generation)
        {
            lock (_gate)
            {
                if (_armed != generation || _fired)
                {
                    return;
                }

                _fired = true;
                _closed = true;
                close();
            }
        }
    }

    private static int Patience(TransferLimits limits, TimeSpan elapsed)
    {
        var remaining = limits.Total - elapsed;
        return (int)Math.Ceiling((remaining < limits.Stall ? remaining : limits.Stall).TotalMilliseconds);
    }

    private static bool Configure(nint request, int patience)
    {
        var features = DisableCookies | DisableRedirects | DisableAuthentication;
        var retries = 1u;
        var receiveResponse = (uint)patience;
        return WinHttpSetTimeouts(request, patience, patience, patience, patience)
            && WinHttpSetOption(request, OptionReceiveResponseTimeout, ref receiveResponse, sizeof(uint))
            && WinHttpSetOption(request, OptionConnectRetries, ref retries, sizeof(uint))
            && WinHttpSetOption(request, OptionDisableFeature, ref features, sizeof(uint));
    }

    // Asking how much is there, then reading exactly that, means no read waits to fill the buffer: a server dripping one
    // byte at a time cannot hold a single call past the deadline.
    private static BodyRead ReadSome(nint request, Watchdog watchdog, byte[] buffer, TimeSpan patience)
    {
        var wait = (uint)Math.Ceiling(patience.TotalMilliseconds);
        if (!WinHttpSetOption(request, OptionReceiveTimeout, ref wait, sizeof(uint)))
        {
            return new BodyRead(0, LastError());
        }

        uint available = 0;
        if (watchdog.Run((int)wait, () => WinHttpQueryDataAvailable(request, out available)) is { } notRead)
        {
            return new BodyRead(0, notRead);
        }

        if (available == 0)
        {
            return new BodyRead(0);
        }

        var wanted = Math.Min(available, (uint)buffer.Length);
        return WinHttpReadData(request, buffer, wanted, out var read) ? new BodyRead((int)read) : new BodyRead(0, LastError());
    }

    private static string? QueryText(nint request, uint info)
    {
        var buffer = new byte[HeaderBufferBytes];
        var length = (uint)buffer.Length;
        return WinHttpQueryBuffer(request, info, 0, buffer, ref length, 0)
            ? Encoding.Unicode.GetString(buffer, 0, (int)length)
            : null;
    }

    private static TransportResult LastError()
    {
        var error = Marshal.GetLastPInvokeError();
        return error == ErrorTimeout
            ? new TransportResult(TransportStatus.TimedOut)
            : new TransportResult(TransportStatus.Failed, error);
    }

    [LibraryImport("winhttp", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint WinHttpOpen(string agent, uint accessType, string? proxy, string? bypass, uint flags);

    [LibraryImport("winhttp", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint WinHttpConnect(nint session, string server, ushort port, uint reserved);

    [LibraryImport("winhttp", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint WinHttpOpenRequest(nint connection, string verb, string path, string? version, string? referrer, nint acceptTypes, uint flags);

    [LibraryImport("winhttp", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpSetTimeouts(nint handle, int resolve, int connect, int send, int receive);

    [LibraryImport("winhttp", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpSetOption(nint handle, uint option, ref uint value, uint length);

    [LibraryImport("winhttp", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpSendRequest(nint request, nint headers, uint headersLength, nint optional, uint optionalLength, uint totalLength, nint context);

    [LibraryImport("winhttp", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpReceiveResponse(nint request, nint reserved);

    [LibraryImport("winhttp", EntryPoint = "WinHttpQueryHeaders", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpQueryStatus(nint request, uint info, nint name, out int value, ref uint length, nint index);

    [LibraryImport("winhttp", EntryPoint = "WinHttpQueryHeaders", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpQueryBuffer(nint request, uint info, nint name, [Out] byte[] buffer, ref uint length, nint index);

    [LibraryImport("winhttp", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpQueryDataAvailable(nint request, out uint available);

    [LibraryImport("winhttp", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpReadData(nint request, [Out] byte[] buffer, uint toRead, out uint read);

    [LibraryImport("winhttp")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpCloseHandle(nint handle);
}
