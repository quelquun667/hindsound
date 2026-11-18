using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Settings;
using NAudio.Wave;

namespace Hindsound.Tests.Export;

/// <summary>A replacement of a clip in two files where only one edit could take the name of its original.</summary>
public sealed class ClipEditFilesRescueTests : IDisposable
{
    private const int Rate = InternalFormat.SampleRate;
    private static readonly ExportSettings Export = new() { Mp3Bitrate = 128, PeakCeilingDb = -1 };
    private readonly EditFolder _folder = new();
    private readonly FakeDecoder _decoder = new();
    private readonly FakeIndex _index = new();
    private readonly List<string> _journal = [];

    public void Dispose() => _folder.Dispose();

    private static float[] Tone(int frames, float value) => [.. Enumerable.Repeat(value, frames)];

    private static ClipGroup Group(params string[] paths) => new([.. paths.Select(path => new ClipFile(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)))]);

    private static int WavFrames(string path)
    {
        using var reader = new WaveFileReader(path);
        return (int)reader.SampleCount;
    }

    [Fact]
    public void An_edit_already_in_place_goes_beside_the_one_that_was_stuck_so_the_copy_stays_a_pair()
    {
        // The bin takes the system track and says both went; the mic track is still there, and its edit can't be moved onto it.
        var system = _folder.Clip("dogs_system.wav", _decoder, Tone(Rate, 0.1f), 1);
        var mic = _folder.Clip("dogs_mic.wav", _decoder, Tone(Rate, 0.1f), 1);
        var files = new ClipEditFiles(_decoder, new FirstOnlyBin(Path.Combine(_folder.Path, "bin")), _index, _journal.Add);
        var request = new EditRequest(EditorMode.Normal, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), [0.0, 0.0]);

        var result = files.Save(Group(system, mic), request, new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, result.Outcome);
        Assert.Equal([Path.Combine(_folder.Path, "dogs (cut)_system.wav"), Path.Combine(_folder.Path, "dogs (cut)_mic.wav")], result.Paths);
        Assert.All(result.Paths, path => Assert.Equal(Rate / 2, WavFrames(path)));
        Assert.Equal(["dogs (cut)_mic.wav", "dogs (cut)_system.wav", "dogs_mic.wav"], _folder.Names());
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(mic));
        Assert.Empty(_index.Moved);
        Assert.DoesNotContain(_journal, line => line.Contains(_folder.Path, StringComparison.OrdinalIgnoreCase) || line.Contains("dogs", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A Recycle Bin that takes the first file to a folder of the test and says it took them all: the others stay.</summary>
    private sealed class FirstOnlyBin(string binFolder) : IRecycleBin
    {
        public RecycleOutcome Recycle(IReadOnlyList<string> paths)
        {
            Directory.CreateDirectory(binFolder);
            File.Move(paths[0], Path.Combine(binFolder, Path.GetFileName(paths[0])));
            return RecycleOutcome.Recycled;
        }
    }
}
