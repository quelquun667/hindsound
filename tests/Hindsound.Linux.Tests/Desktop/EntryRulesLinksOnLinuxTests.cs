using System.Runtime.Versioning;
using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>The real paths of folders reached by symbolic links, as <c>realpath</c> gives them, made in a folder of the test.</summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class EntryRulesLinksOnLinuxTests : IDisposable
{
    private readonly string _root;

    public EntryRulesLinksOnLinuxTests()
    {
        var made = Path.Combine(Path.GetTempPath(), "hindsound-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(made);

        // The temp folder itself may be reached by a link: all is made, and compared, inside its real path.
        _root = EntryRules.RealPath(made)!;
        Directory.CreateDirectory(Path.Combine(_root, "var", "home", "ann", ".local", "share"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "home"), Path.Combine(_root, "var", "home"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "relative"), "var/home/ann");
        Directory.CreateDirectory(Path.Combine(_root, "other"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Under(params string[] parts) => Path.Combine([_root, .. parts]);

    [Fact]
    public void A_link_in_the_middle_of_a_path_is_followed()
    {
        Assert.Equal(Under("var", "home", "ann", ".local", "share"), EntryRules.RealPath(Under("home", "ann", ".local", "share")));
    }

    [Fact]
    public void A_relative_link_is_followed_from_the_folder_that_holds_it()
    {
        Assert.Equal(Under("var", "home", "ann", ".local"), EntryRules.RealPath(Under("relative", ".local")));
    }

    [Fact]
    public void Dot_dot_is_taken_after_the_links_before_it()
    {
        Assert.Equal(Under("var", "home"), EntryRules.RealPath(Under("home", "ann", "..")));
        Assert.Equal(Under("var"), EntryRules.RealPath(Under("home", "..")));
    }

    [Fact]
    public void A_folder_that_is_missing_or_a_path_that_is_relative_has_no_real_path()
    {
        Assert.Null(EntryRules.RealPath(Under("missing")));
        Assert.Null(EntryRules.RealPath("var/home"));
    }

    [Fact]
    public void A_loop_of_links_has_no_real_path()
    {
        Directory.CreateSymbolicLink(Under("loop-a"), Under("loop-b"));
        Directory.CreateSymbolicLink(Under("loop-b"), Under("loop-a"));

        Assert.Null(EntryRules.RealPath(Under("loop-a", "x")));
    }

    [Fact]
    public void One_folder_by_a_link_and_by_its_real_name_is_one_folder()
    {
        Assert.True(EntryRules.SameFolder(Under("home", "ann", ".local", "share"), Under("var", "home", "ann", ".local", "share"), EntryRules.RealPath));
        Assert.False(EntryRules.SameFolder(Under("home", "ann"), Under("other"), EntryRules.RealPath));
    }

    [Fact]
    public void The_data_folder_of_the_user_reached_by_a_link_is_not_a_folder_of_the_system()
    {
        var dataHome = Under("var", "home", "ann", ".local", "share");
        var byLink = Under("home", "ann", ".local", "share");
        Directory.CreateDirectory(Path.Combine(dataHome, "applications"));
        File.WriteAllText(Path.Combine(dataHome, "applications", ApplicationEntry.FileName), "[Desktop Entry]\nExec=/opt/hindsound/hindsound\n");

        var entry = new ApplicationEntry(dataHome, [byLink]);

        Assert.NotEqual(EntryOutcome.SystemHasOne, entry.Decide(Under("other"), "/opt/hindsound/hindsound"));
        Assert.Equal(EntryOutcome.NotPackaged, entry.Decide(byLink, "/opt/hindsound/hindsound"));
    }
}
