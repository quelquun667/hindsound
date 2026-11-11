using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// check-packages.sh names each text of licenses/linux/ it expects in both packages: those of the AppImage runtime, and that
/// of Tmds.DBus.Protocol, which only the Linux app references. Its list must be the whole of licenses/linux/: a text left
/// out of it would no longer be checked, and build-packages.sh could stop shipping it unseen.
/// </summary>
public sealed partial class RuntimeLicenceCheckListTests
{
    [Fact]
    public void The_check_names_every_text_of_the_linux_folder_and_nothing_else()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "packaging", "linux", "check-packages.sh")).Replace("\r\n", "\n");

        var loops = LicenceLoop().Matches(script);
        var loop = Assert.Single(loops);
        var named = loop.Groups[1].Value.Replace("\\\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToList();
        var folder = Directory.GetFiles(Path.Combine(root, "licenses", "linux"), "*.txt").Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(9, folder.Count);
        Assert.Equal(folder, named);
        Assert.Contains("has \"$doc/licenses/linux/$licence\"", loop.Value, StringComparison.Ordinal);
    }

    /// <summary>The loop over the texts of the runtime, its continuation lines included, up to its done.</summary>
    [GeneratedRegex(@"for licence in ((?:[^;\n]|\\\n)+); do\n[^\n]*\n\s*done")]
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
