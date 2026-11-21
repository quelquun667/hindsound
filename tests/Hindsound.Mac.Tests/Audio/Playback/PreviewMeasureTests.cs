using System.Runtime.InteropServices;
using Hindsound.Mac.Audio.Playback;

namespace Hindsound.Mac.Tests.Audio.Playback;

/// <summary>What the proof of the preview measures and how it judges it, on made-up recordings; and the layout of the AudioQueue structures.</summary>
public sealed class PreviewMeasureTests
{
    private const int Rate = 48000;

    [Fact]
    public void A_tone_of_half_scale_reads_minus_nine_rms_minus_six_peak_at_its_frequency()
    {
        var tone = Sine(1000, 0.5f, 0.5);

        Assert.Equal(-9.03, ToneMeasure.LevelDbfs(tone), 1);
        Assert.Equal(-6.02, ToneMeasure.PeakDbfs(tone), 1);
        Assert.Equal(1000, ToneMeasure.Dominant(tone, Rate));
        Assert.Equal(440, ToneMeasure.Dominant(Sine(440, 0.1f, 0.5), Rate));
        Assert.Equal(0, ToneMeasure.ExactZeros(tone.AsSpan(1, 10)));
    }

    [Fact]
    public void Silence_reads_minus_infinity_no_frequency_and_every_sample_zero()
    {
        var silence = new float[4800];

        Assert.True(double.IsNegativeInfinity(ToneMeasure.LevelDbfs(silence)));
        Assert.True(double.IsNegativeInfinity(ToneMeasure.PeakDbfs(silence)));
        Assert.True(double.IsNegativeInfinity(ToneMeasure.LevelDbfs([])));
        Assert.Equal(0, ToneMeasure.Dominant(silence, Rate));
        Assert.Equal(4800, ToneMeasure.ExactZeros(silence));
        Assert.Equal("-inf", ToneMeasure.Db(double.NegativeInfinity));
        Assert.Equal("-6.0", ToneMeasure.Db(-6.02));
    }

    [Fact]
    public void The_sound_heard_is_counted_in_blocks_above_the_threshold()
    {
        float[] samples = [.. Sine(1000, 0.5f, 1.0), .. new float[Rate], .. Sine(1000, 0.001f, 0.5)];

        Assert.Equal(1.0, ToneMeasure.AudibleSeconds(samples, Rate, -40), 2);
    }

    [Fact]
    public void One_channel_is_taken_out_of_interleaved_frames()
    {
        Assert.Equal([1f, 3f, 5f], ToneMeasure.Channel([1, 2, 3, 4, 5, 6], 2, 0));
        Assert.Equal([2f, 4f, 6f], ToneMeasure.Channel([1, 2, 3, 4, 5, 6], 2, 1));
    }

    [Fact]
    public void The_tone_of_the_proof_is_two_seconds_of_stereo_at_minus_six_peak()
    {
        var tone = MacPreviewProof.Tone();

        Assert.Equal(2 * Rate * 2, tone.Length);
        var left = ToneMeasure.Channel(tone, 2, 0);
        Assert.Equal(left, ToneMeasure.Channel(tone, 2, 1));
        Assert.Equal(-6.02, ToneMeasure.PeakDbfs(left), 1);
        Assert.Equal(1000, ToneMeasure.Dominant(left, Rate));
    }

    [Fact]
    public void The_option_names_the_folder()
    {
        Assert.Equal("/tmp/out", MacPreviewProof.Folder(["--settings", "s.json", "--prove-preview", "/tmp/out"]));
        Assert.Null(MacPreviewProof.Folder(["--prove-preview"]));
        Assert.Null(MacPreviewProof.Folder(["--prove-loop"]));
    }

    [Fact]
    public void A_recording_of_a_stop_that_cuts_and_a_play_that_goes_on_passes()
    {
        var (heard, marks) = Recording(afterStop: 0, secondPart: 1.2);

        var (pass, lines) = MacPreviewProof.Judge(heard, 2, Rate, marks);

        Assert.True(pass, string.Join("\n", lines));
        Assert.Contains(lines, line => line.StartsWith("played:", StringComparison.Ordinal) && line.Contains("dominant 1000 Hz", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("after the stop:", StringComparison.Ordinal) && line.Contains("exact zeros 24000/24000", StringComparison.Ordinal));
    }

    [Fact]
    public void Sound_after_the_stop_fails()
    {
        var (heard, marks) = Recording(afterStop: 0.01f, secondPart: 1.2);

        Assert.False(MacPreviewProof.Judge(heard, 2, Rate, marks).Pass);
    }

    [Fact]
    public void A_play_that_starts_the_file_over_after_a_stop_fails()
    {
        var (heard, marks) = Recording(afterStop: 0, secondPart: 2.0);

        Assert.False(MacPreviewProof.Judge(heard, 2, Rate, marks).Pass);
    }

    [Fact]
    public void Another_frequency_fails()
    {
        var (heard, marks) = Recording(afterStop: 0, secondPart: 1.2, hz: 440);

        Assert.False(MacPreviewProof.Judge(heard, 2, Rate, marks).Pass);
    }

    [Fact]
    public void Nothing_heard_fails()
    {
        var heard = new float[Rate * 4 * 2];

        Assert.False(MacPreviewProof.Judge(heard, 2, Rate, new PreviewMarks(Rate / 3, Rate, 2 * Rate, 3 * Rate)).Pass);
    }

    [Fact]
    public void The_structures_of_AudioQueue_have_the_layout_of_Core_Audio_on_64_bits()
    {
        Assert.Equal(40, Marshal.SizeOf<AudioStreamBasicDescription>());
        Assert.Equal(56, Marshal.SizeOf<AudioQueueBuffer>());
        Assert.Equal(8, (int)Marshal.OffsetOf<AudioQueueBuffer>(nameof(AudioQueueBuffer.AudioData)));
        Assert.Equal(16, (int)Marshal.OffsetOf<AudioQueueBuffer>(nameof(AudioQueueBuffer.AudioDataByteSize)));
        Assert.Equal(48, (int)Marshal.OffsetOf<AudioQueueBuffer>(nameof(AudioQueueBuffer.PacketDescriptionCount)));

        var format = AudioStreamBasicDescription.Float(48000, 2);
        Assert.Equal(48000, format.SampleRate);
        Assert.Equal(0x6C70636Du, format.FormatId);
        Assert.Equal(9u, format.FormatFlags);
        Assert.Equal(8u, format.BytesPerFrame);
        Assert.Equal(8u, format.BytesPerPacket);
        Assert.Equal(1u, format.FramesPerPacket);
        Assert.Equal(2u, format.ChannelsPerFrame);
        Assert.Equal(32u, format.BitsPerChannel);
    }

    [Fact]
    public void A_status_of_Core_Audio_reads_as_four_characters_when_it_is_one()
    {
        Assert.Equal("'fmt?'", AudioQueueNative.FourCharacters(0x666D743F));
        Assert.Equal("-50", AudioQueueNative.FourCharacters(-50));
        var error = Assert.Throws<IOException>(() => AudioQueueNative.Check(-50, "AudioQueueStart"));
        Assert.Contains("AudioQueueStart", error.Message, StringComparison.Ordinal);
        AudioQueueNative.Check(0, "AudioQueueStart");
    }

    // 0.3 s of silence, 0.8 s of the tone, a stop of 0.7 s, then the rest of the tone and 0.5 s of silence; stereo.
    private static (float[] Heard, PreviewMarks Marks) Recording(float afterStop, double secondPart, double hz = 1000)
    {
        float[] mono =
        [
            .. new float[(int)(0.3 * Rate)],
            .. Sine(hz, 0.5f, 0.8),
            .. Enumerable.Repeat(afterStop, (int)(0.7 * Rate)),
            .. Sine(hz, 0.5f, secondPart),
            .. new float[(int)(0.5 * Rate)],
        ];
        var heard = new float[mono.Length * 2];
        for (var i = 0; i < mono.Length; i++)
        {
            heard[2 * i] = mono[i];
            heard[(2 * i) + 1] = mono[i];
        }

        var play = (long)(0.3 * Rate);
        var stop = play + (long)(0.8 * Rate);
        var resume = stop + (long)(0.7 * Rate);
        return (heard, new PreviewMarks(play, stop, resume, resume + (long)(secondPart * Rate)));
    }

    private static float[] Sine(double hz, float amplitude, double seconds)
    {
        var samples = new float[(int)(seconds * Rate)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate));
        }

        return samples;
    }
}
