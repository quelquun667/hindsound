using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Hindsound.Tests.Export;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The pictures the dump writes are small files, and they hold exactly the pixels that were drawn.</summary>
public sealed unsafe class PngFileTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    // A flat ground with a few lines of text-like noise, a gradient and long runs: every kind of run the writer meets.
    private static uint[] Picture(int width, int height)
    {
        var pixels = new uint[width * height];
        var random = new Random(7);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = 0x101418u;
                if (y % 9 < 3 && x % 5 < 3)
                {
                    value = (uint)random.Next(0, 0x1000000);
                }
                else if (y > height - 6)
                {
                    value = (uint)((x * 255 / width) * 0x010101);
                }

                pixels[y * width + x] = value;
            }
        }

        return pixels;
    }

    private static (int Width, int Height, byte[] Rgb) Read(byte[] file)
    {
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, file[..8]);
        var at = 8;
        int width = 0, height = 0;
        var data = new MemoryStream();
        while (at < file.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
            var body = file.AsSpan(at + 8, length);
            if (type == "IHDR")
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(body);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
            }
            else if (type == "IDAT")
            {
                data.Write(body);
            }

            at += 12 + length;
        }

        data.Position = 0;
        var raw = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionMode.Decompress))
        {
            zlib.CopyTo(raw);
        }

        var rows = raw.ToArray();
        var rowBytes = width * 3 + 1;
        Assert.Equal(rowBytes * height, rows.Length);
        var rgb = new byte[width * 3 * height];
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(1, rows[y * rowBytes]);
            for (var i = 0; i < width * 3; i++)
            {
                var sub = i >= 3 ? rgb[y * width * 3 + i - 3] : 0;
                rgb[y * width * 3 + i] = (byte)(rows[y * rowBytes + 1 + i] + sub);
            }
        }

        return (width, height, rgb);
    }

    [Theory]
    [InlineData(300, 40)]
    [InlineData(1140, 12)]
    [InlineData(1, 1)]
    [InlineData(7, 3)]
    public void The_file_holds_exactly_the_pixels_drawn(int width, int height)
    {
        var pixels = Picture(width, height);
        var path = _temp.Combine("picture.png");
        fixed (uint* bits = pixels)
        {
            PngFile.Write(path, bits, width, height);
        }

        var (readWidth, readHeight, rgb) = Read(File.ReadAllBytes(path));

        Assert.Equal((width, height), (readWidth, readHeight));
        for (var i = 0; i < pixels.Length; i++)
        {
            Assert.Equal(pixels[i] & 0xFFFFFF, (uint)((rgb[i * 3] << 16) | (rgb[i * 3 + 1] << 8) | rgb[i * 3 + 2]));
        }
    }

    [Fact]
    public void A_flat_picture_is_a_small_file_and_not_the_size_of_its_pixels()
    {
        const int width = 1140, height = 732;
        var pixels = new uint[width * height];
        Array.Fill(pixels, 0x101418u);
        var path = _temp.Combine("flat.png");
        fixed (uint* bits = pixels)
        {
            PngFile.Write(path, bits, width, height);
        }

        Assert.True(new FileInfo(path).Length < 40_000, $"{new FileInfo(path).Length} bytes");
        Assert.Equal(0x10, Read(File.ReadAllBytes(path)).Rgb[0]);
    }
}
