using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Settings;
using NAudio.Wave;

namespace Hindsound.Tests.Export;

/// <summary>An edit written to disk: a copy under a name of its own, an original replaced only once its edit is whole, a soundboard copy.</summary>
public sealed class ClipEditFilesTests : IDisposable
{
    private const int Rate = InternalFormat.SampleRate;
    private readonly EditFolder _folder = new();
    private readonly FakeDecoder _decoder = new();
    private readonly FakeIndex _index = new();
    private readonly List<string> _journal = [];

    public void Dispose() => _folder.Dispose();

    private ClipEditFiles Files(IRecycleBin? bin = null) => new(_decoder, bin ?? new FakeBin(RecycleOutcome.CannotRecycle), _index, _journal.Add);

    private static float[] Tone(int frames, int channels, float value) => [.. Enumerable.Repeat(value, frames * channels)];

    private static ClipGroup Group(params string[] paths) => new([.. paths.Select(path => new ClipFile(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)))]);

    private static EditRequest Cut(double start, double end, params double[] gains) =>
        new(EditorMode.Normal, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), gains);

    private static readonly ExportSettings Export = new() { Mp3Bitrate = 128, PeakCeilingDb = -1 };

    private static int WavFrames(string path)
    {
        using var reader = new WaveFileReader(path);
        return (int)reader.SampleCount; // frames: one per moment, whatever the channels
    }

    [Fact]
    public void A_copy_takes_a_name_of_its_own_when_the_cut_name_is_taken_and_joins_the_collections()
    {
        var original = _folder.Clip("dogs.mp3", _decoder, Tone(Rate, 2, 0.1f), 2);
        File.WriteAllBytes(Path.Combine(_folder.Path, "dogs (cut).mp3"), [9]);
        var clip = Group(original);

        var result = Files().Save(clip, Cut(0.2, 0.8, 0), new EditSave(EditSaveChoice.Copy, "dogs (cut)"), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.Saved, result.Outcome);
        var copy = Assert.Single(result.Paths);
        Assert.Equal(Path.Combine(_folder.Path, "dogs (cut) (2).mp3"), copy);
        Assert.True(new FileInfo(copy).Length > 1000);
        Assert.Equal([9], File.ReadAllBytes(Path.Combine(_folder.Path, "dogs (cut).mp3")));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(original));
        var copied = Assert.Single(_index.Copied);
        Assert.Same(clip, copied.From);
        Assert.Equal([copy], copied.To);
    }

    [Fact]
    public void A_clip_in_two_files_gives_two_copies_with_the_suffixes_of_their_tracks()
    {
        var system = _folder.Clip("dogs_system.wav", _decoder, Tone(Rate, 2, 0.1f), 2);
        var mic = _folder.Clip("dogs_mic.wav", _decoder, Tone(Rate, 1, 0.1f), 1);

        var result = Files().Save(Group(system, mic), Cut(0.5, 1, 0, 3), new EditSave(EditSaveChoice.Copy, "dogs (cut)"), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.Saved, result.Outcome);
        Assert.Equal([Path.Combine(_folder.Path, "dogs (cut)_system.wav"), Path.Combine(_folder.Path, "dogs (cut)_mic.wav")], result.Paths);
        Assert.Equal(Rate / 2, WavFrames(result.Paths[0]));
        Assert.Equal(Rate / 2, WavFrames(result.Paths[1]));
    }

    [Fact]
    public void A_replacement_the_recycle_bin_refuses_leaves_the_originals_and_nothing_more()
    {
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 1, 0.1f), 1);
        var before = _folder.Names();
        var bin = new FakeBin(RecycleOutcome.CannotRecycle);

        var result = Files(bin).Save(Group(original), Cut(0, 0.5, 0), new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.CannotRecycle, result.Outcome);
        Assert.Empty(result.Paths);
        Assert.Single(bin.Calls);
        Assert.Equal(before, _folder.Names());
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(original));
        Assert.Empty(_index.Moved);
    }

    [Fact]
    public void A_replacement_writes_the_whole_edit_before_the_original_goes_then_takes_its_path_and_its_labels()
    {
        var system = _folder.Clip("dogs_system.wav", _decoder, Tone(Rate, 2, 0.1f), 2);
        var mic = _folder.Clip("dogs_mic.wav", _decoder, Tone(Rate, 1, 0.1f), 1);
        var bin = new FakeBin(RecycleOutcome.Recycled, Path.Combine(_folder.Path, "bin"));
        var clip = Group(system, mic);

        var result = Files(bin).Save(clip, Cut(0, 0.25, 0, 0), new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.Saved, result.Outcome);
        Assert.Equal([system, mic], result.Paths);

        // One call, for both originals, while each edit was already whole beside them.
        var called = Assert.Single(bin.Calls);
        Assert.Equal([system, mic], called);
        Assert.True(bin.SizesAtCall["dogs_system.wav" + ClipEditFiles.TempExtension] > 44);
        Assert.True(bin.SizesAtCall["dogs_mic.wav" + ClipEditFiles.TempExtension] > 44);

        Assert.Equal(Rate / 4, WavFrames(system));
        Assert.Equal(Rate / 4, WavFrames(mic));
        Assert.Equal(["dogs_mic.wav", "dogs_system.wav"], _folder.Names());
        var moved = Assert.Single(_index.Moved);
        Assert.Same(clip, moved.From);
        Assert.Equal([system, mic], moved.To);
    }

    [Fact]
    public void An_edit_that_can_not_take_the_name_of_its_original_is_kept_under_a_name_of_its_own()
    {
        // The bin says the original went, but it is still there: the edit can't be moved onto it.
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 1, 0.1f), 1);
        var bin = new FakeBin(RecycleOutcome.Recycled);

        var result = Files(bin).Save(Group(original), Cut(0, 0.5, 0), new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, result.Outcome);
        Assert.Equal([Path.Combine(_folder.Path, "dogs (cut).wav")], result.Paths);
        Assert.Equal(Rate / 2, WavFrames(result.Paths[0]));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(original));
        Assert.Equal(["dogs (cut).wav", "dogs.wav"], _folder.Names());
        Assert.Empty(_index.Moved);
        Assert.NotEmpty(_journal);
        Assert.DoesNotContain(_journal, line => line.Contains(_folder.Path, StringComparison.OrdinalIgnoreCase) || line.Contains("dogs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_edit_that_can_take_neither_its_name_nor_one_of_its_own_is_never_left_hidden()
    {
        // The bin says the original went, but it is still there; and the name of its own is a folder, which no file can take.
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 1, 0.1f), 1);
        Directory.CreateDirectory(Path.Combine(_folder.Path, "dogs (cut).wav"));

        var result = Files(new FakeBin(RecycleOutcome.Recycled)).Save(Group(original), Cut(0, 0.5, 0), new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, result.Outcome);
        Assert.Empty(result.Paths);
        Assert.Equal(["dogs.wav"], _folder.Names());
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(original));
        Assert.Contains(_journal, line => line.StartsWith("editor: edit not kept", StringComparison.Ordinal));
        Assert.DoesNotContain(_journal, line => line.Contains(_folder.Path, StringComparison.OrdinalIgnoreCase) || line.Contains("dogs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_replacement_whose_original_went_in_part_keeps_the_whole_edit_as_a_clip_of_its_own()
    {
        // The bin takes the system track, then fails on the mic track, which stays.
        var system = _folder.Clip("dogs_system.wav", _decoder, Tone(Rate, 1, 0.1f), 1);
        var mic = _folder.Clip("dogs_mic.wav", _decoder, Tone(Rate, 1, 0.1f), 1);
        var bin = new HalfBin(Path.Combine(_folder.Path, "bin"));

        var result = Files(bin).Save(Group(system, mic), Cut(0, 0.5, 0, 0), new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, result.Outcome);
        Assert.Equal([Path.Combine(_folder.Path, "dogs (cut)_system.wav"), Path.Combine(_folder.Path, "dogs (cut)_mic.wav")], result.Paths);
        Assert.All(result.Paths, path => Assert.Equal(Rate / 2, WavFrames(path)));
        Assert.Equal(["dogs (cut)_mic.wav", "dogs (cut)_system.wav", "dogs_mic.wav"], _folder.Names());
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(mic));
        Assert.Empty(_index.Moved);
        Assert.DoesNotContain(_journal, line => line.Contains(_folder.Path, StringComparison.OrdinalIgnoreCase) || line.Contains("dogs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_replacement_whose_original_is_gone_before_the_bin_keeps_the_edit_as_a_clip_of_its_own()
    {
        // The user deleted the original while the edit was being written.
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 1, 0.1f), 1);
        var bin = new GoneBin();

        var result = Files(bin).Save(Group(original), Cut(0, 0.5, 0), new EditSave(EditSaveChoice.Replace, null), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SavedAside, result.Outcome);
        Assert.Equal([Path.Combine(_folder.Path, "dogs (cut).wav")], result.Paths);
        Assert.Equal(Rate / 2, WavFrames(result.Paths[0]));
        Assert.Equal(["dogs (cut).wav"], _folder.Names());
        Assert.Empty(_index.Moved);
        Assert.DoesNotContain(_journal, line => line.Contains(_folder.Path, StringComparison.OrdinalIgnoreCase) || line.Contains("dogs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_soundboard_copy_of_a_clip_in_two_files_is_one_mono_mp3_when_both_are_mono()
    {
        var system = _folder.Clip("dogs_system.mp3", _decoder, Tone(3 * Rate, 1, 0.1f), 1);
        var mic = _folder.Clip("dogs_mic.mp3", _decoder, Tone(3 * Rate, 1, 0.1f), 1);
        var before = _folder.Names();
        var request = new EditRequest(EditorMode.Soundboard, TimeSpan.Zero, TimeSpan.FromSeconds(2), [0, 0]);

        var result = Files().Save(Group(system, mic), request, new EditSave(EditSaveChoice.Copy, "dogs (soundboard)"), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.Saved, result.Outcome);
        var path = Assert.Single(result.Paths);
        Assert.Equal(Path.Combine(_folder.Path, "dogs (soundboard).mp3"), path);
        Assert.Equal([.. before.Append("dogs (soundboard).mp3").Order(StringComparer.Ordinal)], _folder.Names());
        using var reader = new Mp3FileReader(path);
        Assert.Equal(1, reader.WaveFormat.Channels);
    }

    [Fact]
    public void A_soundboard_copy_asked_longer_than_discord_takes_stops_at_its_longest()
    {
        var original = _folder.Clip("dogs.mp3", _decoder, Tone(10 * Rate, 1, 0.1f), 1);
        var request = new EditRequest(EditorMode.Soundboard, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9), [0]);

        var result = Files().Save(Group(original), request, new EditSave(EditSaveChoice.Copy, "dogs (soundboard)"), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.Saved, result.Outcome);
        using var reader = new Mp3FileReader(Assert.Single(result.Paths));
        Assert.InRange(reader.TotalTime.TotalSeconds, Soundboard.MaxSeconds - 0.1, Soundboard.MaxSeconds + 0.1);
    }

    [Fact]
    public void A_soundboard_copy_too_big_leaves_nothing_behind()
    {
        var original = _folder.Clip("dogs.mp3", _decoder, Tone(3 * Rate, 2, 0.1f), 2);
        var before = _folder.Names();
        var files = new ClipEditFiles(_decoder, new FakeBin(RecycleOutcome.CannotRecycle), _index, _journal.Add) { SoundboardMaxBytes = 100 };
        var request = new EditRequest(EditorMode.Soundboard, TimeSpan.Zero, TimeSpan.FromSeconds(2), [0]);

        var result = files.Save(Group(original), request, new EditSave(EditSaveChoice.Copy, "dogs (soundboard)"), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.TooBig, result.Outcome);
        Assert.Empty(result.Paths);
        Assert.Equal(before, _folder.Names());
        Assert.Empty(_index.Copied);
    }

    [Fact]
    public void An_original_gone_before_the_save_is_said_gone_and_nothing_is_written()
    {
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 1, 0.1f), 1);
        var clip = Group(original);
        File.Delete(original);

        var result = Files().Save(clip, Cut(0, 0.5, 0), new EditSave(EditSaveChoice.Copy, "dogs (cut)"), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.SourceGone, result.Outcome);
        Assert.Empty(_folder.Names());
    }

    [Fact]
    public void A_copy_that_can_not_be_decoded_fails_and_leaves_no_copy()
    {
        var original = _folder.Clip("dogs.wav", _decoder, Tone(Rate, 1, 0.1f), 1);
        _decoder.Files.Clear();
        var before = _folder.Names();

        var result = Files().Save(Group(original), Cut(0, 0.5, 0), new EditSave(EditSaveChoice.Copy, "dogs (cut)"), Export, CancellationToken.None);

        Assert.Equal(EditSaveOutcome.Failed, result.Outcome);
        Assert.Equal(before, _folder.Names());
        Assert.DoesNotContain(_journal, line => line.Contains(_folder.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_clip_reads_as_its_longest_file_with_one_list_of_bars_per_file()
    {
        var system = _folder.Clip("dogs_system.wav", _decoder, Tone(2 * Rate, 2, 0.5f), 2);
        var mic = _folder.Clip("dogs_mic.wav", _decoder, Tone(Rate, 1, 0.5f), 1);

        var reading = Files().Load(Group(system, mic), ClipEditRender.Peaks, CancellationToken.None);

        Assert.Equal(EditorLoadOutcome.Loaded, reading.Load.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(2), reading.Load.Duration);
        Assert.Equal(ExportFormat.Wav, reading.Load.Format);
        Assert.Equal(2, reading.Load.Peaks.Count);
        Assert.All(reading.Load.Peaks, bars => Assert.Equal(128, bars.Count));
        Assert.Equal([2, 1], reading.Channels);
        Assert.All(_decoder.Opened, stream => Assert.True(stream.Disposed));
    }

    [Fact]
    public void A_missing_file_reads_as_gone_and_one_that_can_not_be_decoded_as_unreadable()
    {
        var kept = _folder.Clip("dogs.mp3", _decoder, Tone(Rate, 1, 0.5f), 1);
        var gone = Group(kept);
        _decoder.Files.Clear();

        Assert.Equal(EditorLoadOutcome.Unreadable, Files().Load(gone, ClipEditRender.Peaks, CancellationToken.None).Load.Outcome);

        File.Delete(kept);
        Assert.Equal(EditorLoadOutcome.Gone, Files().Load(gone, ClipEditRender.Peaks, CancellationToken.None).Load.Outcome);
    }
}
