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

    [Fact]
    public void An_edit_that_can_not_be_moved_to_a_name_of_its_own_leaves_no_hidden_file_behind()
    {
        // The bin says the original went but it stays, so the edit can't take its name; then the edit, held by another hand, can't
        // be moved to a name of its own. The hand lets go as the log tells it, and what is left of the edit must go.
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 0.1f), 1);
        var bin = new HoldingBin();
        var files = new ClipEditFiles(_decoder, bin, _index, line =>
        {
            _journal.Add(line);
            if (line.StartsWith("editor: edit not kept", StringComparison.Ordinal))
            {
                bin.LetGo();
            }
        });
        var request = new EditRequest(EditorMode.Normal, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), [0.0]);

        var result = files.Save(Group(original), request, new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, result.Outcome);
        Assert.Empty(result.Paths);
        Assert.Contains(_journal, line => line.StartsWith("editor: edit not kept", StringComparison.Ordinal));
        Assert.Equal(["dogs.wav"], _folder.Names());
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(original));
        Assert.Empty(_index.Moved);
    }

    [Fact]
    public void A_replacement_says_its_original_is_in_the_bin_when_the_bin_took_all_of_it_or_part_of_it()
    {
        var kept = _folder.Clip("cats.wav", _decoder, Tone(Rate, 0.1f), 1);
        var request = new EditRequest(EditorMode.Normal, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), [0.0]);
        var all = new ClipEditFiles(_decoder, new FakeBin(RecycleOutcome.Recycled), _index, _journal.Add)
            .Save(Group(kept), request, new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, all.Outcome);
        Assert.True(all.OriginalInBin);

        var system = _folder.Clip("dogs_system.wav", _decoder, Tone(Rate, 0.1f), 1);
        var mic = _folder.Clip("dogs_mic.wav", _decoder, Tone(Rate, 0.1f), 1);
        var part = new ClipEditFiles(_decoder, new HalfBin(Path.Combine(_folder.Path, "bin")), _index, _journal.Add)
            .Save(Group(system, mic), new EditRequest(EditorMode.Normal, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), [0.0, 0.0]), new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, part.Outcome);
        Assert.True(part.OriginalInBin);
    }

    [Fact]
    public void A_replacement_whose_original_was_gone_before_the_bin_says_nothing_went_to_the_bin()
    {
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 0.1f), 1);
        var request = new EditRequest(EditorMode.Normal, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), [0.0]);

        var result = new ClipEditFiles(_decoder, new GoneBin(), _index, _journal.Add)
            .Save(Group(original), request, new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, result.Outcome);
        Assert.False(result.OriginalInBin);
    }

    /// <summary>A Recycle Bin that says it took the original, which stays, and keeps the edit of it open until it is told to let go.</summary>
    private sealed class HoldingBin : IRecycleBin
    {
        private FileStream? _held;

        public RecycleOutcome Recycle(IReadOnlyList<string> paths)
        {
            _held = new FileStream(paths[0] + ClipEditFiles.TempExtension, FileMode.Open, FileAccess.Read, FileShare.None);
            return RecycleOutcome.Recycled;
        }

        public void LetGo()
        {
            _held?.Dispose();
            _held = null;
        }
    }

    [Fact]
    public void An_edit_kept_aside_once_the_original_went_to_the_bin_takes_its_star_and_collections()
    {
        // The bin takes the original, and something takes its name at once: the edit is kept under a name of its own, and it stands
        // in for the original the user replaced, so the star and the collections go with it.
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 0.1f), 1);
        var clip = Group(original);
        var files = new ClipEditFiles(_decoder, new BlockingBin(Path.Combine(_folder.Path, "bin")), _index, _journal.Add);
        var request = new EditRequest(EditorMode.Normal, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), [0.0]);

        var result = files.Save(clip, request, new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, result.Outcome);
        Assert.Equal([Path.Combine(_folder.Path, "dogs (cut).wav")], result.Paths);
        var moved = Assert.Single(_index.Moved);
        Assert.Same(clip, moved.From);
        Assert.Equal(result.Paths, moved.To);
        Assert.Empty(_index.Copied);
    }

    [Fact]
    public void An_edit_kept_aside_beside_an_original_still_there_only_joins_its_collections()
    {
        // The bin says the original went, but it is still there: it keeps its star, and the edit joins its collections as a copy.
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 0.1f), 1);
        var clip = Group(original);
        var files = new ClipEditFiles(_decoder, new SayingBin(), _index, _journal.Add);
        var request = new EditRequest(EditorMode.Normal, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), [0.0]);

        var result = files.Save(clip, request, new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, result.Outcome);
        var copied = Assert.Single(_index.Copied);
        Assert.Same(clip, copied.From);
        Assert.Equal(result.Paths, copied.To);
        Assert.Empty(_index.Moved);
    }

    /// <summary>A Recycle Bin that takes every file to a folder of the test, then a folder takes the name of each, which no file can take.</summary>
    private sealed class BlockingBin(string binFolder) : IRecycleBin
    {
        public RecycleOutcome Recycle(IReadOnlyList<string> paths)
        {
            Directory.CreateDirectory(binFolder);
            foreach (var path in paths)
            {
                File.Move(path, Path.Combine(binFolder, Path.GetFileName(path)));
                Directory.CreateDirectory(path);
            }

            return RecycleOutcome.Recycled;
        }
    }

    /// <summary>A Recycle Bin that says it took the files and leaves them where they are.</summary>
    private sealed class SayingBin : IRecycleBin
    {
        public RecycleOutcome Recycle(IReadOnlyList<string> paths) => RecycleOutcome.Recycled;
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
