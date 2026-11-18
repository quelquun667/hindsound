using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The notices of the code that is compiled into libSDL3.so.0 besides SDL itself. The build turns the X11 driver on, which
/// compiles two files under Red Hat licences that both ask for their notice in copies or in the documentation, so the
/// notices must name them and both Linux packages must carry their text.
/// </summary>
public sealed partial class SdlNoticesTests
{
    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void The_SDL_build_turns_the_X11_driver_on()
    {
        // The two notices below are owed because of this option: if it goes, they can go with it.
        Assert.Contains("-DSDL_X11=ON", Read("tools", "linux", "build-sdl3.sh"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("edid-parse.c", "SDL3-x11-edid-LICENSE.txt", "Copyright 2007 Red Hat, Inc.")]
    [InlineData("xsettings-client.c", "SDL3-x11-xsettings-LICENSE.txt", "Copyright © 2001, 2007 Red Hat, Inc.")]
    public void Each_Red_Hat_file_of_the_X11_driver_has_its_notice_in_the_notices_and_its_text_in_the_repository(string source, string text, string copyright)
    {
        var notices = Read("THIRD-PARTY-NOTICES.md");
        var line = Assert.Single(notices.Split('\n'), l => l.Contains("`src/video/x11/" + source + "`", StringComparison.Ordinal));
        Assert.Contains("](licenses/linux/" + text + ")", line, StringComparison.Ordinal);
        Assert.Contains("Red Hat, Inc.", line, StringComparison.Ordinal);

        var licence = Read("licenses", "linux", text);
        Assert.Contains(copyright, licence, StringComparison.Ordinal);
        Assert.Contains("src/video/x11/" + source, licence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SDL3-x11-edid-LICENSE.txt")]
    [InlineData("SDL3-x11-xsettings-LICENSE.txt")]
    public void The_package_check_looks_for_each_text_of_the_X11_driver(string text)
    {
        var check = Read("packaging", "linux", "check-packages.sh");
        var loop = LicenceLoop().Match(check);

        Assert.True(loop.Success, "check-packages.sh has no loop over the licence texts");
        Assert.Contains(" " + text + " ", " " + Regex.Replace(loop.Groups[1].Value, @"[\s\\]+", " ") + " ", StringComparison.Ordinal);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));

    [GeneratedRegex(@"for licence in ([^;]+); do")]
    private static partial Regex LicenceLoop();

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("Hindsound.slnx is not above the test assembly");
    }
}
