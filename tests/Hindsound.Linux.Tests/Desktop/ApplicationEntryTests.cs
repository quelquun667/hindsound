using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The entry of the app in the menu of applications, written at launch from an AppImage or the folder of the archive. The
/// folders are the test's own, so these run everywhere; the entry of the package is the real one of packaging/linux.
/// </summary>
public sealed class ApplicationEntryTests : IDisposable
{
    private const string Program = "/home/u/Apps/Hindsound 0.5.0.AppImage";

    private readonly string _root = Directory.CreateTempSubdirectory("hindsound-entry-").FullName;

    public ApplicationEntryTests()
    {
        // A package laid out as build-packages.sh does: share/applications and share/icons/hicolor/<size>/apps.
        Directory.CreateDirectory(Path.Combine(Share, "applications"));
        File.Copy(PackagedEntryPath, Path.Combine(Share, "applications", ApplicationEntry.FileName));
        foreach (var size in new[] { "48x48", "256x256" })
        {
            Directory.CreateDirectory(Path.Combine(Share, "icons", "hicolor", size, "apps"));
            File.WriteAllBytes(Path.Combine(Share, "icons", "hicolor", size, "apps", ApplicationEntry.IconName), [0x89, (byte)size.Length]);
        }
    }

    private string Share => Path.Combine(_root, "package", "share");

    private string DataHome => Path.Combine(_root, "home", ".local", "share");

    private string SystemData => Path.Combine(_root, "usr", "share");

    private static string PackagedEntryPath
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
            {
                folder = folder.Parent;
            }

            Assert.NotNull(folder);
            return Path.Combine(folder.FullName, "packaging", "linux", ApplicationEntry.FileName);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private ApplicationEntry Entry() => new(DataHome, [SystemData]);

    [Fact]
    public void A_first_launch_writes_the_entry_of_the_package_starting_this_program_and_its_icons()
    {
        var line = Entry().Apply(Share, Program);

        Assert.Equal("desktop entry: written to the menu of applications, with its icons", line);
        var written = File.ReadAllText(Entry().FilePath);
        Assert.Contains("\nExec=\"/home/u/Apps/Hindsound 0.5.0.AppImage\"\n", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Exec=hindsound", written, StringComparison.Ordinal);
        Assert.Equal(Program, AutostartFileProgram(written));

        // Everything else is the package's, line for line.
        var packaged = File.ReadAllText(PackagedEntryPath).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var lines = written.Split('\n');
        Assert.Equal(packaged.Length, lines.Length);
        Assert.Equal(packaged.Where(l => !l.StartsWith("Exec=", StringComparison.Ordinal)), lines.Where(l => !l.StartsWith("Exec=", StringComparison.Ordinal)));

        foreach (var size in new[] { "48x48", "256x256" })
        {
            Assert.Equal([0x89, (byte)size.Length], File.ReadAllBytes(Path.Combine(DataHome, "icons", "hicolor", size, "apps", ApplicationEntry.IconName)));
        }

        Assert.False(File.Exists(Entry().FilePath + ".tmp"));
    }

    [Fact]
    public void A_launch_after_it_finds_the_entry_and_leaves_it()
    {
        Entry().Apply(Share, Program);
        var before = File.GetLastWriteTimeUtc(Entry().FilePath);
        File.Delete(Path.Combine(DataHome, "icons", "hicolor", "48x48", "apps", ApplicationEntry.IconName));

        Assert.Equal(EntryOutcome.AlreadyThere, Entry().Decide(Share, Program));
        Assert.Equal("desktop entry: already in the menu of applications", Entry().Apply(Share, Program));
        Assert.Equal(before, File.GetLastWriteTimeUtc(Entry().FilePath));
        Assert.False(File.Exists(Path.Combine(DataHome, "icons", "hicolor", "48x48", "apps", ApplicationEntry.IconName)));
    }

    [Fact]
    public void An_entry_that_starts_another_program_is_written_again()
    {
        Entry().Apply(Share, "/home/u/Downloads/Hindsound 0.4.0.AppImage");

        Assert.Equal(EntryOutcome.Install, Entry().Decide(Share, Program));
        Entry().Apply(Share, Program);

        Assert.Equal(Program, AutostartFileProgram(File.ReadAllText(Entry().FilePath)));
    }

    [Fact]
    public void An_entry_hidden_by_the_user_is_left_alone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Entry().FilePath)!);
        File.WriteAllText(Entry().FilePath, "[Desktop Entry]\nType=Application\nName=Hindsound\nExec=hindsound\nHidden=true\n");

        Assert.Equal("desktop entry: hidden by the user, left alone", Entry().Apply(Share, Program));
        Assert.Contains("Exec=hindsound\n", File.ReadAllText(Entry().FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void The_entry_of_a_package_of_the_system_is_never_hidden_behind_one_of_the_user()
    {
        Directory.CreateDirectory(Path.Combine(SystemData, "applications"));
        File.WriteAllText(Path.Combine(SystemData, "applications", ApplicationEntry.FileName), "[Desktop Entry]\nExec=/usr/bin/hindsound\n");

        Assert.Equal("desktop entry: a package of the system has one, nothing written", Entry().Apply(Share, Program));
        Assert.False(File.Exists(Entry().FilePath));
        Assert.False(Directory.Exists(Path.Combine(DataHome, "icons")));
        Assert.Equal("[Desktop Entry]\nExec=/usr/bin/hindsound\n", File.ReadAllText(Path.Combine(SystemData, "applications", ApplicationEntry.FileName)));
    }

    [Fact]
    public void A_program_from_no_package_writes_nothing()
    {
        Assert.Equal("desktop entry: not run from an AppImage or the folder of the archive, nothing written", Entry().Apply(null, Program));
        Assert.Equal(EntryOutcome.NotPackaged, Entry().Decide(Share, null));

        // The package's share folder is the user's own: copying it onto itself is never a launch to act on.
        Assert.Equal(EntryOutcome.NotPackaged, new ApplicationEntry(Share, [SystemData]).Decide(Share, Program));
        Assert.False(Directory.Exists(DataHome));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_is_said_in_one_line_and_thrown_by_nothing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        File.WriteAllText(Path.Combine(_root, "home", ".local"), "a file where the folder goes");

        Assert.StartsWith("desktop entry: not written (", Entry().Apply(Share, Program), StringComparison.Ordinal);
    }

    [Fact]
    public void An_appimage_is_found_from_its_mount_and_only_from_there()
    {
        var appDir = Path.Combine(_root, "mount");
        Directory.CreateDirectory(Path.Combine(appDir, "usr", "share", "applications"));
        File.WriteAllText(Path.Combine(appDir, "usr", "share", "applications", ApplicationEntry.FileName), "[Desktop Entry]\n");
        var env = new Dictionary<string, string?> { ["APPIMAGE"] = Program, ["APPDIR"] = appDir };

        Assert.Equal(Path.Combine(appDir, "usr", "share"), EntryRules.PackageShare(name => env.GetValueOrDefault(name), Path.Combine(appDir, "usr", "bin") + Path.DirectorySeparatorChar, File.Exists));

        // The variables reach the programs an AppImage starts: one elsewhere is no part of the image.
        Assert.Null(EntryRules.PackageShare(name => env.GetValueOrDefault(name), Path.Combine(_root, "elsewhere"), File.Exists));

        // A relative APPIMAGE is no image the runtime set.
        env["APPIMAGE"] = "Hindsound.AppImage";
        Assert.Null(EntryRules.PackageShare(name => env.GetValueOrDefault(name), Path.Combine(appDir, "usr", "bin"), File.Exists));
    }

    [Fact]
    public void The_folder_of_the_archive_is_found_by_the_share_next_to_the_program()
    {
        var folder = Path.GetDirectoryName(Share)!;

        Assert.Equal(Share, EntryRules.PackageShare(_ => null, folder + Path.DirectorySeparatorChar, File.Exists));
        Assert.Null(EntryRules.PackageShare(_ => null, Path.Combine(_root, "home", ".local", "bin"), File.Exists));
    }

    [Fact]
    public void The_folders_of_the_system_follow_xdg_data_dirs_or_its_default()
    {
        Assert.Equal(["/usr/local/share/", "/usr/share/"], EntryRules.SystemDataFolders(null));
        Assert.Equal(["/usr/local/share/", "/usr/share/"], EntryRules.SystemDataFolders(""));
        Assert.Equal(["/var/lib/flatpak/exports/share", "/usr/share"], EntryRules.SystemDataFolders("/var/lib/flatpak/exports/share::relative:/usr/share"));
    }

    [Fact]
    public void An_entry_with_no_exec_gets_one_after_its_group()
    {
        Assert.Equal("[Desktop Entry]\nExec=\"/opt/h\"\nName=Hindsound\n", EntryRules.Text("[Desktop Entry]\nName=Hindsound\n", "/opt/h"));
        Assert.Equal("[Desktop Action x]\nExec=other\n[Desktop Entry]\nExec=\"/opt/h\"\n", EntryRules.Text("[Desktop Action x]\nExec=other\n[Desktop Entry]\nExec=hindsound\n", "/opt/h"));
        Assert.Throws<IOException>(() => EntryRules.Text("Name=Hindsound\n", "/opt/h"));
    }

    [Theory]
    [InlineData("[Desktop Entry]\nExec=\"/opt/h\" --save\n", "AlreadyThere")]
    [InlineData("[Desktop Entry]\nExec=hindsound\n", "Install")]
    [InlineData("[Desktop Entry]\nName=Hindsound\n", "Install")]
    [InlineData("[Other]\nExec=\"/opt/h\"\n", "Install")]
    [InlineData("[Desktop Entry]\r\nHidden=true\r\n", "Hidden")]
    [InlineData("[Desktop Entry]\nHidden=false\nExec=/opt/h\n", "AlreadyThere")]
    public void An_entry_of_the_user_is_judged_by_its_program_and_its_hidden_key(string text, string expected) =>
        Assert.Equal(expected, EntryRules.Judge(text, "/opt/h").ToString());

    private static string? AutostartFileProgram(string text) =>
        text.Split('\n').Single(l => l.StartsWith("Exec=", StringComparison.Ordinal)) is var exec ? AutostartFile.ProgramOf(exec["Exec=".Length..]) : null;
}
