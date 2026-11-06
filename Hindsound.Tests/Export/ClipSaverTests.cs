using System.Diagnostics;
using Hindsound.Audio;
using Hindsound.Export;
using NAudio.Wave;
using Xunit.Abstractions;

namespace Hindsound.Tests.Export;

public sealed class ClipSaverTests : IDisposable
{
    private static readonly DateTime Moment = new(2026, 11, 6, 14, 30, 5);

    private readonly TempFolder _root = new();
    private readonly ITestOutputHelper _output;

    public ClipSaverTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _root.Dispose();

    private string OutputFolder => _root.Combine("out");

    private string RescueFolder => _root.Combine("rescue");

    private ClipSaver Saver() => new(RescueFolder, () => Moment);

    private ExportOptions Wav(Func<ExportOptions, ExportOptions>? change = null)
    {
        var options = new ExportOptions { Format = ExportFormat.Wav, Normalization = NormalizationMode.Off, OutputFolder = OutputFolder };
        return change?.Invoke(options) ?? options;
    }

    // ---------------------------------------------------------------- mix

    [Fact]
    public void Mix_applies_the_gains_of_each_source_in_dB()
    {
        var system = Signals.Sine(300, 0.1, 1);
        var mic = Signals.Sine(700, 0.05, 1, channels: 1);
        var options = Wav(o => o with { SystemGainDb = 6, MicGainDb = -6 });

        var result = Saver().Save(new AudioSnapshot(system, mic), options, "Game");

        var raw = Signals.ReadWavRaw(result.PrimaryPath);
        Assert.Equal(system.Length, raw.Length);
        var systemGain = Math.Pow(10, 6 / 20.0);
        var micGain = Math.Pow(10, -6 / 20.0);
        for (var frame = 0; frame < mic.Length; frame += 7)
        {
            // Reference computed apart, in double: system and mic are summed, mic on both channels.
            var expectedLeft = Math.Round((system[2 * frame] * systemGain + mic[frame] * micGain) * 32767);
            var expectedRight = Math.Round((system[2 * frame + 1] * systemGain + mic[frame] * micGain) * 32767);
            Assert.InRange(raw[2 * frame], expectedLeft - 1, expectedLeft + 1);
            Assert.InRange(raw[2 * frame + 1], expectedRight - 1, expectedRight + 1);
        }
    }

    [Fact]
    public void Mix_of_two_loud_sources_does_not_clip()
    {
        // 0.9 + 0.9 reaches 1.8: without protection the file would be flat-topped at full scale.
        var system = Signals.Sine(300, 0.9, 2);
        var mic = Signals.Sine(300, 0.9, 2, channels: 1);

        var result = Saver().Save(new AudioSnapshot(system, mic), Wav(), null);

        var raw = Signals.ReadWavRaw(result.PrimaryPath);
        Assert.True(raw.Max(s => Math.Abs((int)s)) <= 32767);
        var longestFlatTop = 0;
        var run = 0;
        foreach (var s in raw)
        {
            run = Math.Abs((int)s) >= 32767 ? run + 1 : 0;
            longestFlatTop = Math.Max(longestFlatTop, run);
        }

        Assert.True(longestFlatTop <= 2, $"{longestFlatTop} consecutive full-scale samples: the mix was clipped, not limited");
        Assert.True(raw.Max(s => Math.Abs((int)s)) > 30000); // and it was not simply made quiet
    }

    [Fact]
    public void Mix_of_a_shorter_mic_is_aligned_to_the_end_of_the_system_audio()
    {
        var system = Signals.Constant(0.1f, 48000 * 2);
        var mic = Signals.Constant(0.2f, 48000, channels: 1);

        var result = Saver().Save(new AudioSnapshot(system, mic), Wav(), null);

        var raw = Signals.ReadWavRaw(result.PrimaryPath);
        Assert.Equal(48000 * 2 * 2, raw.Length);
        Assert.InRange(raw[2 * 100], 3276, 3278);          // 0.1: system alone in the first second
        Assert.InRange(raw[2 * (48000 + 100)], 9829, 9831); // 0.3: system plus mic in the second one
        Assert.Equal(TimeSpan.FromSeconds(2), result.Duration);
    }

    [Fact]
    public void Mix_with_only_the_system_audio_or_only_the_mic_works()
    {
        var onlySystem = Saver().Save(new AudioSnapshot(Signals.Sine(300, 0.3, 1), []), Wav(), null);
        var onlyMic = Saver().Save(new AudioSnapshot([], Signals.Sine(300, 0.3, 1, channels: 1)), Wav(), null);

        var (_, systemFormat, systemDuration) = Signals.ReadWav(onlySystem.PrimaryPath);
        var (micSamples, micFormat, micDuration) = Signals.ReadWav(onlyMic.PrimaryPath);
        Assert.Equal(2, systemFormat.Channels);
        Assert.Equal(TimeSpan.FromSeconds(1), systemDuration);
        Assert.Equal(2, micFormat.Channels);
        Assert.Equal(TimeSpan.FromSeconds(1), micDuration);
        Assert.Equal(micSamples[2 * 500], micSamples[2 * 500 + 1]); // the mic is centred
    }

    // ---------------------------------------------------------------- multitrack

    [Fact]
    public void Multitrack_writes_a_system_and_a_mic_file_of_the_same_duration()
    {
        var system = Signals.Sine(300, 0.3, 1.5);
        var mic = Signals.Sine(700, 0.2, 1.0, channels: 1); // shorter: padded at the start
        var options = Wav(o => o with { TrackMode = TrackMode.MultiTrack, SystemGainDb = 3 });

        var result = Saver().Save(new AudioSnapshot(system, mic), options, "Game");

        Assert.Equal(2, result.Paths.Count);
        Assert.EndsWith("_system.wav", result.Paths[0]);
        Assert.EndsWith("_mic.wav", result.Paths[1]);
        var (systemSamples, systemFormat, systemDuration) = Signals.ReadWav(result.Paths[0]);
        var (micSamples, micFormat, micDuration) = Signals.ReadWav(result.Paths[1]);
        Assert.Equal(2, systemFormat.Channels);
        Assert.Equal(1, micFormat.Channels);
        Assert.Equal(TimeSpan.FromSeconds(1.5), systemDuration);
        Assert.Equal(systemDuration, micDuration);
        Assert.Equal(systemSamples.Length / 2, micSamples.Length);

        // Gain applied to the system track only, and the mic is silent for the first half second.
        Assert.Equal(system[2 * 5000] * Math.Pow(10, 3 / 20.0), systemSamples[2 * 5000], 2e-4);
        Assert.All(micSamples.Take(24000), s => Assert.Equal(0f, s));
        Assert.Equal(mic[100], micSamples[24000 + 100], 2e-4);
        Assert.Equal(result.Paths.Sum(p => new FileInfo(p).Length), result.SizeBytes);
    }

    [Fact]
    public void Multitrack_without_a_mic_writes_only_the_system_track()
    {
        var options = Wav(o => o with { TrackMode = TrackMode.MultiTrack });

        var result = Saver().Save(new AudioSnapshot(Signals.Sine(300, 0.3, 1), []), options, null);

        Assert.Single(result.Paths);
        Assert.EndsWith("_system.wav", result.Paths[0]);
    }

    [Fact]
    public void Multitrack_linked_gain_keeps_the_balance_between_the_tracks()
    {
        var system = Signals.Sine(300, 0.04, 8);
        var mic = Signals.Sine(700, 0.02, 8, channels: 1); // half the level
        var options = Wav(o => o with
        {
            TrackMode = TrackMode.MultiTrack,
            Normalization = NormalizationMode.Loudness,
            LinkedTrackGain = true,
        });

        var result = Saver().Save(new AudioSnapshot(system, mic), options, null);

        var systemPeak = Signals.Peak(Signals.ReadWav(result.Paths[0]).Samples);
        var micPeak = Signals.Peak(Signals.ReadWav(result.Paths[1]).Samples);
        Assert.Equal(2.0, systemPeak / micPeak, 0.01);
        Assert.True(systemPeak > 0.04 * 1.5); // and it was raised
    }

    [Fact]
    public void Multitrack_unlinked_normalizes_each_track_on_its_own()
    {
        var system = Signals.Sine(300, 0.2, 8);
        var mic = Signals.Sine(700, 0.1, 8, channels: 1);
        var options = Wav(o => o with
        {
            TrackMode = TrackMode.MultiTrack,
            Normalization = NormalizationMode.Peak,
            LinkedTrackGain = false,
        });

        var result = Saver().Save(new AudioSnapshot(system, mic), options, null);

        Assert.Equal(-1.0, Signals.PeakDb(Signals.ReadWav(result.Paths[0]).Samples), 0.01);
        Assert.Equal(-1.0, Signals.PeakDb(Signals.ReadWav(result.Paths[1]).Samples), 0.01);
    }

    // ---------------------------------------------------------------- normalization through the saver

    [Fact]
    public void A_very_quiet_clip_comes_out_at_the_target_loudness_under_the_ceiling()
    {
        var snapshot = new AudioSnapshot(Signals.Sine(1000, Signals.DbToLinear(-30), 10), []);
        var options = Wav(o => o with { Normalization = NormalizationMode.Loudness, TargetLufs = -16, PeakCeilingDb = -1 });

        var result = Saver().Save(snapshot, options, null);

        var (samples, _, _) = Signals.ReadWav(result.PrimaryPath);
        Assert.Equal(-16.0, LoudnessMeter.Measure(samples, 2), 0.1);
        Assert.True(Signals.PeakDb(samples) <= -1.0 + 0.001);
    }

    [Fact]
    public void A_silent_clip_stays_silent_and_is_reported_as_silent()
    {
        var snapshot = new AudioSnapshot(new float[48000 * 2], new float[48000]);
        var options = Wav(o => o with { Normalization = NormalizationMode.Loudness });

        var result = Saver().Save(snapshot, options, null);

        Assert.True(result.IsSilent);
        Assert.All(Signals.ReadWavRaw(result.PrimaryPath), s => Assert.Equal(0, s));
    }

    // ---------------------------------------------------------------- result

    [Fact]
    public void The_result_describes_what_the_interface_shows()
    {
        var system = Signals.Constant(0.1f, 48000 * 3);
        var snapshot = new AudioSnapshot(system, []);

        var result = Saver().Save(snapshot, Wav(), "Valorant");

        Assert.Equal(TimeSpan.FromSeconds(3), result.Duration);
        Assert.False(result.IsSilent);
        Assert.False(result.Rescued);
        Assert.Equal(24, result.Bars.Count);
        Assert.All(result.Bars, b => Assert.Equal(0.66667f, b, 1e-4f)); // 0.1 constant: -20 dB
        Assert.Equal(new FileInfo(result.PrimaryPath).Length, result.SizeBytes);
        Assert.Equal(Path.Combine(OutputFolder, "Hindsound_Valorant_2026-11-06_14-30-05.wav"), result.PrimaryPath);
    }

    [Fact]
    public void The_output_folder_is_created_and_environment_variables_are_expanded()
    {
        Environment.SetEnvironmentVariable("HINDSOUND_TEST_ROOT", _root.Path);
        try
        {
            var options = Wav(o => o with { OutputFolder = @"%HINDSOUND_TEST_ROOT%\deep\er" });

            var result = Saver().Save(new AudioSnapshot(Signals.Sine(300, 0.3, 0.5), []), options, null);

            Assert.Equal(_root.Combine("deep", "er"), Path.GetDirectoryName(result.PrimaryPath));
        }
        finally { Environment.SetEnvironmentVariable("HINDSOUND_TEST_ROOT", null); }
    }

    [Fact]
    public void An_existing_file_is_never_overwritten()
    {
        var snapshot = new AudioSnapshot(Signals.Sine(300, 0.3, 0.5), []);

        var first = Saver().Save(snapshot, Wav(), "Game");
        var firstBytes = File.ReadAllBytes(first.PrimaryPath);
        var second = Saver().Save(snapshot, Wav(), "Game");

        Assert.NotEqual(first.PrimaryPath, second.PrimaryPath);
        Assert.EndsWith("Hindsound_Game_2026-11-06_14-30-05 (2).wav", second.PrimaryPath);
        Assert.Equal(firstBytes, File.ReadAllBytes(first.PrimaryPath));
    }

    [Fact]
    public void An_empty_snapshot_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Saver().Save(new AudioSnapshot([], []), Wav(), null));
    }

    [Fact]
    public void An_unsupported_mp3_bitrate_is_refused_and_nothing_is_written()
    {
        var options = Wav(o => o with { Format = ExportFormat.Mp3, Mp3BitrateKbps = 64 });

        Assert.Throws<ArgumentException>(() => Saver().Save(new AudioSnapshot(Signals.Sine(300, 0.3, 0.5), []), options, null));
        Assert.False(Directory.Exists(OutputFolder));
        Assert.False(Directory.Exists(RescueFolder));
    }

    // ---------------------------------------------------------------- rescue

    [Fact]
    public void A_folder_that_cannot_be_written_sends_the_clip_to_the_rescue_folder()
    {
        // A folder cannot be created under a file: the output folder is "inside" this file.
        var blocker = _root.Combine("blocker");
        File.WriteAllText(blocker, "i am a file");
        var options = Wav(o => o with { OutputFolder = Path.Combine(blocker, "clips") });
        var snapshot = new AudioSnapshot(Signals.Sine(300, 0.3, 1), []);

        var result = Saver().Save(snapshot, options, "Game");

        Assert.True(result.Rescued);
        Assert.Equal(RescueFolder, Path.GetDirectoryName(result.PrimaryPath));
        Assert.True(File.Exists(result.PrimaryPath));
        Assert.Equal(TimeSpan.FromSeconds(1), Signals.ReadWav(result.PrimaryPath).Duration);
    }

    [Fact]
    public void An_invalid_folder_path_is_rescued_too()
    {
        var options = Wav(o => o with { OutputFolder = "C:\\<>|?*" });

        var result = Saver().Save(new AudioSnapshot(Signals.Sine(300, 0.3, 0.5), []), options, null);

        Assert.True(result.Rescued);
        Assert.Equal(RescueFolder, Path.GetDirectoryName(result.PrimaryPath));
    }

    [Fact]
    public void A_multitrack_clip_is_rescued_whole()
    {
        var blocker = _root.Combine("blocker");
        File.WriteAllText(blocker, "i am a file");
        var options = Wav(o => o with { OutputFolder = Path.Combine(blocker, "x"), TrackMode = TrackMode.MultiTrack });
        var snapshot = new AudioSnapshot(Signals.Sine(300, 0.3, 1), Signals.Sine(300, 0.3, 1, channels: 1));

        var result = Saver().Save(snapshot, options, null);

        Assert.True(result.Rescued);
        Assert.All(result.Paths, p => Assert.Equal(RescueFolder, Path.GetDirectoryName(p)));
        Assert.Equal(2, Directory.GetFiles(RescueFolder).Length);
    }

    [Fact]
    public void A_clip_that_cannot_be_saved_anywhere_throws_instead_of_failing_silently()
    {
        var blocker = _root.Combine("blocker");
        File.WriteAllText(blocker, "i am a file");
        var saver = new ClipSaver(Path.Combine(blocker, "rescue"), () => Moment);
        var options = Wav(o => o with { OutputFolder = Path.Combine(blocker, "out") });

        Assert.ThrowsAny<Exception>(() => saver.Save(new AudioSnapshot(Signals.Sine(300, 0.3, 0.5), []), options, null));
    }

    // ---------------------------------------------------------------- async, MP3 end to end

    [Fact]
    public async Task SaveAsync_returns_at_once_while_the_work_goes_on_in_the_background()
    {
        // The work stops at the clock (called once the audio is rendered) until the test lets it go.
        // If SaveAsync did the work on the caller's thread, it would wait here (up to 10 s) and come back finished.
        using var gate = new ManualResetEventSlim(false);
        var saver = new ClipSaver(RescueFolder, () =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return Moment;
        });

        var task = saver.SaveAsync(new AudioSnapshot(Signals.Sine(300, 0.3, 0.5), []), Wav(), "x");

        Assert.False(task.IsCompleted);
        gate.Set();
        var result = await task;
        Assert.True(File.Exists(result.PrimaryPath));
    }

    [Fact]
    public async Task A_cancelled_save_writes_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Saver().SaveAsync(new AudioSnapshot(Signals.Sine(300, 0.3, 0.5), []), Wav(), null, cancelled.Token));
        Assert.False(Directory.Exists(OutputFolder));
    }

    [Fact]
    public void A_snapshot_comes_out_as_an_mp3_that_decodes_with_the_right_duration()
    {
        var snapshot = new AudioSnapshot(Signals.Sine(440, 0.3, 4), Signals.Sine(880, 0.1, 4, channels: 1));
        var options = new ExportOptions { Format = ExportFormat.Mp3, Mp3BitrateKbps = 192, OutputFolder = OutputFolder };

        var result = Saver().Save(snapshot, options, "Game");

        Assert.EndsWith(".mp3", result.PrimaryPath);
        using var reader = new Mp3FileReader(result.PrimaryPath);
        Assert.Equal(48000, reader.WaveFormat.SampleRate);
        Assert.Equal(2, reader.WaveFormat.Channels);
        Assert.Equal(192_000, reader.Mp3WaveFormat.AverageBytesPerSecond * 8);
        Assert.InRange(reader.TotalTime.TotalSeconds, 3.95, 4.1);
        Assert.Equal(TimeSpan.FromSeconds(4), result.Duration);
    }

    [Fact]
    public void Encoding_five_minutes_of_stereo_to_mp3_is_timed()
    {
        // 5:00 of PC audio with a microphone: the full buffer of the app. The numbers go to the test output.
        var random = new Random(7);
        var frames = 48000 * 300;
        var system = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / 48000;
            var value = (float)(0.15 * Math.Sin(2 * Math.PI * 220 * t) + 0.1 * Math.Sin(2 * Math.PI * 1760 * t) + 0.05 * (random.NextDouble() - 0.5));
            system[2 * i] = value;
            system[2 * i + 1] = value * 0.8f;
        }

        var mic = new float[frames];
        for (var i = 0; i < frames; i++) mic[i] = (float)(0.1 * Math.Sin(2 * Math.PI * 330 * i / 48000.0) * (0.5 + 0.5 * Math.Sin(i / 20000.0)));
        var snapshot = new AudioSnapshot(system, mic);

        var mp3 = Stopwatch.StartNew();
        var mp3Result = Saver().Save(snapshot, new ExportOptions { Format = ExportFormat.Mp3, Mp3BitrateKbps = 192, OutputFolder = OutputFolder }, "mp3");
        mp3.Stop();

        var plain = Stopwatch.StartNew();
        Saver().Save(snapshot, new ExportOptions { Format = ExportFormat.Mp3, Mp3BitrateKbps = 192, Normalization = NormalizationMode.Off, OutputFolder = OutputFolder }, "plain");
        plain.Stop();

        var wav = Stopwatch.StartNew();
        var wavResult = Saver().Save(snapshot, Wav(), "wav");
        wav.Stop();

        _output.WriteLine($"5:00 stereo + mic, loudness normalization, MP3 192 kbps: {mp3.Elapsed.TotalSeconds:F1} s, {mp3Result.SizeBytes / 1e6:F1} MB");
        _output.WriteLine($"5:00 stereo + mic, no normalization, MP3 192 kbps: {plain.Elapsed.TotalSeconds:F1} s");
        _output.WriteLine($"5:00 stereo + mic, no normalization, WAV: {wav.Elapsed.TotalSeconds:F1} s, {wavResult.SizeBytes / 1e6:F1} MB");

        using var reader = new Mp3FileReader(mp3Result.PrimaryPath);
        Assert.InRange(reader.TotalTime.TotalSeconds, 299.9, 300.2);
        Assert.Equal(TimeSpan.FromMinutes(5), wavResult.Duration);
    }
}
