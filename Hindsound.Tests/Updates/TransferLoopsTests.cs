using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class TransferLoopsTests
{
    private static readonly TransferLimits Roomy = new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), 10 * 1024 * 1024);

    /// <summary>Hands out the given sizes one read at a time, then the end.</summary>
    private static BodyReader Chunks(params int[] sizes)
    {
        var next = 0;
        return (buffer, _) => new BodyRead(next < sizes.Length ? sizes[next++] : 0);
    }

    private static TimeSpan NoTime() => TimeSpan.Zero;

    // ---- The body

    [Fact]
    public void TheWholeBodyReachesTheSink()
    {
        long received = 0;

        var result = TransferLoops.PumpBody(Chunks(1000, 2000, 3000), Roomy, data => received += data.Length, () => false, NoTime);

        Assert.Equal(TransportStatus.Completed, result.Status);
        Assert.Equal(6000, received);
    }

    [Fact]
    public void TheStopDelegateIsTestedEverySixtyFourKilobytes()
    {
        var tests = 0;
        var chunks = Enumerable.Repeat(16 * 1024, 16).ToArray(); // 256 KB

        TransferLoops.PumpBody(Chunks(chunks), Roomy, _ => { }, () => { tests++; return false; }, NoTime);

        // Once before the first read, then once per 64 KB.
        Assert.Equal(5, tests);
    }

    [Fact]
    public void AGameGoingFullScreenStopsTheTransferWithinSixtyFourKilobytes()
    {
        long received = 0;
        var fullScreen = false;
        var chunks = Enumerable.Repeat(16 * 1024, 100).ToArray();

        var result = TransferLoops.PumpBody(
            Chunks(chunks),
            Roomy,
            data =>
            {
                received += data.Length;
                fullScreen = received >= 16 * 1024;
            },
            () => fullScreen,
            NoTime);

        Assert.Equal(TransportStatus.Stopped, result.Status);
        Assert.Equal(64 * 1024, received);
    }

    [Fact]
    public void AlreadyInAGameNothingIsRead()
    {
        var reads = 0;

        var result = TransferLoops.PumpBody(
            (_, _) => { reads++; return new BodyRead(0); },
            Roomy,
            _ => { },
            () => true,
            NoTime);

        Assert.Equal(TransportStatus.Stopped, result.Status);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void ABodyOverTheCeilingIsCutBeforeTheBytesReachTheSink()
    {
        long received = 0;
        var limits = Roomy with { MaxBytes = 2500 };

        var result = TransferLoops.PumpBody(Chunks(1000, 1000, 1000), limits, data => received += data.Length, () => false, NoTime);

        Assert.Equal(TransportStatus.TooLarge, result.Status);
        Assert.Equal(2000, received);
    }

    [Fact]
    public void ABodyAtTheCeilingIsAllowed()
    {
        var result = TransferLoops.PumpBody(Chunks(1000, 1000), Roomy with { MaxBytes = 2000 }, _ => { }, () => false, NoTime);

        Assert.Equal(TransportStatus.Completed, result.Status);
    }

    [Fact]
    public void TheTotalDelayEndsADrippingServer()
    {
        var clock = TimeSpan.Zero;
        var reads = 0;

        var result = TransferLoops.PumpBody(
            (_, _) =>
            {
                reads++;
                clock += TimeSpan.FromSeconds(20);
                return new BodyRead(1);
            },
            Roomy,
            _ => { },
            () => false,
            () => clock);

        Assert.Equal(TransportStatus.TimedOut, result.Status);
        Assert.Equal(15, reads); // 15 reads of 20 s: 5 minutes.
    }

    [Fact]
    public void EachReadWaitsNoLongerThanTheSilenceAllowed()
    {
        var waits = new List<TimeSpan>();
        var left = 2;

        TransferLoops.PumpBody(
            (_, patience) =>
            {
                waits.Add(patience);
                return new BodyRead(left-- > 0 ? 1 : 0);
            },
            Roomy,
            _ => { },
            () => false,
            NoTime);

        Assert.All(waits, wait => Assert.Equal(TimeSpan.FromSeconds(30), wait));
    }

    [Fact]
    public void TheLastReadWaitsNoLongerThanWhatIsLeft()
    {
        var waits = new List<TimeSpan>();

        TransferLoops.PumpBody(
            (_, patience) =>
            {
                waits.Add(patience);
                return new BodyRead(0);
            },
            Roomy,
            _ => { },
            () => false,
            () => TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(4));

        Assert.Equal(TimeSpan.FromSeconds(4), Assert.Single(waits));
    }

    [Fact]
    public void ASilentServerEndsAsTheReaderSays()
    {
        var result = TransferLoops.PumpBody(
            (_, _) => new BodyRead(0, new TransportResult(TransportStatus.TimedOut)),
            Roomy,
            _ => { },
            () => false,
            NoTime);

        Assert.Equal(TransportStatus.TimedOut, result.Status);
    }

    [Theory]
    [InlineData("100", 99, true)]
    [InlineData("100", 100, false)]
    [InlineData("99", 100, false)]
    [InlineData("99999999999999", 100, true)]
    [InlineData(null, 100, false)]
    [InlineData("", 100, false)]
    [InlineData("garbage", 100, false)]
    [InlineData("-5", 100, false)]
    public void AnAnnouncedLengthOverTheCeilingIsRefused(string? length, long max, bool expected)
    {
        Assert.Equal(expected, TransferLoops.AnnouncedTooLarge(length, max));
    }

    // ---- The redirects

    private const string Start = "https://github.com/quelquun667/hindsound/releases/latest/download/hindsound-update-win-x64.json";

    private static HopOutcome Done() => new(new TransportResult(TransportStatus.Completed));

    [Fact]
    public void GitHubsRedirectsAreFollowed()
    {
        var asked = new List<string>();
        const string Second = "https://github.com/quelquun667/hindsound/releases/download/v1.0.0/hindsound-update-win-x64.json";
        const string Third = "https://release-assets.githubusercontent.com/github-production-release-asset/1/2?sp=r&sig=a%2Fb";

        var result = TransferLoops.FollowRedirects(
            Start,
            uri =>
            {
                asked.Add(uri.AbsoluteUri);
                return asked.Count switch
                {
                    1 => new HopOutcome(default, Second),
                    2 => new HopOutcome(default, Third),
                    _ => Done(),
                };
            },
            () => false);

        Assert.Equal(TransportStatus.Completed, result.Status);
        Assert.Equal([Start, Second, Third], asked);
    }

    [Fact]
    public void ARelativeRedirectStaysOnTheSameHost()
    {
        var asked = new List<string>();

        TransferLoops.FollowRedirects(
            Start,
            uri =>
            {
                asked.Add(uri.AbsoluteUri);
                return asked.Count == 1 ? new HopOutcome(default, "/quelquun667/hindsound/x") : Done();
            },
            () => false);

        Assert.Equal("https://github.com/quelquun667/hindsound/x", asked[1]);
    }

    [Theory]
    [InlineData("https://evil.com/a")]
    [InlineData("http://github.com/a")]
    [InlineData("http://objects.githubusercontent.com/a")]
    [InlineData("//evil.com/a")]
    [InlineData("https://github.com@evil.com/a")]
    [InlineData("https://evil.github.com/a")]
    [InlineData("https://github.com:8443/a")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("")]
    public void ARedirectOutOfGitHubIsNeverRequested(string location)
    {
        var requests = 0;

        var result = TransferLoops.FollowRedirects(
            Start,
            _ =>
            {
                requests++;
                return new HopOutcome(default, location);
            },
            () => false);

        Assert.Equal(TransportStatus.Blocked, result.Status);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void AnAddressOutOfGitHubIsNeverRequestedInTheFirstPlace()
    {
        var requests = 0;

        var result = TransferLoops.FollowRedirects("https://evil.com/a", _ => { requests++; return Done(); }, () => false);

        Assert.Equal(TransportStatus.Blocked, result.Status);
        Assert.Equal(0, requests);
    }

    [Fact]
    public void AnEndlessRedirectEnds()
    {
        var requests = 0;

        var result = TransferLoops.FollowRedirects(
            Start,
            _ =>
            {
                requests++;
                return new HopOutcome(default, Start);
            },
            () => false);

        Assert.Equal(TransportStatus.Blocked, result.Status);
        Assert.Equal(TransferLoops.MaxRedirects + 1, requests);
    }

    [Fact]
    public void AStopIsHeardBetweenTwoHops()
    {
        var requests = 0;

        var result = TransferLoops.FollowRedirects(
            Start,
            _ =>
            {
                requests++;
                return new HopOutcome(default, Start);
            },
            () => requests >= 1);

        Assert.Equal(TransportStatus.Stopped, result.Status);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(301, true)]
    [InlineData(302, true)]
    [InlineData(303, true)]
    [InlineData(307, true)]
    [InlineData(308, true)]
    [InlineData(200, false)]
    [InlineData(304, false)]
    [InlineData(404, false)]
    public void TheRedirectStatusesAreKnown(int status, bool expected)
    {
        Assert.Equal(expected, TransferLoops.IsRedirect(status));
    }
}
