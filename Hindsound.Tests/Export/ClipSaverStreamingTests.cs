using Hindsound.Audio;
using Hindsound.Export;
using NAudio.Wave;
using Xunit.Abstractions;

namespace Hindsound.Tests.Export;

public sealed class ClipSaverStreamingTests : IDisposable
{
    private static readonly DateTime Moment = new(2026, 11, 6, 14, 30, 5);

    private readonly TempFolder _root = new();
    private readonly ITestOutputHelper _output;

    public ClipSaverStreamingTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _root.Dispose();

    private string OutputFolder => _root.Combine("out");

    private string RescueFolder => _root.Combine("rescue");

    private static IAudioFileWriter FailingOpen(string path, int channels, int kbps)
    {
        File.WriteAllText(path, "half an mp3"); // the encoder got as far as creating the file
        throw new DllNotFoundException("libmp3lame.64.dll");
    }

    private static ExportOptions Mp3Options(string folder) =>
        new() { Format = ExportFormat.Mp3, Mp3BitrateKbps = 192, Normalization = NormalizationMode.Off, OutputFolder = folder };

    // ---------------------------------------------------------------- MP3 failure falls back to WAV

    [Fact]
    public void A_failing_mp3_encoder_gives_a_wav_in_the_same_folder_and_says_so()
    {
        var system = Signals.Sine(300, 0.3, 1.5);
        var saver = new ClipSaver(RescueFolder, () => Moment, FailingOpen);

        var result = saver.Save(new AudioSnapshot(system, []), Mp3Options(OutputFolder), "Game");

        Assert.True(result.FellBackToWav);
        Assert.False(result.Rescued);
        Assert.Equal(Path.Combine(OutputFolder, "Hindsound_Game_2026-11-06_14-30-05.wav"), result.PrimaryPath);
        Assert.Equal([result.PrimaryPath], Directory.GetFiles(OutputFolder)); // the partial MP3 is gone
        var (samples, format, duration) = Signals.ReadWav(result.PrimaryPath);
        Assert.Equal(48000, format.SampleRate);
        Assert.Equal(2, format.Channels);
        Assert.Equal(TimeSpan.FromSeconds(1.5), duration);
        Assert.Equal(system[2 * 5000], samples[2 * 5000], 2e-5);
        Assert.Equal(new FileInfo(result.PrimaryPath).Length, result.SizeBytes);
    }

    [Fact]
    public void An_mp3_that_fails_on_the_second_track_leaves_two_wav_files_and_no_mp3()
    {
        var calls = 0;
        IAudioFileWriter OpenSecondFails(string path, int channels, int kbps) =>
            ++calls == 1 ? Encoders.OpenMp3(path, channels, kbps) : FailingOpen(path, channels, kbps);
        var saver = new ClipSaver(RescueFolder, () => Moment, OpenSecondFails);
        var options = Mp3Options(OutputFolder) with { TrackMode = TrackMode.MultiTrack };

        var result = saver.Save(new AudioSnapshot(Signals.Sine(300, 0.3, 1), Signals.Sine(500, 0.2, 1, channels: 1)), options, "Game");

        Assert.True(result.FellBackToWav);
        Assert.Equal(2, result.Paths.Count);
        Assert.All(result.Paths, p => Assert.EndsWith(".wav", p));
        Assert.Equal(2, Directory.GetFiles(OutputFolder).Length);
        Assert.All(result.Paths, p => Assert.Equal(TimeSpan.FromSeconds(1), Signals.ReadWav(p).Duration));
    }

    [Fact]
    public void A_working_mp3_encoder_does_not_set_the_fallback_flag()
    {
        var result = new ClipSaver(RescueFolder, () => Moment).Save(new AudioSnapshot(Signals.Sine(300, 0.3, 1), []), Mp3Options(OutputFolder), "Game");

        Assert.False(result.FellBackToWav);
        Assert.EndsWith(".mp3", result.PrimaryPath);
    }

    [Fact]
    public void A_failing_encoder_in_an_unwritable_folder_still_saves_a_wav_in_the_rescue_folder()
    {
        var blocker = _root.Combine("blocker");
        File.WriteAllText(blocker, "i am a file");
        var saver = new ClipSaver(RescueFolder, () => Moment, FailingOpen);

        var result = saver.Save(new AudioSnapshot(Signals.Sine(300, 0.3, 1), []), Mp3Options(Path.Combine(blocker, "x")), "Game");

        Assert.True(result.Rescued);
        Assert.True(result.FellBackToWav);
        Assert.Equal(RescueFolder, Path.GetDirectoryName(result.PrimaryPath));
        Assert.EndsWith(".wav", result.PrimaryPath);
        Assert.Equal([result.PrimaryPath], Directory.GetFiles(RescueFolder));
    }

    // ---------------------------------------------------------------- streaming limiter

    [Fact]
    public void The_streaming_limiter_gives_the_same_samples_in_pieces_as_in_one_go()
    {
        var random = new Random(99);
        var audio = new float[48000 * 2 * 2];
        for (var i = 0; i < audio.Length; i++) audio[i] = (float)(random.NextDouble() * 6 - 3);
        var whole = (float[])audio.Clone();
        const float ceiling = 0.891f;

        Limiter.Apply(whole, 2, ceiling);

        var pieces = new List<float>();
        var limiter = new StreamingLimiter(2, ceiling);
        var buffer = new float[2 * 1777];
        for (var start = 0; start < audio.Length; start += buffer.Length)
        {
            var count = Math.Min(buffer.Length, audio.Length - start);
            audio.AsSpan(start, count).CopyTo(buffer);
            var written = limiter.Process(buffer.AsSpan(0, count));
            pieces.AddRange(buffer.AsSpan(0, written * 2).ToArray());
        }

        var tail = new float[2 * limiter.DelayFrames];
        pieces.AddRange(tail.AsSpan(0, limiter.Flush(tail) * 2).ToArray());

        Assert.Equal(whole, pieces.ToArray());
    }

    // ---------------------------------------------------------------- memory

    private AudioSnapshot Snapshot(int seconds)
    {
        var frames = 48000 * seconds;
        var system = new float[frames * 2];
        var mic = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / 48000;
            system[2 * i] = (float)(0.2 * Math.Sin(2 * Math.PI * 220 * t) + 0.1 * Math.Sin(2 * Math.PI * 1760 * t));
            system[2 * i + 1] = system[2 * i] * 0.8f;
            mic[i] = (float)(0.15 * Math.Sin(2 * Math.PI * 330 * t));
        }

        return new AudioSnapshot(system, mic);
    }

    private long Allocated(AudioSnapshot snapshot, ExportOptions options)
    {
        var saver = new ClipSaver(RescueFolder, () => Moment);
        saver.Save(snapshot, options with { OutputFolder = _root.Combine("warmup") }, "warm"); // JIT and first-use allocations
        var before = GC.GetAllocatedBytesForCurrentThread();
        saver.Save(snapshot, options, "measure");
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // Measured (60 s of PC audio plus microphone, loudness normalization): MP3 429 232 bytes, WAV 161 168 bytes.
    // The bounds are 1.5 times that. Before the clip went through in pieces the same save allocated 23 MB (and 116 MB
    // for 5:00): a copy of the whole clip is a hundred times over these bounds.
    private const long Mp3BoundBytes = 650_000;
    private const long WavBoundBytes = 250_000;

    [Fact]
    public void Saving_60_seconds_as_mp3_allocates_little()
    {
        var snapshot = Snapshot(60);
        var options = new ExportOptions { Format = ExportFormat.Mp3, Mp3BitrateKbps = 192, Normalization = NormalizationMode.Loudness, OutputFolder = OutputFolder };

        var allocated = Allocated(snapshot, options);

        _output.WriteLine($"MP3, 60 s, loudness: {allocated} bytes allocated (snapshot itself: {(snapshot.System.Length + snapshot.Mic.Length) * 4 / 1e6:F1} MB)");
        Assert.True(allocated < Mp3BoundBytes, $"{allocated} bytes");
    }

    [Fact]
    public void Saving_60_seconds_as_wav_allocates_little()
    {
        var snapshot = Snapshot(60);
        var options = new ExportOptions { Format = ExportFormat.Wav, Normalization = NormalizationMode.Loudness, OutputFolder = OutputFolder };

        var allocated = Allocated(snapshot, options);

        _output.WriteLine($"WAV, 60 s, loudness: {allocated} bytes allocated (snapshot itself: {(snapshot.System.Length + snapshot.Mic.Length) * 4 / 1e6:F1} MB)");
        Assert.True(allocated < WavBoundBytes, $"{allocated} bytes");
    }
}
