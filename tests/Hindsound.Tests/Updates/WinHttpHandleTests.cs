using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>
/// The app makes one check a day for weeks: each GET has to give back what it took. The first one costs for good (WinHTTP's
/// threads, the TLS providers, the proxy settings), the next ones nothing. Against a local TLS server, so no test leaves the PC:
/// its certificate is refused, after the whole handshake, which is all a GET opens before an answer.
/// </summary>
[Collection("Counts handles")]
public class WinHttpHandleTests
{
    private const int Warmup = 5;
    private const int Gets = 100;

    // Handles of the pool's threads come and go between two counts: some room, far under one handle a GET.
    private const int Room = 25;

    [Fact]
    public void A_hundred_GETs_after_the_first_ones_leave_the_handle_count_where_it_was()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var made = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        // SChannel takes no key held only in memory: through a PFX, whose key file goes when the certificate is disposed.
        using var certificate = X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx), null);
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var address = new Uri($"https://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/hindsound.json");
        using var stop = new CancellationTokenSource();
        var serving = Task.Run(() => Serve(server, certificate, stop.Token));
        var results = new List<TransportResult>();

        for (var i = 0; i < Warmup; i++)
        {
            results.Add(OneGet(address));
        }

        var before = Handles();
        for (var i = 0; i < Gets; i++)
        {
            results.Add(OneGet(address));
        }

        var after = Handles();
        stop.Cancel();
        server.Stop();
        serving.Wait(TimeSpan.FromSeconds(5));

        // 12175: the refused certificate, so every GET went through the whole handshake.
        Assert.All(results, r => Assert.Equal(new TransportResult(TransportStatus.Failed, 12175), r));
        Assert.True(after - before <= Room, $"handles {before} after {Warmup} GETs, then {after} after {Gets} more");
    }

    /// <summary>
    /// A GET the watchdog ends closes the request itself, and the connection after it: 30 GETs on a server that accepts and says
    /// nothing, each ended at 0.2 s by the watchdog, leave the handle count where it was. Without the close of the connection
    /// after a firing, the same run left 679 handles for 498 before.
    /// </summary>
    [Fact]
    public void Thirty_GETs_the_watchdog_ended_leave_the_handle_count_where_it_was()
    {
        const int silentWarmup = 5;
        const int silentGets = 30;
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var address = new Uri($"https://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/hindsound.json");
        using var stop = new CancellationTokenSource();
        var serving = Task.Run(async () =>
        {
            // The server lives in this process: the end it holds for one GET is a handle of the count, so each one is let go
            // when the next one comes (the GET ended at 0.2 s and closed its side long before).
            TcpClient? held = null;
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var client = await server.AcceptTcpClientAsync(stop.Token);
                    held?.Dispose();
                    held = client;
                }
            }
            catch (Exception)
            {
                // The server is stopped: the end of the test.
            }
            finally
            {
                held?.Dispose();
            }
        });
        var results = new List<TransportResult>();

        for (var i = 0; i < silentWarmup; i++)
        {
            results.Add(OneSilentGet(address));
        }

        var before = Handles();
        for (var i = 0; i < silentGets; i++)
        {
            results.Add(OneSilentGet(address));
        }

        var after = Handles();
        stop.Cancel();
        server.Stop();
        serving.Wait(TimeSpan.FromSeconds(5));

        Assert.All(results, r => Assert.Equal(new TransportResult(TransportStatus.TimedOut), r));
        Assert.True(after - before <= Room, $"handles {before} after {silentWarmup} GETs, then {after} after {silentGets} more");
    }

    private static TransportResult OneSilentGet(Uri address)
    {
        var limits = new TransferLimits(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(0.2), 1024);
        return new WinHttpTransport().Run(
            address.AbsoluteUri,
            limits,
            _ => { },
            () => false,
            (_, request, _) => request(address).Result);
    }

    private static TransportResult OneGet(Uri address)
    {
        var limits = new TransferLimits(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), 1024);
        return new WinHttpTransport().Run(
            address.AbsoluteUri,
            limits,
            _ => { },
            () => false,
            (_, request, _) => request(address).Result);
    }

    private static int Handles()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var self = Process.GetCurrentProcess();
        return self.HandleCount;
    }

    private static async Task Serve(TcpListener server, X509Certificate2 certificate, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await server.AcceptTcpClientAsync(stop);
            }
            catch (Exception)
            {
                return;
            }

            using (client)
            using (var tls = new SslStream(client.GetStream()))
            {
                try
                {
                    await tls.AuthenticateAsServerAsync(certificate).WaitAsync(TimeSpan.FromSeconds(5), stop);
                }
                catch (Exception)
                {
                    // The client refuses the certificate: the end every GET here is meant to reach.
                }
            }
        }
    }
}
