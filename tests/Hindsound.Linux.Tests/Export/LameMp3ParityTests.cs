using Hindsound.App;
using Hindsound.Export;
using Hindsound.Linux.Export;

namespace Hindsound.Linux.Tests.Export;

/// <summary>
/// The MP3 of Linux against the MP3 of Windows. Both are LAME 3.100: given the same PCM, the same settings give the same
/// file, byte for byte. The binding is run here through the DLL of the NAudio.Lame package (same functions, same signatures,
/// another file name), and the file NAudio.Lame writes is the reference. Windows only: NAudio.Lame cannot load its library
/// anywhere else, so on Linux these tests return at once (the tests of LameMp3OnLinuxTests run the real thing there).
/// </summary>
[Collection(PlatformDefaultsCollection.Name)]
public sealed class LameMp3ParityTests
{
    public static TheoryData<int, int> Formats => new()
    {
        { 2, 128 }, { 2, 192 }, { 2, 320 }, { 1, 128 }, { 1, 192 }, { 1, 320 },
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void The_binding_writes_the_file_NAudio_Lame_writes(int channels, int kbps)
    {
        if (WindowsLame.Find() is not { } dll) return;
        using var folder = new TestFolder();
        var signal = TestSignals.Music(channels, seconds: 3);

        var reference = folder.Combine("naudio.mp3");
        WriteWith(null, reference, signal, channels, kbps);
        var ours = folder.Combine("ours.mp3");
        WriteWith(LameLibrary.Load([dll]), ours, signal, channels, kbps);

        var expected = File.ReadAllBytes(reference);
        var actual = File.ReadAllBytes(ours);
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.AsSpan().SequenceEqual(actual), "The two files differ at byte " + FirstDifference(expected, actual) + ".");
    }

    [Fact]
    public void Writes_cut_anywhere_give_the_same_file_as_one_write()
    {
        if (WindowsLame.Find() is not { } dll) return;
        using var folder = new TestFolder();
        var lame = LameLibrary.Load([dll]);
        var pcm = TestSignals.Pcm16(TestSignals.Music(2, seconds: 2));

        var whole = folder.Combine("whole.mp3");
        using (var file = new FileStream(whole, FileMode.Create, FileAccess.ReadWrite))
        using (var encoder = LameMp3Encoder.Open(lame, file, 2, 192))
            encoder.Write(pcm);

        // 4001 bytes: not a whole number of frames, so every write ends in the middle of one.
        var pieces = folder.Combine("pieces.mp3");
        using (var file = new FileStream(pieces, FileMode.Create, FileAccess.ReadWrite))
        using (var encoder = LameMp3Encoder.Open(lame, file, 2, 192))
            for (var offset = 0; offset < pcm.Length; offset += 4001)
                encoder.Write(pcm, offset, Math.Min(4001, pcm.Length - offset));

        Assert.True(File.ReadAllBytes(whole).AsSpan().SequenceEqual(File.ReadAllBytes(pieces)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void The_tone_of_the_Linux_test_is_written_as_NAudio_Lame_writes_it(int channels)
    {
        // The tone the tests on Linux encode and decode (LameMp3OnLinuxTests): proven here to be, byte for byte, a file that
        // NAudio.Lame writes and that the tests of Windows decode.
        if (WindowsLame.Find() is not { } dll) return;
        using var folder = new TestFolder();
        var signal = TestSignals.Sine(channels, 1000, 0.5, seconds: 1);
        WriteWith(null, folder.Combine("naudio.mp3"), signal, channels, 192);
        WriteWith(LameLibrary.Load([dll]), folder.Combine("ours.mp3"), signal, channels, 192);

        Assert.True(File.ReadAllBytes(folder.Combine("naudio.mp3")).AsSpan().SequenceEqual(File.ReadAllBytes(folder.Combine("ours.mp3"))));
    }

    [Fact]
    public void The_version_is_the_one_Windows_ships()
    {
        if (WindowsLame.Find() is not { } dll) return;

        Assert.Equal("3.100", LameLibrary.Load([dll]).Version);
    }

    [Fact]
    public void A_library_that_is_not_there_is_reported_with_every_place_tried()
    {
        var missing = Path.Combine(Path.GetTempPath(), "hindsound-no-such-dir", LameLibrary.SystemName);

        var ex = Assert.Throws<DllNotFoundException>(() => LameLibrary.Load([missing]));

        Assert.Contains(missing, ex.Message);
        Assert.Contains("no such file", ex.Message);
    }

    [Fact]
    public void The_library_is_looked_for_next_to_the_program_first_then_in_the_system()
    {
        var candidates = LameLibrary.Candidates(Path.Combine(Path.GetTempPath(), "app"));

        Assert.Equal([Path.Combine(Path.GetTempPath(), "app", "libmp3lame.so.0"), "libmp3lame.so.0"], candidates);
    }

    [Fact]
    public void An_odd_channel_count_is_refused_before_LAME_is_asked()
    {
        using var output = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => new LameMp3Stream(null!, output, 48000, 3, 192));
    }

    private static void WriteWith(LameLibrary? lame, string path, float[] signal, int channels, int kbps)
    {
        var before = PlatformDefaults.Mp3Encoder;
        PlatformDefaults.Mp3Encoder = lame is null ? null : (file, ch, rate) => LameMp3Encoder.Open(lame, file, ch, rate);
        try
        {
            Encoders.WriteMp3(path, signal, channels, kbps);
        }
        finally
        {
            PlatformDefaults.Mp3Encoder = before;
        }
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) return i;
        return Math.Min(a.Length, b.Length);
    }
}
