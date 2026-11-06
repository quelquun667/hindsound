using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

/// <summary>Serves bytes from memory, in chunks, and writes down what it was asked.</summary>
internal sealed class FakeTransport(byte[] body, TransportStatus status = TransportStatus.Completed) : IUpdateTransport
{
    public int ChunkBytes { get; init; } = 16 * 1024;

    public List<string> Urls { get; } = [];

    public List<TransferLimits> Limits { get; } = [];

    /// <summary>Runs before each chunk, to look at the disk while the transfer is under way.</summary>
    public Action<int>? BeforeChunk { get; init; }

    public TransportResult Get(string url, TransferLimits limits, BodySink sink, Func<bool> shouldStop)
    {
        Urls.Add(url);
        Limits.Add(limits);
        for (var offset = 0; offset < body.Length; offset += ChunkBytes)
        {
            BeforeChunk?.Invoke(offset);
            sink(body.AsSpan(offset, Math.Min(ChunkBytes, body.Length - offset)));
        }

        return new TransportResult(status);
    }
}
