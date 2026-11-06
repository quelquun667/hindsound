namespace Hindsound.Updates;

/// <summary>Receives the body, piece by piece. Spans cannot be generic arguments, hence a delegate of its own.</summary>
internal delegate void BodySink(ReadOnlySpan<byte> data);

public enum TransportStatus
{
    Completed,

    /// <summary>The caller's delegate asked to stop, for instance because a game went full screen.</summary>
    Stopped,
    TooLarge,
    TimedOut,

    /// <summary>The address, or a redirect, leads outside the allowed hosts or away from HTTPS.</summary>
    Blocked,
    Failed,
}

/// <param name="Status">How the request ended.</param>
/// <param name="Detail">The HTTP status or Windows error behind a <see cref="TransportStatus.Failed"/>, for the journal.</param>
internal readonly record struct TransportResult(TransportStatus Status, int Detail = 0);

/// <param name="Total">The whole request, redirects included.</param>
/// <param name="Stall">The longest silence accepted at any point.</param>
/// <param name="MaxBytes">The most the body may hold. Refused from the announced length when there is one, before reading.</param>
internal readonly record struct TransferLimits(TimeSpan Total, TimeSpan Stall, long MaxBytes)
{
    public static TransferLimits Manifest { get; } = new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), UpdateManifestReader.MaxManifestBytes);

    public static TransferLimits ForSetup(long size) => new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), size);
}

/// <summary>A GET and nothing else. The real one is <see cref="WinHttpTransport"/>; the tests use a fake.</summary>
internal interface IUpdateTransport
{
    /// <summary>Never throws by itself. An exception from <paramref name="sink"/> goes through to the caller.</summary>
    TransportResult Get(string url, TransferLimits limits, BodySink sink, Func<bool> shouldStop);
}
