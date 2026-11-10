using System.Net;
using System.Net.Sockets;
using System.Text;
using Hindsound.Linux.Updates;
using Hindsound.Updates;

namespace Hindsound.Linux.Tests.Updates;

/// <summary>
/// The update check sends no cookie: one handed out by the first hop of a redirect does not come back on the next hop.
/// On a local server, so it runs here too.
/// </summary>
public class LinuxHttpTransportCookieTests
{
    private static readonly TransferLimits Roomy = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), 1024);

    [Fact]
    public void A_cookie_set_by_a_redirect_is_not_sent_on_the_next_hop()
    {
        using var server = new TwoAnswers(
            "HTTP/1.1 302 Found\r\nSet-Cookie: session=abc; Path=/\r\nLocation: /second\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        var transport = new LinuxHttpTransport(_ => { });

        var result = transport.Run(server.Address.AbsoluteUri, Roomy, _ => { }, () => false, (url, request, _) =>
        {
            var first = request(new Uri(url));
            Assert.Equal("/second", first.RedirectTo);
            return request(new Uri(server.Address, first.RedirectTo)).Result;
        });

        Assert.Equal(TransportStatus.Completed, result.Status);
        var second = server.Requests[1];
        Assert.StartsWith("GET /second HTTP/1.1", second);
        Assert.DoesNotContain("Cookie", second, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A loopback server for two requests, one connection each, in order.</summary>
    private sealed class TwoAnswers : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serving;
        private readonly CancellationTokenSource _done = new();
        private readonly string[] _requests = [string.Empty, string.Empty];

        public TwoAnswers(string first, string second)
        {
            _listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _serving = Task.Run(async () =>
            {
                string[] answers = [first, second];
                for (var i = 0; i < answers.Length; i++)
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

                    _requests[i] = text.ToString();
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(answers[i]), _done.Token);
                }
            });
        }

        public Uri Address { get; }

        public string[] Requests
        {
            get
            {
                _serving.Wait(TimeSpan.FromSeconds(5));
                return _requests;
            }
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
