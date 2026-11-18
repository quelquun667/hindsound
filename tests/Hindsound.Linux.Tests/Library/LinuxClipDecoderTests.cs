using System.Text;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Linux.Library;
using Hindsound.Linux.Tests.Export;

namespace Hindsound.Linux.Tests.Library;

/// <summary>The signals of the fixtures and of the wav files, rebuilt in code.</summary>
internal static class ClipSignals
{
    public const int Rate = InternalFormat.SampleRate;
    public const double Frequency = 997, Amplitude = 0.5;

    /// <summary>
    /// The sine is at 997 Hz, not 1 kHz: at 48 kHz a 1 kHz sine repeats every 48 frames, and a packet of an mp3 (1152 frames)
    /// or its delays are whole numbers of that period, so a frame misplaced by one of them would pass.
    /// </summary>
    public static float[] Sine(int rate, int channels, double seconds)
    {
        var frames = (int)(seconds * rate);
        var samples = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        {
            var value = (float)(Amplitude * Math.Sin(2 * Math.PI * Frequency * f / rate));
            for (var c = 0; c < channels; c++) samples[f * channels + c] = value;
        }

        return samples;
    }

    /// <summary>The sweep of ChirpStereo48, one channel: from 300 Hz to 6000 Hz over its length, at 48 kHz.</summary>
    public static float[] Chirp(double seconds)
    {
        var frames = (int)(seconds * Rate);
        var samples = new float[frames];
        double f0 = 300, k = 6000 / f0;
        for (var f = 0; f < frames; f++)
        {
            var t = (double)f / Rate;
            samples[f] = (float)(Amplitude * Math.Sin(2 * Math.PI * f0 * seconds / Math.Log(k) * (Math.Pow(k, t / seconds) - 1)));
        }

        return samples;
    }
}

/// <summary>
/// The clips of the library read back by the decoder of the Linux app: the length, the channels, the rate of 48 kHz, the level,
/// the pitch, the frame a cut lands on, and what can't be read. It runs on Windows too: the decoder is portable code.
/// </summary>
public sealed class LinuxClipDecoderTests : IDisposable
{
    private const int Rate = ClipSignals.Rate;
    private const double Amplitude = ClipSignals.Amplitude;

    private readonly TestFolder _folder = new();
    private readonly LinuxClipDecoder _decoder = new();

    public void Dispose() => _folder.Dispose();

    // ---------------------------------------------------------------- an mp3 read whole

    [Fact]
    public void A_stereo_mp3_comes_back_with_its_length_its_level_and_its_pitch()
    {
        using var stream = _decoder.Open(Fixture("sine.mp3", ClipFixtures.SineStereo48));
        var samples = ReadAll(stream);

        Assert.Equal(2, stream.Channels);
        Assert.Equal(TimeSpan.FromSeconds(0.6), stream.Duration);
        Assert.Equal(28800, samples.Length / 2);
        AssertLevelAndPitch(samples, 2, 0.2, 0.4);
    }

    [Fact]
    public void An_mp3_starts_on_its_first_frame_and_ends_on_its_last()
    {
        // A sweep has no repeat to hide a shift: the decoded frames must line up with the signal that was encoded, at the start
        // (the encoder delay and the delay of the decoder) and at the end (the padding).
        using var stream = _decoder.Open(Fixture("chirp.mp3", ClipFixtures.ChirpStereo48));
        var samples = ReadAll(stream);
        var reference = ClipSignals.Chirp(0.8);
        var left = Channel(samples, 2, 0);

        Assert.Equal(reference.Length, left.Length);
        Assert.Equal(0, BestLag(left, reference, from: 3000, to: 20000));
        Assert.Equal(0, BestLag(left, reference, from: reference.Length - 20000, to: reference.Length - 3000));
        Assert.Equal(left, Channel(samples, 2, 1));
    }

    [Fact]
    public void A_mono_mp3_stays_mono_with_its_own_length()
    {
        using var stream = _decoder.Open(Fixture("mono.mp3", ClipFixtures.SineMono48));
        var samples = ReadAll(stream);

        Assert.Equal(1, stream.Channels);
        Assert.Equal(TimeSpan.FromSeconds(0.5), stream.Duration);
        Assert.Equal(24000, samples.Length);
        AssertLevelAndPitch(samples, 1, 0.15, 0.35);
    }

    [Fact]
    public void An_mp3_at_44_1_kHz_is_resampled_to_48_kHz()
    {
        using var stream = _decoder.Open(Fixture("cd.mp3", ClipFixtures.SineMono44));
        var samples = ReadAll(stream);

        Assert.Equal(1, stream.Channels);
        Assert.Equal(TimeSpan.FromSeconds(0.6), stream.Duration);
        Assert.Equal(28800, samples.Length);
        AssertLevelAndPitch(samples, 1, 0.2, 0.4);
    }

    [Fact]
    public void An_mp3_without_its_tag_is_read_at_the_length_the_decoder_finds()
    {
        // The tag frame cut off (384 bytes at 128 kbps and 48 kHz): nothing says where the sound starts and ends.
        var tagged = ClipFixtures.SineStereo48;
        Assert.Equal(0xFF, tagged[384]);
        using var stream = _decoder.Open(Fixture("untagged.mp3", tagged[384..]));
        var samples = ReadAll(stream);

        Assert.Equal(2, stream.Channels);
        AssertNear(TimeSpan.FromSeconds(0.6), stream.Duration, 30);
        Assert.Equal(stream.Duration.TotalSeconds * Rate, samples.Length / 2, 1);
        AssertLevelAndPitch(samples, 2, 0.2, 0.4);
    }

    [Fact]
    public void An_mp3_behind_an_id3_tag_is_read_the_same()
    {
        var plain = ClipFixtures.ChirpStereo48;
        var tag = new byte[10 + 300];
        "ID3"u8.CopyTo(tag);
        tag[3] = 4;
        tag[8] = 300 >> 7;
        tag[9] = 300 & 0x7F;
        using var whole = _decoder.Open(Fixture("plain.mp3", plain));
        using var tagged = _decoder.Open(Fixture("id3.mp3", [.. tag, .. plain]));

        Assert.Equal(whole.Duration, tagged.Duration);
        Assert.Equal(ReadAll(whole), ReadAll(tagged));
    }

    [Fact]
    public void A_read_gives_only_whole_frames_and_nothing_is_lost_between_reads()
    {
        var path = Fixture("odd.mp3", ClipFixtures.SineStereo48);
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

    // ---------------------------------------------------------------- seeking in an mp3

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.01)]
    [InlineData(0.1)]
    [InlineData(0.2345678)]
    [InlineData(0.3)]
    [InlineData(0.55)]
    public void A_seek_in_an_mp3_gives_the_same_frames_as_reading_up_to_there(double at)
    {
        var path = Fixture("seek.mp3", ClipFixtures.ChirpStereo48);
        using var whole = _decoder.Open(path);
        var all = ReadAll(whole);

        using var stream = _decoder.Open(path);
        ReadSome(stream, 20_000); // somewhere else first
        stream.Seek(TimeSpan.FromSeconds(at));
        var after = ReadSome(stream, 2 * 960);

        var from = (int)Math.Round(at * Rate) * 2;
        for (var i = 0; i < after.Length; i++) Assert.Equal(all[from + i], after[i], 1e-4);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.15)]
    [InlineData(0.0001)]
    public void A_seek_near_the_end_of_an_mp3_gives_its_last_frames_and_all_of_them(double back)
    {
        // The shortest selection, at the very end of the clip: the first packet after the seek is then close to the last.
        var path = Fixture("end.mp3", ClipFixtures.ChirpStereo48);
        using var whole = _decoder.Open(path);
        var all = ReadAll(whole);

        using var stream = _decoder.Open(path);
        stream.Seek(TimeSpan.FromSeconds(0.8 - back));
        var tail = ReadAll(stream);

        var from = (int)Math.Round((0.8 - back) * Rate) * 2;
        Assert.Equal(all.Length - from, tail.Length);
        for (var i = 0; i < tail.Length; i++) Assert.Equal(all[from + i], tail[i], 1e-4);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.3)]
    [InlineData(0.5)]
    public void A_seek_in_an_mp3_at_44_1_kHz_gives_the_same_frames_as_reading_up_to_there(double at)
    {
        var path = Fixture("cd-seek.mp3", ClipFixtures.SineMono44);
        using var whole = _decoder.Open(path);
        var all = ReadAll(whole);

        using var stream = _decoder.Open(path);
        stream.Seek(TimeSpan.FromSeconds(at));
        var after = ReadSome(stream, 960);

        // One frame off at 48 kHz would be 0.065 away on this sine.
        var from = (int)Math.Round(at * Rate);
        for (var i = 0; i < after.Length; i++) Assert.Equal(all[from + i], after[i], 1e-3);
    }

    [Fact]
    public void A_seek_past_the_end_reads_nothing_and_a_closed_stream_refuses()
    {
        var stream = _decoder.Open(Fixture("short.mp3", ClipFixtures.SineStereo48));
        stream.Seek(TimeSpan.FromSeconds(10));
        Assert.Equal(0, stream.Read(new float[4096]));
        stream.Seek(TimeSpan.FromSeconds(0.6));
        Assert.Equal(0, stream.Read(new float[4096]));
        stream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.Read(new float[16]));
        Assert.Throws<ObjectDisposedException>(() => stream.Seek(TimeSpan.Zero));
        stream.Dispose(); // twice is fine
    }

    [Fact]
    public void A_seek_back_to_the_start_reads_the_file_again()
    {
        using var stream = _decoder.Open(Fixture("again.mp3", ClipFixtures.SineMono44));
        var first = ReadAll(stream);
        stream.Seek(TimeSpan.Zero);
        var second = ReadAll(stream);

        Assert.Equal(first, second);
    }

    // ---------------------------------------------------------------- an mp3 that can't be read well

    [Fact]
    public void An_mp3_cut_short_gives_the_sound_it_holds()
    {
        var bytes = ClipFixtures.SineStereo48;
        using var stream = _decoder.Open(Fixture("cut.mp3", bytes[..(bytes.Length / 2)]));
        var samples = ReadAll(stream);

        Assert.InRange(samples.Length / 2, 8000, 20000);
        AssertLevelAndPitch(samples, 2, 0.1, 0.25);
    }

    [Fact]
    public void An_mp3_with_a_damaged_middle_ends_cleanly_or_is_refused_with_the_right_exception()
    {
        var bytes = ClipFixtures.ChirpStereo48;
        for (var i = 3000; i < 4500; i++) bytes[i] = (byte)(i * 31);
        var path = Fixture("damaged.mp3", bytes);

        try
        {
            using var stream = _decoder.Open(path);
            var samples = ReadAll(stream);
            Assert.NotEmpty(samples);
        }
        catch (ClipDecodeException)
        {
            // Allowed: a file the decoder can't make sense of.
        }
    }

    [Fact]
    public void A_text_file_named_mp3_is_refused_without_its_path_in_the_message()
    {
        var path = Fixture("notes of someone.mp3", Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("These are not sounds at all. ", 400))));

        var error = Assert.Throws<ClipDecodeException>(() =>
        {
            using var stream = _decoder.Open(path);
            ReadAll(stream);
        });

        Assert.DoesNotContain("someone", error.Message);
        Assert.DoesNotContain(Path.GetDirectoryName(path)!, error.Message);
    }

    [Fact]
    public void An_empty_file_and_a_file_of_another_format_are_refused()
    {
        Assert.Throws<ClipDecodeException>(() => _decoder.Open(Fixture("empty.mp3", [])));
        Assert.Throws<ClipDecodeException>(() => _decoder.Open(Fixture("pic.wav", [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, 0, 0, 0, 0])));
        Assert.Throws<ClipDecodeException>(() => _decoder.Open(Fixture("tiny.mp3", [0xFF, 0xFB, 0x90, 0x00])));
    }

    [Fact]
    public void A_missing_file_and_a_folder_are_refused_without_their_names_in_the_message()
    {
        var gone = Assert.Throws<ClipDecodeException>(() => _decoder.Open(_folder.Combine("gone.mp3")));
        Assert.DoesNotContain("gone", gone.Message);

        var folder = Assert.Throws<ClipDecodeException>(() => _decoder.Open(Path.GetDirectoryName(_folder.Combine("x"))!));
        Assert.DoesNotContain(Path.GetFileName(Path.GetDirectoryName(_folder.Combine("x"))!), folder.Message);
        Assert.Throws<ArgumentNullException>(() => _decoder.Open(null!));
    }

    // ---------------------------------------------------------------- a wav

    [Fact]
    public void A_wav_written_by_the_app_comes_back_the_same()
    {
        var path = _folder.Combine("app.wav");
        var sine = ClipSignals.Sine(Rate, 2, 1);
        Encoders.WriteWav(path, sine, 2);

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(2, stream.Channels);
        Assert.Equal(TimeSpan.FromSeconds(1), stream.Duration);
        Assert.Equal(sine.Length, samples.Length);
        for (var i = 0; i < sine.Length; i++) Assert.Equal(sine[i], samples[i], 1.0 / 32768);
    }

    [Fact]
    public void A_mono_wav_stays_mono()
    {
        var path = WriteWav("mono.wav", Rate, 1, ClipSignals.Sine(Rate, 1, 1));

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(1, stream.Channels);
        Assert.Equal(TimeSpan.FromSeconds(1), stream.Duration);
        Assert.Equal(Rate, samples.Length);
        AssertLevelAndPitch(samples, 1, 0.25, 0.5);
    }

    [Fact]
    public void A_wav_at_44_1_kHz_is_resampled_to_48_kHz()
    {
        var path = WriteWav("cd.wav", 44100, 2, ClipSignals.Sine(44100, 2, 1));

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(2, stream.Channels);
        Assert.Equal(TimeSpan.FromSeconds(1), stream.Duration);
        Assert.Equal(Rate, samples.Length / 2);
        AssertLevelAndPitch(samples, 2, 0.25, 0.5);
    }

    [Fact]
    public void Four_channels_are_mixed_down_to_two()
    {
        // Constant levels: front left 0.4, front right -0.4, the two others 0.2. Each of these goes to both sides at half.
        var frames = Rate / 2;
        var quad = new float[frames * 4];
        for (var f = 0; f < frames; f++)
        {
            quad[4 * f] = 0.4f;
            quad[4 * f + 1] = -0.4f;
            quad[4 * f + 2] = 0.2f;
            quad[4 * f + 3] = 0.2f;
        }

        using var stream = _decoder.Open(WriteWav("quad.wav", Rate, 4, quad));
        var samples = ReadAll(stream);

        Assert.Equal(2, stream.Channels);
        Assert.Equal(frames, samples.Length / 2);
        Assert.Equal(0.3f, samples[1000], 0.002f); // (0.4 + 0.2) / 2
        Assert.Equal(-0.1f, samples[1001], 0.002f); // (-0.4 + 0.2) / 2
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(-32)] // float
    [InlineData(-64)]
    public void Every_sample_format_of_a_wav_gives_the_same_levels(int bits)
    {
        var values = new[] { 0.5f, -0.5f, 0.25f, -1f, 0f };
        var path = WriteWav("format.wav", Rate, 1, values, bits);

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(values.Length, samples.Length);
        for (var i = 0; i < values.Length; i++) Assert.Equal(values[i], samples[i], bits == 8 ? 0.01 : 0.0001);
    }

    [Theory]
    [InlineData(Rate)]
    [InlineData(44100)]
    public void A_seek_in_a_wav_lands_on_the_frame_of_its_time(int rate)
    {
        var path = WriteWav($"seek-{rate}.wav", rate, 2, ClipSignals.Sine(rate, 2, 3));

        using var stream = _decoder.Open(path);
        ReadSome(stream, 20_000); // somewhere else first
        stream.Seek(TimeSpan.FromSeconds(1.5));
        var after = ReadSome(stream, 2 * 480);

        // The first frame read is the one of 1.5 s; one frame off would be 0.065 away on this sine.
        for (var f = 0; f < 480; f++)
            Assert.Equal(Amplitude * Math.Sin(2 * Math.PI * ClipSignals.Frequency * (1.5 + (double)f / Rate)), after[2 * f], 0.01);
    }

    [Fact]
    public void A_wav_that_claims_more_sound_than_it_holds_is_cut_to_the_file()
    {
        // A writer that stopped before it fixed its header: the sizes say 4 GB.
        var path = WriteWav("claim.wav", Rate, 1, ClipSignals.Sine(Rate, 1, 0.5));
        var bytes = File.ReadAllBytes(path);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), uint.MaxValue);
        BitConverter.TryWriteBytes(bytes.AsSpan(40), uint.MaxValue - 100);
        File.WriteAllBytes(path, bytes);

        using var stream = _decoder.Open(path);
        Assert.Equal(Rate / 2, ReadAll(stream).Length);
        Assert.Equal(TimeSpan.FromSeconds(0.5), stream.Duration);
    }

    [Fact]
    public void A_wav_with_other_chunks_before_its_sound_is_read()
    {
        var path = WriteWav("list.wav", Rate, 1, ClipSignals.Sine(Rate, 1, 0.5));
        var bytes = File.ReadAllBytes(path);

        // A LIST chunk of an odd size (5 bytes, then one byte of padding) between the format and the data.
        byte[] list = [.. "LIST"u8, 5, 0, 0, 0, 1, 2, 3, 4, 5, 0];
        File.WriteAllBytes(path, [.. bytes[..36], .. list, .. bytes[36..]]);

        using var stream = _decoder.Open(path);
        var samples = ReadAll(stream);

        Assert.Equal(Rate / 2, samples.Length);
        AssertLevelAndPitch(samples, 1, 0.1, 0.4);
    }

    [Fact]
    public void A_wav_in_a_format_that_is_not_read_is_refused()
    {
        var path = WriteWav("adpcm.wav", Rate, 1, ClipSignals.Sine(Rate, 1, 0.1));
        var bytes = File.ReadAllBytes(path);
        bytes[20] = 2; // Microsoft ADPCM
        File.WriteAllBytes(path, bytes);

        Assert.Throws<ClipDecodeException>(() => _decoder.Open(path));
        Assert.Throws<ClipDecodeException>(() => _decoder.Open(Fixture("headeronly.wav", bytes[..36])));
    }

    [Fact]
    public void The_peaks_of_a_decoded_file_rise_where_the_sound_starts()
    {
        // One second of silence, then two of the sine: the first third of the bars is empty, the rest near -6 dBFS.
        var samples = ClipSignals.Sine(Rate, 1, 3);
        Array.Clear(samples, 0, Rate);

        using var stream = _decoder.Open(WriteWav("late.wav", Rate, 1, samples));
        var bars = EditPeaks.Compute(stream, 30);

        Assert.All(bars.Take(10), bar => Assert.Equal(0f, bar));
        Assert.All(bars.Skip(10), bar => Assert.Equal(ClipPeaks.ToBar(0.5f), bar, 0.01f));
    }

    // ---------------------------------------------------------------- helpers

    private string Fixture(string name, byte[] bytes)
    {
        var path = _folder.Combine(name);
        File.WriteAllBytes(path, bytes);
        return path;
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

    private static float[] Channel(float[] interleaved, int channels, int index) =>
        Enumerable.Range(0, interleaved.Length / channels).Select(f => interleaved[f * channels + index]).ToArray();

    private static void AssertNear(TimeSpan expected, TimeSpan actual, double milliseconds) =>
        Assert.InRange(Math.Abs((actual - expected).TotalMilliseconds), 0, milliseconds);

    // The level between two times within 5 %, and the pitch there: the zero crossings of a 997 Hz sine.
    private static void AssertLevelAndPitch(float[] samples, int channels, double from, double to)
    {
        var peak = 0f;
        var crossings = 0;
        var first = (int)(from * Rate);
        var last = (int)(to * Rate);
        for (var f = first; f < last; f++)
        {
            var value = samples[f * channels];
            peak = Math.Max(peak, Math.Abs(value));
            if (f > first && Math.Sign(value) != Math.Sign(samples[(f - 1) * channels]) && value != 0) crossings++;
        }

        var expected = 2 * ClipSignals.Frequency * (to - from);
        Assert.InRange(peak, Amplitude * 0.95, Amplitude * 1.05);
        Assert.InRange(crossings, expected - 2, expected + 2);
    }

    // The shift, in frames, at which the decoded signal best matches the reference over frames [from, to) of the reference.
    private static int BestLag(float[] decoded, float[] reference, int from, int to)
    {
        var best = 0;
        var bestSum = double.MinValue;
        for (var lag = -1500; lag <= 1500; lag++)
        {
            var sum = 0.0;
            for (var n = from; n < to; n++) sum += (double)decoded[n + lag] * reference[n];
            if (sum > bestSum)
            {
                bestSum = sum;
                best = lag;
            }
        }

        return best;
    }

    /// <summary>A PCM wav at any rate and channel count. <paramref name="bits"/> is negative for float.</summary>
    private string WriteWav(string name, int rate, int channels, float[] interleaved, int bits = 16)
    {
        var width = Math.Abs(bits) / 8;
        var data = new byte[interleaved.Length * width];
        for (var i = 0; i < interleaved.Length; i++)
        {
            var at = i * width;
            var v = interleaved[i];
            switch (bits)
            {
                case 8: data[at] = (byte)Math.Round(v * 127 + 128); break;
                case 16: BitConverter.TryWriteBytes(data.AsSpan(at), (short)Math.Round(v * 32767)); break;
                case 24:
                    var i24 = (int)Math.Round(v * 8388607);
                    data[at] = (byte)i24;
                    data[at + 1] = (byte)(i24 >> 8);
                    data[at + 2] = (byte)(i24 >> 16);
                    break;
                case 32: BitConverter.TryWriteBytes(data.AsSpan(at), (int)Math.Round(v * 2147483647.0)); break;
                case -32: BitConverter.TryWriteBytes(data.AsSpan(at), v); break;
                default: BitConverter.TryWriteBytes(data.AsSpan(at), (double)v); break;
            }
        }

        var path = _folder.Combine(name);
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file, Encoding.ASCII);
        writer.Write("RIFF"u8);
        writer.Write(36 + data.Length);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)(bits < 0 ? 3 : 1));
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * width);
        writer.Write((short)(channels * width));
        writer.Write((short)(width * 8));
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
        return path;
    }
}
