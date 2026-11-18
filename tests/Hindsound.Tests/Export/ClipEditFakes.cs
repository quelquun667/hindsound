using System.Collections.Concurrent;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;

namespace Hindsound.Tests.Export;

/// <summary>A folder of its own under the temporary folder of the system, gone with the test.</summary>
internal sealed class EditFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));

    public EditFolder() => Directory.CreateDirectory(Path);

    /// <summary>A file of the library: some bytes on disk, which the fake decoder reads as the samples it is given.</summary>
    public string Clip(string name, FakeDecoder decoder, float[] samples, int channels)
    {
        var path = System.IO.Path.Combine(Path, name);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        decoder.Files[path] = (samples, channels);
        return path;
    }

    public string[] Names() => [.. Directory.GetFiles(Path).Select(System.IO.Path.GetFileName).Order(StringComparer.Ordinal)!];

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { /* a file still held goes with the next cleanup of the system */ }
    }
}

/// <summary>Decodes nothing: each path it knows reads as the samples it was given, at 48 kHz.</summary>
internal sealed class FakeDecoder : IClipDecoder
{
    public ConcurrentDictionary<string, (float[] Samples, int Channels)> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<FakeStream> Opened { get; } = new();

    public IDecodedStream Open(string path)
    {
        if (!Files.TryGetValue(path, out var file))
        {
            throw new ClipDecodeException("unknown");
        }

        var stream = new FakeStream(file.Samples, file.Channels);
        Opened.Enqueue(stream);
        return stream;
    }
}

internal sealed class FakeStream(float[] samples, int channels) : IDecodedStream
{
    private int _position;

    public int Channels => channels;

    public TimeSpan Duration => TimeSpan.FromSeconds(samples.Length / channels / (double)InternalFormat.SampleRate);

    public bool Disposed { get; private set; }

    public void Seek(TimeSpan at) => _position = (int)Math.Min(samples.Length, ClipEditRender.FrameAt(at) * channels);

    public int Read(Span<float> interleaved)
    {
        var count = Math.Min(interleaved.Length / channels * channels, samples.Length - _position);
        samples.AsSpan(_position, count).CopyTo(interleaved);
        _position += count;
        return count;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Keeps every sample written, in memory.</summary>
internal sealed class CollectingWriter : IAudioFileWriter
{
    public List<float> Samples { get; } = [];

    public void Write(ReadOnlySpan<float> interleaved) => Samples.AddRange(interleaved.ToArray());

    public void Dispose()
    {
    }
}

/// <summary>A Recycle Bin that refuses, or that moves the files to a folder of the test, and tells what it saw.</summary>
internal sealed class FakeBin(RecycleOutcome answer, string? binFolder = null) : IRecycleBin
{
    public List<IReadOnlyList<string>> Calls { get; } = [];

    /// <summary>What each call found in the folder of the files, at the moment it was called.</summary>
    public List<string[]> FolderAtCall { get; } = [];

    public Dictionary<string, long> SizesAtCall { get; } = new(StringComparer.OrdinalIgnoreCase);

    public RecycleOutcome Recycle(IReadOnlyList<string> paths)
    {
        Calls.Add(paths);
        var folder = Path.GetDirectoryName(paths[0])!;
        FolderAtCall.Add([.. Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal)!]);
        foreach (var file in Directory.GetFiles(folder))
        {
            SizesAtCall[Path.GetFileName(file)] = new FileInfo(file).Length;
        }

        if (answer == RecycleOutcome.Recycled && binFolder is not null)
        {
            Directory.CreateDirectory(binFolder);
            foreach (var path in paths)
            {
                File.Move(path, Path.Combine(binFolder, Path.GetFileName(path)));
            }
        }

        return answer;
    }
}

/// <summary>A Recycle Bin that takes the first file to a folder of the test, then fails on the others, which stay.</summary>
internal sealed class HalfBin(string binFolder) : IRecycleBin
{
    public RecycleOutcome Recycle(IReadOnlyList<string> paths)
    {
        Directory.CreateDirectory(binFolder);
        File.Move(paths[0], Path.Combine(binFolder, Path.GetFileName(paths[0])));
        return RecycleOutcome.Failed;
    }
}

/// <summary>A Recycle Bin that finds each file deleted by another hand just before it was to take it.</summary>
internal sealed class GoneBin : IRecycleBin
{
    public RecycleOutcome Recycle(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            File.Delete(path);
        }

        return RecycleOutcome.Gone;
    }
}

internal sealed class FakeIndex : IClipIndex
{
    public List<(ClipGroup From, IReadOnlyList<string> To)> Moved { get; } = [];

    public List<(ClipGroup From, IReadOnlyList<string> To)> Copied { get; } = [];

    public LibraryOutcome MoveLabels(ClipGroup from, IReadOnlyList<string> to)
    {
        Moved.Add((from, to));
        return LibraryOutcome.Done;
    }

    public LibraryOutcome CopyCollections(ClipGroup from, IReadOnlyList<string> to)
    {
        Copied.Add((from, to));
        return LibraryOutcome.Done;
    }
}
