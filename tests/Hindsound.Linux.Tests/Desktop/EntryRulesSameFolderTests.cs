using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// When two paths are one folder, decided from a resolver given by the test: /home is a link to /var/home on some systems
/// (Fedora Silverblue), and the one folder reached by both names is one folder.
/// </summary>
public sealed class EntryRulesSameFolderTests
{
    private static readonly Dictionary<string, string> Real = new()
    {
        ["/home/ann/.local/share"] = "/var/home/ann/.local/share",
        ["/var/home/ann/.local/share"] = "/var/home/ann/.local/share",
        ["/usr/share"] = "/usr/share",
    };

    private static string? Resolve(string path) => Real.GetValueOrDefault(Path.TrimEndingDirectorySeparator(path));

    [Fact]
    public void One_folder_under_two_names_is_one_folder()
    {
        Assert.True(EntryRules.SameFolder("/home/ann/.local/share", "/var/home/ann/.local/share", Resolve));
        Assert.True(EntryRules.SameFolder("/var/home/ann/.local/share/", "/home/ann/.local/share", Resolve));
    }

    [Fact]
    public void Two_folders_are_two_folders()
    {
        Assert.False(EntryRules.SameFolder("/usr/share", "/var/home/ann/.local/share", Resolve));
    }

    [Fact]
    public void The_same_name_is_the_same_folder_with_no_need_to_resolve_it()
    {
        var asked = 0;

        Assert.True(EntryRules.SameFolder("/usr/share/", "/usr/share", path => { asked++; return null; }));
        Assert.Equal(0, asked);
    }

    [Fact]
    public void A_folder_that_does_not_exist_is_compared_by_its_name_alone()
    {
        Assert.False(EntryRules.SameFolder("/home/ann/missing", "/var/home/ann/.local/share", Resolve));
        Assert.False(EntryRules.SameFolder("/home/ann/.local/share", "/nowhere", Resolve));
        Assert.False(EntryRules.SameFolder("/nowhere", "/elsewhere", _ => null));
    }

    [Fact]
    public void The_data_folder_of_the_user_under_another_name_is_left_out_of_the_system_folders()
    {
        var entry = new ApplicationEntry("/var/home/ann/.local/share", ["/home/ann/.local/share", "/usr/share"], Resolve);

        Assert.Equal(EntryOutcome.NotPackaged, entry.Decide("/home/ann/.local/share", "/opt/hindsound"));
    }
}
