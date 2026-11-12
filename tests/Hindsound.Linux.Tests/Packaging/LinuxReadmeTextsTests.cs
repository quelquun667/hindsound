using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// What the README of the repository and the README of the tar.gz tell a Linux user. Both are read long after they are
/// written: the repository's from the page of any tag, the tar.gz's from a folder unpacked months later.
/// </summary>
public sealed class LinuxReadmeTextsTests
{
    /// <summary>
    /// The labels of the Keyboard panel of GNOME Settings 46, the one of Ubuntu 24.04: the row "View and Customize
    /// Shortcuts" under the group "Keyboard Shortcuts" opens the dialog whose last section is "Custom Shortcuts"
    /// (panels/keyboard/cc-keyboard-panel.ui and cc-keyboard-shortcut-dialog.c of gnome-control-center 46.0).
    /// </summary>
    private const string CustomShortcutsPath = "Settings > Keyboard > View and Customize Shortcuts > Custom Shortcuts";

    [Fact]
    public void The_readme_of_the_tar_gz_gives_the_path_to_custom_shortcuts_of_the_settings_of_ubuntu_24_04()
    {
        Assert.Equal(CustomShortcutsPath, PathToCustomShortcuts(Text("packaging", "linux", "README.txt")));
    }

    [Fact]
    public void The_readme_of_the_repository_gives_the_path_to_custom_shortcuts_of_the_settings_of_ubuntu_24_04()
    {
        Assert.Equal(CustomShortcutsPath, PathToCustomShortcuts(Text("README.md")));
    }

    [Fact]
    public void The_readme_of_the_repository_does_not_announce_the_linux_version_as_still_to_come()
    {
        // The page of a tag shows the README as it was at that tag: a release that carries the Linux packages must not
        // say they are yet to come, and nothing in the release chain reads the README to catch it.
        Assert.DoesNotContain("on its way", Text("README.md"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The menu path that ends at Custom Shortcuts, read across a line break.</summary>
    private static string PathToCustomShortcuts(string text)
    {
        var match = Regex.Match(text, @"Settings > Keyboard > [^,:]*?Custom Shortcuts");
        Assert.True(match.Success, "no path to Custom Shortcuts");
        return match.Value;
    }

    /// <summary>The file with every run of white space, line breaks included, made one space.</summary>
    private static string Text(params string[] path)
    {
        return Regex.Replace(File.ReadAllText(Path.Combine([RepositoryRoot(), .. path])), @"\s+", " ");
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
