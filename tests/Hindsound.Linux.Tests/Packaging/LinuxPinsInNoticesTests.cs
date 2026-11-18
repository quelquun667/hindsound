using System.Globalization;
using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The notices repeat what build-sdl3.sh and fetch-fonts.sh pin: the SDL version and archive hash, the hash of each font
/// archive, the git blob and size of the Barlow file. A bump of a script that leaves the notices behind would state a hash
/// the packages no longer match, so each pin of a script must be in the notices.
/// </summary>
public sealed partial class LinuxPinsInNoticesTests
{
    private static readonly string Root = RepositoryRoot();

    [Theory]
    [InlineData("build-sdl3.sh", "SDL_SHA256")]
    [InlineData("fetch-fonts.sh", "SELAWIK_SHA256")]
    [InlineData("fetch-fonts.sh", "CASCADIA_SHA256")]
    [InlineData("fetch-fonts.sh", "BARLOW_COMMIT")]
    [InlineData("fetch-fonts.sh", "BARLOW_BLOB")]
    public void The_notices_state_each_pin_of_the_Linux_build_scripts(string script, string name)
    {
        var value = Pin(script, name);

        Assert.Contains("`" + value + "`", Notices(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_notices_state_the_size_of_the_Barlow_file_the_script_checks()
    {
        var size = int.Parse(Pin("fetch-fonts.sh", "BARLOW_SIZE"), CultureInfo.InvariantCulture);

        Assert.Contains(size.ToString("N0", CultureInfo.InvariantCulture) + " bytes", Notices(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_notices_name_the_SDL_version_the_script_builds()
    {
        var version = Pin("build-sdl3.sh", "SDL_VERSION");

        Assert.Contains("SDL " + version, Notices(), StringComparison.Ordinal);
        Assert.Contains("SDL3-" + version + ".tar.gz", Notices(), StringComparison.Ordinal);
        Assert.Contains("release-" + version, Notices(), StringComparison.Ordinal);
    }

    private static string Notices() => File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"));

    /// <summary>The value of <c>readonly NAME='value'</c> (or <c>readonly NAME=value</c>) in a script of tools/linux, which must be there once.</summary>
    private static string Pin(string script, string name)
    {
        var text = File.ReadAllText(Path.Combine(Root, "tools", "linux", script));
        var found = Regex.Matches(text, @"(?m)^readonly " + name + @"='?([^'\r\n]+)'?\r?$");
        Assert.Single(found);
        return found[0].Groups[1].Value;
    }

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
