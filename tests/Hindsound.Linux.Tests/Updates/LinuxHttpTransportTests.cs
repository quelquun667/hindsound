using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hindsound.Linux.Updates;
using Hindsound.Updates;

namespace Hindsound.Linux.Tests.Updates;

/// <summary>
/// The HTTP of the Linux update check. HttpClient is the same on every system, so these run here too: on a local server,
/// through <see cref="LinuxHttpTransport.Run"/> with a redirect loop that lets the loopback address through.
/// </summary>
public class LinuxHttpTransportTests
{
    private const string GitHubUrl = "https://github.com/quelquun667/hindsound/releases/latest/download/hindsound-update-linux-x64.json";
    private static readonly TransferLimits Roomy = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), 1024);

    [Theory]
    [InlineData("http://github.com/a")]
    [InlineData("https://example.org/a")]
    [InlineData("https://127.0.0.1/a")]
    [InlineData("https://github.com:8443/a")]
    public void An_address_out_of_GitHub_makes_no_connection(string url)
    {
        var received = 0;

        var result = Transport().Get(url, Roomy, data => received += data.Length, () => false);

        Assert.Equal(TransportStatus.Blocked, result.Status);
        Assert.Equal(0, received);
    }

    [Fact]
    public void A_stop_before_the_first_request_makes_no_connection()
    {
        Assert.Equal(TransportStatus.Stopped, Transport().Get(GitHubUrl, Roomy, _ => { }, () => true).Status);
    }

    [Fact]
    public void An_exhausted_delay_makes_no_connection()
    {
        var limits = new TransferLimits(TimeSpan.Zero, TimeSpan.FromSeconds(1), 1024);

        Assert.Equal(TransportStatus.TimedOut, Transport().Get(GitHubUrl, limits, _ => { }, () => false).Status);
    }

    [Fact]
    public void A_body_arrives_whole()
    {
        using var server = new OneAnswer("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello");
        using var body = new MemoryStream();

        var result = server.Get(Roomy, data => body.Write(data));

        Assert.Equal(TransportStatus.Completed, result.Status);
        Assert.Equal("hello", Encoding.ASCII.GetString(body.ToArray()));
        Assert.StartsWith("GET / HTTP/1.1", server.Request);
        Assert.Contains("User-Agent: Hindsound", server.Request);
        Assert.DoesNotContain("Cookie", server.Request);
        Assert.DoesNotContain("Authorization", server.Request);
    }

    [Fact]
    public void A_redirect_is_handed_back_raw_and_not_followed()
    {
        using var server = new OneAnswer("HTTP/1.1 302 Found\r\nLocation: https://objects.githubusercontent.com/x?token=a%2Fb\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        var hop = server.Hop(Roomy, _ => { });

        Assert.Equal("https://objects.githubusercontent.com/x?token=a%2Fb", hop.RedirectTo);
    }

    [Fact]
    public void A_redirect_without_an_address_gives_the_empty_text()
    {
        using var server = new OneAnswer("HTTP/1.1 301 Moved\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        Assert.Equal(string.Empty, server.Hop(Roomy, _ => { }).RedirectTo);
    }

    [Fact]
    public void An_error_status_fails_with_its_number()
    {
        using var server = new OneAnswer("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        Assert.Equal(new TransportResult(TransportStatus.Failed, 404), server.Get(Roomy, _ => { }));
    }

    [Fact]
    public void A_body_announced_too_large_is_refused_before_a_byte()
    {
        using var server = new OneAnswer("HTTP/1.1 200 OK\r\nContent-Length: 2048\r\nConnection: close\r\n\r\n" + new string('x', 2048));
        var received = 0;

        var result = server.Get(Roomy, data => received += data.Length);

        Assert.Equal(TransportStatus.TooLarge, result.Status);
        Assert.Equal(0, received);
    }

    [Fact]
    public void A_body_larger_than_the_ceiling_without_a_length_is_cut()
    {
        using var server = new OneAnswer("HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n" + new string('x', 4096));

        Assert.Equal(TransportStatus.TooLarge, server.Get(Roomy, _ => { }).Status);
    }

    /// <summary>Accepts the connection and never says a word.</summary>
    [Fact]
    public void A_silent_server_ends_within_the_delay()
    {
        using var server = new OneAnswer(null);
        var limits = new TransferLimits(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), 1024);
        var watch = Stopwatch.StartNew();

        var result = server.Get(limits, _ => { });

        Assert.Equal(TransportStatus.TimedOut, result.Status);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(6));
    }

    /// <summary>Sends the headers, then stops talking in the middle of the body.</summary>
    [Fact]
    public void A_body_that_stops_coming_ends_within_the_delay()
    {
        using var server = new OneAnswer("HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\nabc");
        var limits = new TransferLimits(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), 1024);
        var watch = Stopwatch.StartNew();

        var result = server.Get(limits, _ => { });

        Assert.Equal(TransportStatus.TimedOut, result.Status);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void An_exception_from_the_sink_goes_through()
    {
        using var server = new OneAnswer("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello");

        Assert.Throws<InvalidDataException>(() => server.Get(Roomy, _ => throw new InvalidDataException()));
    }

    private static LinuxHttpTransport Transport() => new(_ => { });

    /// <summary>A loopback server for one request: it writes the given answer and keeps the connection open, or says nothing.</summary>
    private sealed class OneAnswer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serving;
        private readonly CancellationTokenSource _done = new();
        private string _request = string.Empty;

        public OneAnswer(string? answer)
        {
            _listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _serving = Task.Run(async () =>
            {
                using var client = await _listener.AcceptTcpClientAsync(_done.Token);
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var text = new StringBuilder();
                while (!text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, _done.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    text.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                _request = text.ToString();
                if (answer is not null)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(answer), _done.Token);
                }

                // Held open until the test ends: an answer without a length ends where the connection closes.
                if (!answer?.Contains("Connection: close", StringComparison.Ordinal) ?? true)
                {
                    await Task.Delay(Timeout.Infinite, _done.Token);
                }
            });
        }

        public Uri Address { get; }

        public string Request
        {
            get
            {
                _serving.Wait(TimeSpan.FromSeconds(5));
                return _request;
            }
        }

        public TransportResult Get(TransferLimits limits, BodySink sink) =>
            Transport().Run(Address.AbsoluteUri, limits, sink, () => false, (url, request, _) => request(new Uri(url)).Result);

        public HopOutcome Hop(TransferLimits limits, BodySink sink)
        {
            HopOutcome outcome = default;
            Transport().Run(Address.AbsoluteUri, limits, sink, () => false, (url, request, _) =>
            {
                outcome = request(new Uri(url));
                return outcome.Result;
            });
            return outcome;
        }

        public void Dispose()
        {
            _done.Cancel();
            _listener.Stop();
            try
            {
                _serving.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Cancelled at the end of the test, as meant.
            }

            _done.Dispose();
        }
    }
}
