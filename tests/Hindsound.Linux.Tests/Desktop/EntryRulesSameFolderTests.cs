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

    // Paths that are full on every system, made in a folder that does not exist: nothing here reads a disk but for a file that is not there.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "hindsound-entry-" + Guid.NewGuid().ToString("N"));

    private static string Folder(params string[] parts) => Path.Combine([Root, .. parts]);

    private static Func<string, string?> Links(params (string Name, string Real)[] links)
    {
        var map = links.ToDictionary(link => Path.TrimEndingDirectorySeparator(link.Name), link => link.Real);
        return path => map.GetValueOrDefault(Path.TrimEndingDirectorySeparator(path));
    }

    [Fact]
    public void The_data_folder_of_the_user_under_another_name_is_left_out_of_the_system_folders()
    {
        var dataHome = Folder("var", "home", "ann", "share");
        var byLink = Folder("home", "ann", "share");
        var system = Folder("usr", "share");
        var resolve = Links((dataHome, dataHome), (byLink, dataHome), (system, system));

        var entry = new ApplicationEntry(dataHome, [byLink, system], resolve);

        Assert.Equal([system], entry.SystemData);
    }

    [Fact]
    public void The_system_folders_are_all_kept_when_none_is_the_data_folder_of_the_user()
    {
        var dataHome = Folder("var", "home", "ann", "share");
        var one = Folder("usr", "local", "share");
        var two = Folder("usr", "share");

        var entry = new ApplicationEntry(dataHome, [one, two], Links((dataHome, dataHome), (one, one), (two, two)));

        Assert.Equal([one, two], entry.SystemData);
    }

    [Fact]
    public void A_program_whose_package_is_the_data_folder_of_the_user_under_another_name_is_not_packaged()
    {
        var dataHome = Folder("var", "home", "ann", "share");
        var byLink = Folder("home", "ann", "share");
        var entry = new ApplicationEntry(dataHome, [], Links((dataHome, dataHome), (byLink, dataHome)));

        Assert.Equal(EntryOutcome.NotPackaged, entry.Decide(byLink, "/opt/hindsound"));
    }

    [Fact]
    public void A_package_that_is_another_folder_than_the_data_folder_of_the_user_is_packaged()
    {
        var dataHome = Folder("var", "home", "ann", "share");
        var package = Folder("app", "usr", "share");
        var entry = new ApplicationEntry(dataHome, [], Links((dataHome, dataHome), (package, package)));

        Assert.Equal(EntryOutcome.Install, entry.Decide(package, "/opt/hindsound"));
    }

    [Fact]
    public void The_share_folder_of_an_AppImage_is_found_by_its_real_path_when_its_name_differs_from_the_program_folder()
    {
        // The runtime mounts under /tmp, which is a link to /tmplink on this system: APPDIR is named by the link, the program by its real name.
        var environment = new Dictionary<string, string> { ["APPIMAGE"] = "/home/ann/Hindsound.AppImage", ["APPDIR"] = "/tmplink/.mount_x" };
        var resolve = Links(("/tmplink/.mount_x/usr/bin", "/tmp/.mount_x/usr/bin"), ("/tmp/.mount_x/usr/bin", "/tmp/.mount_x/usr/bin"));
        var entryFile = "/tmplink/.mount_x/usr/share/applications/" + ApplicationEntry.FileName;

        var share = EntryRules.PackageShare(name => environment.GetValueOrDefault(name), "/tmp/.mount_x/usr/bin", path => path.Replace(Path.DirectorySeparatorChar, '/') == entryFile, path => resolve(path.Replace(Path.DirectorySeparatorChar, '/')));

        Assert.Equal("/tmplink/.mount_x/usr/share", share?.Replace(Path.DirectorySeparatorChar, '/'));
    }

    [Fact]
    public void The_share_folder_of_an_AppImage_is_not_found_when_the_program_runs_from_another_folder()
    {
        var environment = new Dictionary<string, string> { ["APPIMAGE"] = "/home/ann/Hindsound.AppImage", ["APPDIR"] = "/tmplink/.mount_x" };
        var resolve = Links(("/tmplink/.mount_x/usr/bin", "/tmp/.mount_x/usr/bin"), ("/opt/other/bin", "/opt/other/bin"));

        var share = EntryRules.PackageShare(name => environment.GetValueOrDefault(name), "/opt/other/bin", _ => true, path => resolve(path.Replace(Path.DirectorySeparatorChar, '/')));

        Assert.Null(share);
    }
}
