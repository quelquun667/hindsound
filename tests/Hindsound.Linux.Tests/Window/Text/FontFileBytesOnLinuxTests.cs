using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Linux.Tests.Window.Text;

/// <summary>
/// A font whose path is not valid UTF-8, as fontconfig may give it (a Linux path is bytes, an old folder may be in Latin-1):
/// opened through the bytes themselves, where a string would have turned the byte into U+FFFD and missed the file.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed unsafe partial class FontFileBytesOnLinuxTests
{
    private const string LibC = "libc.so.6";

    [LibraryImport(LibC, SetLastError = true)]
    private static partial int mkdir(byte* path, uint mode);

    [LibraryImport(LibC, SetLastError = true)]
    private static partial int symlink(byte* target, byte* path);

    [LibraryImport(LibC)]
    private static partial int unlink(byte* path);

    [LibraryImport(LibC)]
    private static partial int rmdir(byte* path);

    // The parts end to end, then the zero that ends a C string.
    private static byte[] Zeroed(params byte[][] parts) => parts.SelectMany(part => part).Append((byte)0).ToArray();

    [Fact]
    public void A_font_under_a_folder_whose_name_is_not_UTF8_opens_through_its_bytes()
    {
        var fonts = Environment.GetEnvironmentVariable(FreeTypeText.FontDirectoryVariable) is { Length: > 0 } folder
            ? folder
            : throw new InvalidOperationException(FreeTypeText.FontDirectoryVariable + " is not set: it names the folder of the app's fonts.");
        var temp = Directory.CreateTempSubdirectory("hindsound-font-");

        // "caf" then 0xE9, é in Latin-1: not valid UTF-8.
        var dir = Zeroed(Encoding.UTF8.GetBytes(temp.FullName + "/caf"), [0xE9]);
        var link = Zeroed(dir.AsSpan(0, dir.Length - 1).ToArray(), "/Selawik-Regular.ttf"u8.ToArray());
        var target = FontFile.PathBytes(Path.Combine(fonts, "Selawik-Regular.ttf"));
        Assert.Equal(0, TextNative.FT_Init_FreeType(out var library));
        try
        {
            fixed (byte* d = dir, l = link, t = target)
            {
                Assert.True(mkdir(d, 0x1C0) == 0, "mkdir: errno " + Marshal.GetLastPInvokeError());
                Assert.True(symlink(t, l) == 0, "symlink: errno " + Marshal.GetLastPInvokeError());
            }

            using (var file = FontFile.Open(library, 0, link, 0))
            {
                Assert.NotNull(file);
                Assert.Equal("Selawik-Regular.ttf", file.Name);
                Assert.True(file.Covers('A'));
            }

            // The same path through a string: the byte is no longer the folder's, and the file is not found.
            var asString = Encoding.UTF8.GetString(link.AsSpan(0, link.Length - 1));
            Assert.Contains('\uFFFD', asString);
            Assert.Null(FontFile.Open(library, 1, asString, 0));
        }
        finally
        {
            fixed (byte* d = dir, l = link)
            {
                unlink(l);
                rmdir(d);
            }

            TextNative.FT_Done_FreeType(library);
            temp.Delete(recursive: true);
        }
    }
}
