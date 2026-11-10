using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The Uninstall section of the README of the tar.gz removes everything the app adds outside its own folders, the
/// autostart entry of "Start with the session" included: left behind, it starts a program that is no longer there at every
/// login.
/// </summary>
public sealed class LinuxReadmeUninstallTests
{
    [Fact]
    public void Uninstall_removes_the_autostart_entry_the_app_writes()
    {
        // AutostartEntry writes into $XDG_CONFIG_HOME/autostart, ~/.config/autostart by default.
        Assert.Contains("    rm -f ~/.config/autostart/" + AutostartEntry.FileName, Uninstall());
    }

    [Fact]
    public void Uninstall_names_both_folders_the_app_leaves_behind()
    {
        var text = string.Join('\n', Uninstall());

        // The settings (ApplicationData) and the log, rescued clips and update state (LocalApplicationData) on Linux.
        Assert.Contains("~/.config/Hindsound", text, StringComparison.Ordinal);
        Assert.Contains("~/.local/share/Hindsound", text, StringComparison.Ordinal);
    }

    /// <summary>The lines of the Uninstall section, up to the next title.</summary>
    private static List<string> Uninstall()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "packaging", "linux", "README.txt")).ToList();
        var start = lines.IndexOf("Uninstall");
        Assert.True(start >= 0, "README.txt has no Uninstall section");
        Assert.Matches("^-+$", lines[start + 1]);

        var section = new List<string>();
        for (var index = start + 2; index < lines.Count; index++)
        {
            if (index + 1 < lines.Count && lines[index + 1].Length > 0 && lines[index + 1].All(c => c == '-'))
            {
                break;
            }

            section.Add(lines[index]);
        }

        return section;
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
