using Hindsound.App;
using Hindsound.Export;

namespace Hindsound.Linux.Tests.Export;

/// <summary>The MP3 of a platform whose encoder is not NAudio.Lame: the portable code hands it the file and the PCM.</summary>
[Collection(PlatformDefaultsCollection.Name)]
public sealed class Mp3EncoderSeamTests
{
    [Fact]
    public void An_mp3_goes_through_the_encoder_of_the_platform_when_there_is_one()
    {
        var folder = Directory.CreateTempSubdirectory("hindsound-mp3-").FullName;
        var before = PlatformDefaults.Mp3Encoder;
        var encoder = new MemoryStream();
        Stream? output = null;
        var asked = (Channels: 0, Kbps: 0);
        PlatformDefaults.Mp3Encoder = (file, channels, kbps) =>
        {
            output = file;
            asked = (channels, kbps);
            return encoder;
        };
        try
        {
            Encoders.WriteMp3(Path.Combine(folder, "clip.mp3"), new float[960], 2, 192);

            Assert.IsType<FileStream>(output);
            Assert.Equal((2, 192), asked);
            Assert.Equal(960 * sizeof(short), encoder.ToArray().Length);
        }
        finally
        {
            PlatformDefaults.Mp3Encoder = before;
            Directory.Delete(folder, recursive: true);
        }
    }
}
