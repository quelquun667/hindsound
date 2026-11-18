using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// What the log says about the window: where SDL and the fonts came from, which the check of the packages reads, and what
/// was missing. Never a path, which would carry the user's name.
/// </summary>
public sealed class LinuxWindowSupportTests
{
    [Fact]
    public void A_package_says_it_drew_with_its_own_sdl_and_its_own_fonts()
    {
        Assert.Equal(
            "window: SDL next to the program, fonts next to the program",
            LinuxWindowSupport.FoundLine("/home/you/Applications/hindsound/libSDL3.so.0", null));
        Assert.Equal(
            "window: SDL next to the program, fonts next to the program",
            LinuxWindowSupport.FoundLine("/tmp/.mount_HindsoXyz/usr/bin/libSDL3.so.0", ""));
    }

    [Fact]
    public void A_build_says_its_sdl_came_from_the_system_and_its_fonts_from_the_variable_without_naming_them()
    {
        var line = LinuxWindowSupport.FoundLine("libSDL3.so.0", "/home/you/work/fonts");

        Assert.Equal("window: SDL from the system, fonts from HINDSOUND_FONT_DIR", line);
        Assert.DoesNotContain("/", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "window: none, the libSDL3.so.0 next to the program cannot be loaded, nor one of the system")]
    [InlineData(false, "window: none, libSDL3.so.0 is not next to the program and cannot be loaded from the system")]
    public void A_system_without_sdl_says_whether_the_copy_of_the_app_was_there(bool copy, string expected)
    {
        Assert.Equal(expected, LinuxWindowSupport.NoSdlLine(copy));
    }
}
