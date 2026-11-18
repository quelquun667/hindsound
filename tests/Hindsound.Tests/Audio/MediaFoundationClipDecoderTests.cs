using System.IO;
using System.Text;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;
using NAudio.Lame;
using NAudio.Wave;

namespace Hindsound.Tests.Audio;

/// <summary>
/// Media Foundation reads back the files this test writes itself, in its own temp folder: the length, the channels, the rate
/// of 48 kHz, the level, and a seek that lands on the exact frame.
/// </summary>
/// <remarks>
/// The sine is at 997 Hz, not 1 kHz: at 48 kHz a 1 kHz sine repeats every 48 frames, and an mp3 packet of 1152 frames, or
/// the 528 frames Media Foundation trims, are whole numbers of that period, so a frame misplaced by one of them would pass.
/// </remarks>
public sealed class MediaFoundationClipDecoderTests : IDisposable
{
    private const int Rate = InternalFormat.SampleRate;
    private const double Seconds = 3, Frequency = 997, Amplitude = 0.5;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));
    private readonly MediaFoundationClipDecoder _decoder = new();

    public MediaFoundationClipDecoderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { /* a file still open: nothing more to do in a test */ }
    }

    // ---------------------------------------------------------------- reading a whole file

    [Fact]
    public void An_mp3_comes_back_in_stereo_at_48_kHz_with_its_length_and_level()
    {
        var path = Path.Combine(_folder, "sine.mp3");
        Encoders.WriteMp3(path, Sine(Rate, 2), 2, 192);

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(2, stream.Channels);
        AssertNear(TimeSpan.FromSeconds(Seconds), stream.Duration);
        AssertNear(TimeSpan.FromSeconds(Seconds), FramesToTime(samples.Length / 2));
        AssertLevelAndPitch(samples, 2);
    }

    [Fact]
    public void An_mp3_comes_back_without_the_silence_of_its_encoder()
    {
        // LAME adds a tag frame, a delay before the sound and padding after it; none of them is part of the clip.
        var path = Path.Combine(_folder, "gapless.mp3");
        Encoders.WriteMp3(path, Sine(Rate, 2), 2, 320);

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(TimeSpan.FromSeconds(Seconds), stream.Duration);
        Assert.Equal((int)(Seconds * Rate), samples.Length / 2);
        AssertSine(samples, 2, 0, 480, 0.02);
    }

    [Fact]
    public void A_mono_mp3_stays_mono_with_its_own_length()
    {
        var path = Path.Combine(_folder, "mono.mp3");
        Encoders.WriteMp3(path, Sine(Rate, 1), 1, 128);

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(1, stream.Channels);
        Assert.Equal(TimeSpan.FromSeconds(Seconds), stream.Duration);
        Assert.Equal((int)(Seconds * Rate), samples.Length);
        AssertSine(samples, 1, 0, 480, 0.03);
    }

    [Fact]
    public void An_mp3_without_its_tag_is_read_at_the_length_the_decoder_finds()
    {
        // The Info frame of LAME cut off (576 bytes at 192 kbps and 48 kHz): nothing says where the sound starts and ends.
        var tagged = Path.Combine(_folder, "tagged.mp3");
        Encoders.WriteMp3(tagged, Sine(Rate, 2), 2, 192);
        var bytes = File.ReadAllBytes(tagged);
        Assert.Equal(0xFF, bytes[576]);
        var path = Path.Combine(_folder, "untagged.mp3");
        File.WriteAllBytes(path, bytes[576..]);

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(2, stream.Channels);
        AssertNear(TimeSpan.FromSeconds(Seconds), stream.Duration);
        AssertNear(TimeSpan.FromSeconds(Seconds), FramesToTime(samples.Length / 2));
        AssertLevelAndPitch(samples, 2);
    }

    [Fact]
    public void A_seek_past_the_end_reads_nothing_and_a_closed_stream_refuses()
    {
        var path = Path.Combine(_folder, "short.wav");
        WriteWav(path, Rate, 2, Sine(Rate, 2));

        var stream = _decoder.Open(path);
        stream.Seek(TimeSpan.FromSeconds(10));
        Assert.Equal(0, stream.Read(new float[4096]));
        stream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.Read(new float[16]));
        Assert.Throws<ObjectDisposedException>(() => stream.Seek(TimeSpan.Zero));
    }

    [Fact]
    public void A_mono_wav_stays_mono()
    {
        var path = Path.Combine(_folder, "mono.wav");
        WriteWav(path, Rate, 1, Sine(Rate, 1));

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(1, stream.Channels);
        AssertNear(TimeSpan.FromSeconds(Seconds), stream.Duration);
        Assert.Equal((int)(Seconds * Rate), samples.Length);
        AssertLevelAndPitch(samples, 1);
    }

    [Fact]
    public void A_wav_at_44_1_kHz_is_resampled_to_48_kHz()
    {
        var path = Path.Combine(_folder, "cd.wav");
        WriteWav(path, 44100, 2, Sine(44100, 2));

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(2, stream.Channels);
        AssertNear(TimeSpan.FromSeconds(Seconds), stream.Duration);
        Assert.Equal((int)(Seconds * Rate), samples.Length / 2);
        AssertLevelAndPitch(samples, 2);
    }

    [Fact]
    public void Four_channels_are_mixed_down_to_two()
    {
        // Constant levels: front left 0.4, front right -0.4, the two others 0.2. Each of these goes to both sides at half.
        var path = Path.Combine(_folder, "quad.wav");
        var frames = Rate / 2;
        var quad = new float[frames * 4];
        for (var f = 0; f < frames; f++)
        {
            quad[4 * f] = 0.4f;
            quad[4 * f + 1] = -0.4f;
            quad[4 * f + 2] = 0.2f;
            quad[4 * f + 3] = 0.2f;
        }

        WriteWav(path, Rate, 4, quad);

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(2, stream.Channels);
        Assert.Equal(frames, samples.Length / 2);
        Assert.Equal(0.3f, samples[1000], 0.002f); // (0.4 + 0.2) / 2
        Assert.Equal(-0.1f, samples[1001], 0.002f); // (-0.4 + 0.2) / 2
    }

    [Fact]
    public void A_read_gives_only_whole_frames_and_nothing_is_lost_between_reads()
    {
        var path = Path.Combine(_folder, "odd.wav");
        WriteWav(path, Rate, 2, Sine(Rate, 2));

        using var whole = _decoder.Open(path);
        var expected = ReadAll(whole);

        using var stream = _decoder.Open(path);
        var got = new List<float>();
        var buffer = new float[777]; // an odd number of floats: the last one of each read stays empty
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            Assert.Equal(0, read % 2);
            got.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        Assert.Equal(expected, got.ToArray());
    }

    // ---------------------------------------------------------------- seeking

    [Theory]
    [InlineData(Rate)]
    [InlineData(44100)]
    public void A_seek_lands_on_the_frame_of_its_time(int rate)
    {
        var path = Path.Combine(_folder, $"seek-{rate}.wav");
        WriteWav(path, rate, 2, Sine(rate, 2));

        using var stream = _decoder.Open(path);
        ReadSome(stream, 20_000); // somewhere else first
        stream.Seek(TimeSpan.FromSeconds(1.5));
        var after = ReadSome(stream, 2 * 480);

        // The first frame read is the one of 1.5 s; one frame off would be 0.065 away on this sine.
        AssertSine(after, 2, 1.5, 480, 0.01);
    }

    [Fact]
    public void A_seek_in_an_mp3_gives_the_same_frames_as_reading_up_to_there()
    {
        var path = Path.Combine(_folder, "seek.mp3");
        Encoders.WriteMp3(path, Sine(Rate, 2), 2, 192);

        using var whole = _decoder.Open(path);
        var all = ReadAll(whole);

        using var stream = _decoder.Open(path);
        stream.Seek(TimeSpan.FromSeconds(1.5));
        var after = ReadSome(stream, 2 * 480);

        var from = (int)(1.5 * Rate) * 2;
        for (var i = 0; i < after.Length; i++) Assert.Equal(all[from + i], after[i], 0.01);
        AssertSine(after, 2, 1.5, 480, 0.02);
    }

    [Theory]
    [InlineData(128, 0.2)]
    [InlineData(128, 0.15)]
    [InlineData(192, 0.2)]
    [InlineData(192, 0.15)]
    public void A_seek_near_the_end_of_an_mp3_gives_its_last_frames_and_all_of_them(int kbps, double back)
    {
        // The shortest selection, at the very end of the clip: the first packet after the seek is then close to the last.
        var path = Path.Combine(_folder, $"end-{kbps}-{back * 100}.mp3");
        Encoders.WriteMp3(path, Sine(Rate, 2), 2, kbps);

        using var whole = _decoder.Open(path);
        var all = ReadAll(whole);

        using var stream = _decoder.Open(path);
        stream.Seek(TimeSpan.FromSeconds(Seconds - back));
        var tail = ReadAll(stream);

        var from = (int)Math.Round((Seconds - back) * Rate) * 2;
        Assert.Equal(all.Length - from, tail.Length);
        for (var i = 0; i < tail.Length; i++) Assert.Equal(all[from + i], tail[i], 0.01);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.9)]
    public void A_seek_in_an_mp3_at_44_1_kHz_gives_the_same_frames_as_reading_up_to_there(double at)
    {
        var path = Path.Combine(_folder, $"cd-{at * 10}.mp3");
        WriteMp3(path, 44100, Sine(44100, 2), 192);

        using var whole = _decoder.Open(path);
        var all = ReadAll(whole);

        using var stream = _decoder.Open(path);
        stream.Seek(TimeSpan.FromSeconds(at));
        var after = ReadSome(stream, 2 * 480);

        // One frame off at 48 kHz would be 0.065 away on this sine.
        var from = (int)Math.Round(at * Rate) * 2;
        for (var i = 0; i < after.Length; i++) Assert.Equal(all[from + i], after[i], 0.01);
    }

    [Fact]
    public void A_seek_back_to_the_start_reads_the_file_again()
    {
        var path = Path.Combine(_folder, "again.wav");
        WriteWav(path, 44100, 1, Sine(44100, 1));

        using var stream = _decoder.Open(path);
        var first = ReadAll(stream);
        stream.Seek(TimeSpan.Zero);
        var second = ReadAll(stream);

        Assert.Equal(first.Length, second.Length);
        Assert.Equal(first, second);
    }

    // ---------------------------------------------------------------- what can't be read

    [Fact]
    public void A_text_file_named_mp3_is_refused_without_its_path_in_the_message()
    {
        var path = Path.Combine(_folder, "notes of someone.mp3");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("These are not sounds at all. ", 400)));

        var error = Assert.Throws<ClipDecodeException>(() =>
        {
            using var stream = _decoder.Open(path);
            ReadAll(stream);
        });

        Assert.DoesNotContain("someone", error.Message);
        Assert.DoesNotContain(_folder, error.Message);
    }

    [Fact]
    public void A_missing_file_is_refused()
    {
        var error = Assert.Throws<ClipDecodeException>(() => _decoder.Open(Path.Combine(_folder, "gone.mp3")));

        Assert.DoesNotContain("gone", error.Message);
    }

    [Fact]
    public void The_peaks_of_a_decoded_file_rise_where_the_sound_starts()
    {
        // One second of silence, then two of the sine: the first third of the bars is empty, the rest near -6 dBFS.
        var path = Path.Combine(_folder, "late.wav");
        var samples = Sine(Rate, 1);
        Array.Clear(samples, 0, Rate);
        WriteWav(path, Rate, 1, samples);

        using var stream = _decoder.Open(path);
        var bars = EditPeaks.Compute(stream, 30);

        Assert.All(bars.Take(10), bar => Assert.Equal(0f, bar));
        Assert.All(bars.Skip(10), bar => Assert.Equal(ClipPeaks.ToBar(0.5f), bar, 0.01f));
    }

    // ---------------------------------------------------------------- helpers

    private static float[] Sine(int rate, int channels)
    {
        var frames = (int)(Seconds * rate);
        var samples = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        {
            var value = (float)(Amplitude * Math.Sin(2 * Math.PI * Frequency * f / rate));
            for (var c = 0; c < channels; c++) samples[f * channels + c] = value;
        }

        return samples;
    }

    private static float[] ReadAll(IDecodedStream stream)
    {
        var all = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = stream.Read(buffer)) > 0) all.AddRange(buffer.AsSpan(0, read).ToArray());
        return all.ToArray();
    }

    private static float[] ReadSome(IDecodedStream stream, int floats)
    {
        var buffer = new float[floats];
        var at = 0;
        int read;
        while (at < floats && (read = stream.Read(buffer.AsSpan(at))) > 0) at += read;
        Assert.Equal(floats, at);
        return buffer;
    }

    // The frames read from the time `at` on are those of the sine written, within the tolerance.
    private static void AssertSine(float[] samples, int channels, double at, int frames, double tolerance)
    {
        for (var f = 0; f < frames; f++)
        {
            var expected = Amplitude * Math.Sin(2 * Math.PI * Frequency * (at + (double)f / Rate));
            for (var c = 0; c < channels; c++) Assert.Equal(expected, samples[f * channels + c], tolerance);
        }
    }

    private static TimeSpan FramesToTime(int frames) => TimeSpan.FromSeconds((double)frames / Rate);

    private static void AssertNear(TimeSpan expected, TimeSpan actual) =>
        Assert.InRange(Math.Abs((actual - expected).TotalMilliseconds), 0, 30);

    // The level of the middle second within 5 %, and the pitch: 1994 zero crossings in that second, at 48 kHz.
    private static void AssertLevelAndPitch(float[] samples, int channels)
    {
        var peak = 0f;
        var crossings = 0;
        for (var f = Rate; f < 2 * Rate; f++)
        {
            var value = samples[f * channels];
            peak = Math.Max(peak, Math.Abs(value));
            if (f > Rate && Math.Sign(value) != Math.Sign(samples[(f - 1) * channels]) && value != 0) crossings++;
        }

        Assert.InRange(peak, Amplitude * 0.95, Amplitude * 1.05);
        Assert.InRange(crossings, 1990, 1998);
    }

    /// <summary>A constant-bitrate mp3 at any rate, with its LAME tag (the app's own writer is fixed at 48 kHz).</summary>
    private static void WriteMp3(string path, int rate, float[] stereo, int kbps)
    {
        var data = new byte[stereo.Length * 2];
        Encoders.ToPcm16(stereo, data);

        // Opened for reading too, so that LAME writes its tag back into the first frame when it closes.
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var writer = new LameMP3FileWriter(file, new WaveFormat(rate, 16, 2), kbps);
        writer.Write(data, 0, data.Length);
    }

    /// <summary>A 16-bit PCM wav at any rate and channel count (the app's own writer is fixed at 48 kHz).</summary>
    private static void WriteWav(string path, int rate, int channels, float[] interleaved)
    {
        var data = new byte[interleaved.Length * 2];
        Encoders.ToPcm16(interleaved, data);
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file, Encoding.ASCII);
        writer.Write("RIFF"u8);
        writer.Write(36 + data.Length);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
    }
}
