namespace Hindsound.Ui.Window;

/// <summary>A PNG written by hand, lightly compressed: a picture for checking the drawing is not worth a library in the exe, and the README must not weigh megabytes.</summary>
internal static unsafe class PngFile
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, uint* bgra, int width, int height)
    {
        var rowBytes = width * 3 + 1;
        var raw = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var row = bgra + y * width;
            var at = y * rowBytes + 1;
            raw[at - 1] = 1;
            for (var x = 0; x < width; x++, at += 3)
            {
                var value = row[x];
                raw[at] = (byte)(value >> 16);
                raw[at + 1] = (byte)(value >> 8);
                raw[at + 2] = (byte)value;
            }
        }

        // The Sub filter, from the right so that each byte still sees the unfiltered one before it.
        for (var y = 0; y < height; y++)
        {
            var first = y * rowBytes + 1;
            for (var at = y * rowBytes + rowBytes - 1; at >= first + 3; at--)
            {
                raw[at] -= raw[at - 3];
            }
        }

        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 2;
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", Compressed(raw));
        Chunk(file, "IEND", []);
    }

    // Zlib with one block of fixed codes, whose only matches are runs of one byte. After the Sub filter the flat areas of a window
    // are runs of zeros, which is nearly all of the saving: a picture of 1140 x 732 goes from 2.5 MB to well under 100 KB.
    internal static byte[] Compressed(byte[] raw)
    {
        var bits = new List<byte> { 0x78, 0x01 };
        uint pending = 0;
        var count = 0;

        void Put(uint value, int length)
        {
            pending |= value << count;
            count += length;
            while (count >= 8)
            {
                bits.Add((byte)pending);
                pending >>= 8;
                count -= 8;
            }
        }

        // The codes of Huffman are written from their first bit.
        void Code(uint code, int length)
        {
            uint reversed = 0;
            for (var i = 0; i < length; i++)
            {
                reversed = (reversed << 1) | ((code >> i) & 1);
            }

            Put(reversed, length);
        }

        void Symbol(int symbol)
        {
            if (symbol < 144)
            {
                Code((uint)(0x30 + symbol), 8);
            }
            else if (symbol < 256)
            {
                Code((uint)(0x190 + symbol - 144), 9);
            }
            else if (symbol < 280)
            {
                Code((uint)(symbol - 256), 7);
            }
            else
            {
                Code((uint)(0xC0 + symbol - 280), 8);
            }
        }

        void Run(int length)
        {
            var code = 28;
            while (RunBase[code] > length)
            {
                code--;
            }

            Symbol(257 + code);
            Put((uint)(length - RunBase[code]), RunExtra[code]);

            // Distance 1: the byte just written.
            Code(0, 5);
        }

        Put(1, 1);
        Put(1, 2);
        var at = 0;
        while (at < raw.Length)
        {
            var value = raw[at++];
            Symbol(value);
            var same = 0;
            while (at + same < raw.Length && raw[at + same] == value)
            {
                same++;
            }

            while (same >= 3)
            {
                var length = Math.Min(same, 258);
                Run(length);
                at += length;
                same -= length;
            }
        }

        Symbol(256);
        if (count > 0)
        {
            bits.Add((byte)pending);
        }

        uint a = 1, b = 0;
        foreach (var value in raw)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        var output = bits.ToArray();
        Array.Resize(ref output, output.Length + 4);
        WriteBigEndian(output, output.Length - 4, (b << 16) | a);
        return output;
    }

    private static readonly int[] RunBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];

    private static readonly int[] RunExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];

    private static void Chunk(Stream file, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        file.Write(length);
        var name = System.Text.Encoding.ASCII.GetBytes(type);
        file.Write(name);
        file.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var value in name)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        var end = new byte[4];
        WriteBigEndian(end, 0, crc ^ 0xFFFFFFFFu);
        file.Write(end);
    }

    private static void WriteBigEndian(byte[] target, int at, uint value)
    {
        target[at] = (byte)(value >> 24);
        target[at + 1] = (byte)(value >> 16);
        target[at + 2] = (byte)(value >> 8);
        target[at + 3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
