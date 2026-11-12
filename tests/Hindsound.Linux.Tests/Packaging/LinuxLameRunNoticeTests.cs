namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The section "On Linux" of LAME in THIRD-PARTY-NOTICES.md says what has run of the Linux build of LAME. The package
/// check of the CI starts the program of both packages with --prove-mp3, which writes an MP3 through the libmp3lame.so.0
/// of the package, so the section must not say that nothing of it has run, and must name that check.
/// </summary>
public sealed class LinuxLameRunNoticeTests
{
    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void The_package_check_writes_an_MP3_with_the_library_of_each_package()
    {
        var check = File.ReadAllText(Path.Combine(Root, "packaging", "linux", "check-packages.sh"));

        Assert.Contains("--prove-mp3", check, StringComparison.Ordinal);
        Assert.Contains("libmp3lame.so.0 was not loaded from the package", check, StringComparison.Ordinal);
    }

    [Fact]
    public void The_LAME_section_on_Linux_does_not_say_that_nothing_of_it_has_run()
    {
        var section = LameOnLinux();

        Assert.DoesNotContain("has run yet", section, StringComparison.Ordinal);
        Assert.DoesNotContain("the first place it will", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_LAME_section_on_Linux_names_the_check_that_ran_it()
    {
        var section = LameOnLinux();

        Assert.Contains("check-packages.sh", section, StringComparison.Ordinal);
        Assert.Contains("--prove-mp3", section, StringComparison.Ordinal);
    }

    private static string LameOnLinux()
    {
        var notices = File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md")).ReplaceLineEndings("\n");
        var lame = notices.IndexOf("\n## LAME\n", StringComparison.Ordinal);
        Assert.True(lame >= 0, "THIRD-PARTY-NOTICES.md has no section LAME");
        var start = notices.IndexOf("\n### On Linux\n", lame, StringComparison.Ordinal);
        Assert.True(start >= 0, "the section LAME of THIRD-PARTY-NOTICES.md has no part On Linux");
        var end = notices.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
        return end < 0 ? notices[start..] : notices[start..end];
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
