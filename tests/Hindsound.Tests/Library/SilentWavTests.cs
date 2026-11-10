using Hindsound.Library;

namespace Hindsound.Tests.Library;

/// <summary>The only file a run off screen may play: pure digital silence, read back before it is played.</summary>
public sealed class SilentWavTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hindsound-silence-" + Guid.NewGuid().ToString("N"));

    public SilentWavTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private string Written(int milliseconds = 500)
    {
        var path = Path.Combine(_folder, "pets.wav");
        SilentWav.Write(path, milliseconds);
        return path;
    }

    [Fact]
    public void What_it_writes_reads_back_as_silence_of_the_length_asked()
    {
        var path = Written(1500);
        Assert.True(SilentWav.IsPureSilence(path));
        Assert.Equal(44 + 48000 * 3 / 2 * 4, new FileInfo(path).Length);
    }

    [Fact]
    public void One_byte_of_sound_anywhere_in_the_data_is_not_silence()
    {
        foreach (var at in new[] { 44, 1000, 44 + 48000 * 2 - 1 })
        {
            var bytes = File.ReadAllBytes(Written());
            bytes[at] = 1;
            Assert.False(SilentWav.IsPureSilence(bytes));
        }
    }

    [Fact]
    public void Anything_else_than_a_plain_pcm_wav_is_not_silence()
    {
        var bytes = File.ReadAllBytes(Written());

        var compressed = (byte[])bytes.Clone();
        compressed[20] = 3;
        Assert.False(SilentWav.IsPureSilence(compressed));

        var cut = bytes[..^10];
        Assert.False(SilentWav.IsPureSilence(cut));

        var notWave = (byte[])bytes.Clone();
        notWave[8] = (byte)'X';
        Assert.False(SilentWav.IsPureSilence(notWave));

        // A chunk of its own could hold a sound another player reads.
        byte[] extra = [.. bytes, .. "LIST"u8, 4, 0, 0, 0, 1, 2, 3, 4];
        Assert.False(SilentWav.IsPureSilence(extra));

        Assert.False(SilentWav.IsPureSilence(bytes[..40]));
        Assert.False(SilentWav.IsPureSilence([]));
    }

    [Fact]
    public void A_file_that_is_missing_too_big_or_an_mp3_is_not_silence()
    {
        Assert.False(SilentWav.IsPureSilence(Path.Combine(_folder, "Sam.wav")));

        var big = Path.Combine(_folder, "Alex.wav");
        SilentWav.Write(big, 30_000);
        Assert.True(new FileInfo(big).Length > SilentWav.MaxBytes);
        Assert.False(SilentWav.IsPureSilence(big));

        var mp3 = Path.Combine(_folder, "dogs.mp3");
        File.WriteAllBytes(mp3, new byte[4096]);
        Assert.False(SilentWav.IsPureSilence(mp3));
    }
}
