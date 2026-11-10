namespace Hindsound.Updates;

/// <summary>One read: some bytes, or the end (zero bytes), or an error status.</summary>
internal readonly record struct BodyRead(int Bytes, TransportResult Error = default)
{
    public bool Failed => Error.Status != TransportStatus.Completed;
}

/// <summary>Reads up to the buffer's size, waiting no longer than the given patience.</summary>
internal delegate BodyRead BodyReader(byte[] buffer, TimeSpan patience);

/// <summary>What one request gave: an end, or the address it redirects to.</summary>
internal readonly record struct HopOutcome(TransportResult Result, string? RedirectTo = null);

/// <summary>
/// The decisions of the transport, apart from the Windows calls so they can be tested without a network: the limits on
/// the body, and which redirects are followed.
/// </summary>
internal static class TransferLoops
{
    public const int StopTestEveryBytes = 64 * 1024;
    public const int MaxRedirects = 5;

    public static readonly TimeSpan StopTestEveryTime = TimeSpan.FromSeconds(1);

    public static TransportResult PumpBody(BodyReader read, TransferLimits limits, BodySink sink, Func<bool> shouldStop, Func<TimeSpan> elapsed)
    {
        if (shouldStop())
        {
            return new TransportResult(TransportStatus.Stopped);
        }

        var buffer = new byte[StopTestEveryBytes];
        long total = 0;
        var sinceStopTest = 0;
        var lastStopTest = elapsed();
        while (true)
        {
            var now = elapsed();
            var remaining = limits.Total - now;
            if (remaining <= TimeSpan.Zero)
            {
                return new TransportResult(TransportStatus.TimedOut);
            }

            var step = read(buffer, remaining < limits.Stall ? remaining : limits.Stall);
            if (step.Failed)
            {
                return step.Error;
            }

            if (step.Bytes == 0)
            {
                return new TransportResult(TransportStatus.Completed);
            }

            total += step.Bytes;
            if (total > limits.MaxBytes)
            {
                return new TransportResult(TransportStatus.TooLarge);
            }

            sink(buffer.AsSpan(0, step.Bytes));

            // By time as well: a server dripping bytes stays under the stall limit, and 64 KB could take the whole 5 minutes.
            sinceStopTest += step.Bytes;
            if (sinceStopTest >= StopTestEveryBytes || now - lastStopTest >= StopTestEveryTime)
            {
                sinceStopTest = 0;
                lastStopTest = now;
                if (shouldStop())
                {
                    return new TransportResult(TransportStatus.Stopped);
                }
            }
        }
    }

    /// <summary>Every address, the first one included, passes the policy before any connection is made to it.</summary>
    public static TransportResult FollowRedirects(string url, Func<Uri, HopOutcome> request, Func<bool> shouldStop)
    {
        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!UpdateUrlPolicy.IsAllowedHop(current, out var uri))
            {
                return new TransportResult(TransportStatus.Blocked);
            }

            if (shouldStop())
            {
                return new TransportResult(TransportStatus.Stopped);
            }

            var outcome = request(uri!);
            if (outcome.RedirectTo is null)
            {
                return outcome.Result;
            }

            // A relative Location is resolved against this hop, and the result then faces the policy like any other.
            // An empty one would resolve to this very address: it is no address at all, so null, which the policy blocks.
            current = outcome.RedirectTo.Length != 0 && Uri.TryCreate(uri, outcome.RedirectTo, out var next) ? next.AbsoluteUri : null;
        }

        return new TransportResult(TransportStatus.Blocked);
    }

    public static bool IsRedirect(int status) => status is 301 or 302 or 303 or 307 or 308;

    /// <summary>A body announced as larger than the ceiling is refused without reading a byte.</summary>
    public static bool AnnouncedTooLarge(string? contentLength, long maxBytes) =>
        long.TryParse(contentLength, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var length)
        && length > maxBytes;
}
