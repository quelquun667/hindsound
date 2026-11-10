using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Hindsound.Updates;

namespace Hindsound.Linux.Updates;

/// <summary>
/// The GET of the update check, on HttpClient. The same rules as the Windows transport: its own redirects are turned off, so
/// every hop faces the allowed hosts in <see cref="TransferLoops.FollowRedirects"/>; the body goes through
/// <see cref="TransferLoops.PumpBody"/>, with its total delay, its longest silence and its ceiling; no cookie, no
/// credential, a fixed User-Agent, nothing else sent. A client is made for each GET and gone after it: between two checks,
/// nothing of the transport is alive.
/// </summary>
internal sealed class LinuxHttpTransport(Action<string> log) : IUpdateTransport
{
    private const string UserAgent = "Hindsound";

    public TransportResult Get(string url, TransferLimits limits, BodySink sink, Func<bool> shouldStop) =>
        Run(url, limits, sink, shouldStop, TransferLoops.FollowRedirects);

    /// <summary>For the tests on a local server: the delays and the client, around a different redirect loop.</summary>
    internal TransportResult Run(
        string url,
        TransferLimits limits,
        BodySink sink,
        Func<bool> shouldStop,
        Func<string, Func<Uri, HopOutcome>, Func<bool>, TransportResult> follow)
    {
        var watch = Stopwatch.StartNew();
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,

            // The token of each request holds the delay, connection included.
            ConnectTimeout = Timeout.InfiniteTimeSpan,
        };
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        return follow(url, uri => OneHop(client, uri, limits, sink, shouldStop, watch), shouldStop);
    }

    private HopOutcome OneHop(HttpClient client, Uri uri, TransferLimits limits, BodySink sink, Func<bool> shouldStop, Stopwatch watch)
    {
        var patience = Patience(limits, watch.Elapsed);
        if (patience <= TimeSpan.Zero)
        {
            return new HopOutcome(new TransportResult(TransportStatus.TimedOut));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        HttpResponseMessage response;
        using (var answerDue = new CancellationTokenSource(patience))
        {
            try
            {
                // Up to the headers only: the body is read below, piece by piece, under the limits.
                response = client.Send(request, HttpCompletionOption.ResponseHeadersRead, answerDue.Token);
            }
            catch (OperationCanceledException)
            {
                return new HopOutcome(new TransportResult(TransportStatus.TimedOut));
            }
            catch (HttpRequestException ex)
            {
                return new HopOutcome(Failed(ex));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
            {
                log($"update network: {ex.GetType().Name}");
                return new HopOutcome(new TransportResult(TransportStatus.Failed));
            }
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (TransferLoops.IsRedirect(status))
            {
                // The raw text, as the server wrote it: FollowRedirects resolves it and holds it against the policy. A
                // redirect without an address cannot be followed: the empty text is blocked there.
                return new HopOutcome(default, RawHeader(response, "Location") ?? string.Empty);
            }

            if (status != 200)
            {
                return new HopOutcome(new TransportResult(TransportStatus.Failed, status));
            }

            var announced = response.Content.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture);
            if (TransferLoops.AnnouncedTooLarge(announced, limits.MaxBytes))
            {
                return new HopOutcome(new TransportResult(TransportStatus.TooLarge));
            }

            Stream body;
            try
            {
                body = response.Content.ReadAsStream();
            }
            catch (HttpRequestException ex)
            {
                return new HopOutcome(Failed(ex));
            }
            catch (IOException)
            {
                return new HopOutcome(new TransportResult(TransportStatus.Failed));
            }

            using (body)
            {
                return new HopOutcome(TransferLoops.PumpBody(
                    (buffer, wait) => ReadSome(body, buffer, wait),
                    limits,
                    sink,
                    shouldStop,
                    () => watch.Elapsed));
            }
        }
    }

    private static TimeSpan Patience(TransferLimits limits, TimeSpan elapsed)
    {
        var remaining = limits.Total - elapsed;
        return remaining < limits.Stall ? remaining : limits.Stall;
    }

    // One read, given no more than the patience: a server that stops talking ends the read, however the stream waits.
    private static BodyRead ReadSome(Stream body, byte[] buffer, TimeSpan patience)
    {
        using var due = new CancellationTokenSource(patience);
        try
        {
            // On the update pass's own thread, which has nothing else to do: waiting here holds nobody.
            return new BodyRead(body.ReadAsync(buffer.AsMemory(), due.Token).AsTask().GetAwaiter().GetResult());
        }
        catch (OperationCanceledException)
        {
            return new BodyRead(0, new TransportResult(TransportStatus.TimedOut));
        }
        catch (HttpRequestException ex)
        {
            return new BodyRead(0, Failed(ex));
        }
        catch (IOException)
        {
            return new BodyRead(0, new TransportResult(TransportStatus.Failed));
        }
    }

    // The kind of error, for the journal: a name lookup, a refused connection, a TLS failure.
    private static TransportResult Failed(HttpRequestException ex) => new(TransportStatus.Failed, (int)ex.HttpRequestError);

    private static string? RawHeader(HttpResponseMessage response, string name) =>
        response.Headers.NonValidated.TryGetValues(name, out var values) ? values.ToString() : null;
}
