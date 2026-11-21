using System.Security.Cryptography;
using System.Text;
using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The entry an AppImage or the archive wrote in the user's menu, once the .deb is installed: under the same name, it goes
/// before the one of the package, and the menu would keep starting the old copy. Any launch removes it, and the
/// icons the old copy put there, when they are the app's and nobody changed them. The folders are the test's own.
/// </summary>
public sealed class ApplicationEntryLeftBehindTests : IDisposable
{
    private const string AppImage = "/home/u/Apps/Hindsound 0.8.0.AppImage";

    private const string PackageProgram = "/usr/lib/hindsound/hindsound";

    private readonly string _root = Directory.CreateTempSubdirectory("hindsound-left-").FullName;

    public ApplicationEntryLeftBehindTests()
    {
        // The AppImage and the .deb carry the same entry and the same icons, from packaging/linux.
        foreach (var share in new[] { Share, SystemData })
        {
            Directory.CreateDirectory(Path.Combine(share, "applications"));
            File.Copy(PackagedEntryPath, Path.Combine(share, "applications", ApplicationEntry.FileName));
            foreach (var size in new[] { "48x48", "256x256" })
            {
                Directory.CreateDirectory(Path.Combine(share, "icons", "hicolor", size, "apps"));
                File.WriteAllBytes(IconIn(share, size), [0x89, (byte)size.Length]);
            }
        }
    }

    private string Share => Path.Combine(_root, "mount", "usr", "share");

    private string DataHome => Path.Combine(_root, "home", ".local", "share");

    private string SystemData => Path.Combine(_root, "usr", "share");

    private string SystemEntry => Path.Combine(SystemData, "applications", ApplicationEntry.FileName);

    private static string PackagedEntryPath => Path.Combine(RepositoryRoot(), "packaging", "linux", ApplicationEntry.FileName);

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

    private static string IconIn(string share, string size) => Path.Combine(share, "icons", "hicolor", size, "apps", ApplicationEntry.IconName);

    private ApplicationEntry Entry() => new(DataHome, [SystemData]);

    /// <summary>What the AppImage wrote at its first launch, before the package was there.</summary>
    private void FirstLaunchOfTheAppImage() =>
        Assert.Equal("desktop entry: written to the menu of applications, with its icons", new ApplicationEntry(DataHome, []).Apply(Share, AppImage));

    private void WriteUserEntry(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Entry().FilePath)!);
        File.WriteAllText(Entry().FilePath, text);
    }

    [Fact]
    public void The_copy_of_the_package_removes_the_entry_of_the_appimage_and_the_icons_equal_to_its_own()
    {
        FirstLaunchOfTheAppImage();

        // The package's 256x256 icon is another one, and a size the package lacks stays too.
        File.WriteAllBytes(IconIn(SystemData, "256x256"), [0x89, 0x01, 0x02]);
        Directory.CreateDirectory(Path.Combine(DataHome, "icons", "hicolor", "64x64", "apps"));
        File.WriteAllBytes(IconIn(DataHome, "64x64"), [0x89, 0x05]);

        var line = Entry().RemoveLeftBehind(PackageProgram);

        Assert.Equal("desktop entry: removed the one an earlier launch wrote, as a package of the system has one, with 1 icon", line);
        Assert.DoesNotContain(_root, line, StringComparison.Ordinal);
        Assert.False(File.Exists(Entry().FilePath));
        Assert.False(File.Exists(IconIn(DataHome, "48x48")));
        Assert.Equal([0x89, (byte)"256x256".Length], File.ReadAllBytes(IconIn(DataHome, "256x256")));
        Assert.Equal([0x89, 0x05], File.ReadAllBytes(IconIn(DataHome, "64x64")));

        // The package's own files are left as they were, and the next launch finds nothing more to remove.
        Assert.Equal(File.ReadAllBytes(PackagedEntryPath), File.ReadAllBytes(SystemEntry));
        Assert.True(File.Exists(IconIn(SystemData, "48x48")));
        Assert.Null(Entry().RemoveLeftBehind(PackageProgram));
    }

    [Fact]
    public void The_line_counts_every_icon_removed()
    {
        FirstLaunchOfTheAppImage();

        Assert.EndsWith(", with 2 icons", Entry().RemoveLeftBehind(PackageProgram), StringComparison.Ordinal);
        Assert.False(File.Exists(IconIn(DataHome, "256x256")));
    }

    [Fact]
    public void Another_appimage_removes_it_too_and_still_writes_nothing()
    {
        FirstLaunchOfTheAppImage();
        const string other = "/home/u/Downloads/Hindsound 0.9.0.AppImage";

        Assert.NotNull(Entry().RemoveLeftBehind(other));
        Assert.Equal("desktop entry: a package of the system has one, nothing written", Entry().Apply(Share, other));
        Assert.False(File.Exists(Entry().FilePath));
    }

    [Fact]
    public void The_appimage_run_after_the_package_removes_its_own_entry_and_writes_nothing()
    {
        // The menu still starts the AppImage once: that launch takes its entry away, and the next ones start the package.
        FirstLaunchOfTheAppImage();

        Assert.NotNull(Entry().RemoveLeftBehind(AppImage));
        Assert.Equal("desktop entry: a package of the system has one, nothing written", Entry().Apply(Share, AppImage));
        Assert.False(File.Exists(Entry().FilePath));
        Assert.False(File.Exists(IconIn(DataHome, "48x48")));
        Assert.Equal(File.ReadAllBytes(PackagedEntryPath), File.ReadAllBytes(SystemEntry));
    }

    [Theory]
    [InlineData("Hidden=true")]
    [InlineData("NoDisplay=true")]
    public void An_entry_the_user_hid_is_never_removed(string key)
    {
        // A package whose own entry has the key: the app's entry then has it too, and only the user's choice keeps it.
        var system = File.ReadAllText(PackagedEntryPath).Replace("\r\n", "\n", StringComparison.Ordinal) + key + "\n";
        File.WriteAllText(SystemEntry, system);
        WriteUserEntry(EntryRules.Text(system, AppImage));

        Assert.Null(Entry().RemoveLeftBehind(PackageProgram));
        Assert.True(File.Exists(Entry().FilePath));
    }

    [Theory]
    [InlineData("Comment=Mine\n")]
    [InlineData("[Desktop Action Save]\nExec=hindsound --save\n")]
    [InlineData("\n")]
    public void An_entry_the_user_changed_is_never_removed(string added)
    {
        FirstLaunchOfTheAppImage();
        File.AppendAllText(Entry().FilePath, added);

        Assert.Null(Entry().RemoveLeftBehind(PackageProgram));
        Assert.True(File.Exists(Entry().FilePath));
        Assert.True(File.Exists(IconIn(DataHome, "48x48")));
    }

    [Theory]
    [InlineData("[Desktop Entry]\nType=Application\nName=Hindsound\nExec=/opt/hindsound/hindsound\n")]
    [InlineData("[Desktop Entry]\nName=Hindsound\n")]
    public void An_entry_another_tool_wrote_is_never_removed(string text)
    {
        WriteUserEntry(text);

        Assert.Null(Entry().RemoveLeftBehind(PackageProgram));
        Assert.Equal(text, File.ReadAllText(Entry().FilePath));
    }

    [Fact]
    public void An_entry_in_windows_line_ends_is_one_changed_after_the_app()
    {
        FirstLaunchOfTheAppImage();
        File.WriteAllText(Entry().FilePath, File.ReadAllText(Entry().FilePath).Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Null(Entry().RemoveLeftBehind(PackageProgram));
        Assert.True(File.Exists(Entry().FilePath));
    }

    [Fact]
    public void With_no_entry_of_the_system_nothing_is_removed()
    {
        FirstLaunchOfTheAppImage();
        File.Delete(SystemEntry);

        Assert.Null(Entry().RemoveLeftBehind(PackageProgram));
        Assert.True(File.Exists(Entry().FilePath));
        Assert.Null(new ApplicationEntry(DataHome, []).RemoveLeftBehind(PackageProgram));
    }

    [Fact]
    public void A_data_folder_of_the_user_listed_among_those_of_the_system_is_not_taken_for_a_package()
    {
        // Some sessions put ~/.local/share in $XDG_DATA_DIRS: the entry a lone AppImage wrote there is its own, kept as it is.
        FirstLaunchOfTheAppImage();
        File.Delete(SystemEntry);
        var written = File.ReadAllBytes(Entry().FilePath);
        var entry = new ApplicationEntry(DataHome, [DataHome + "/", SystemData]);

        Assert.Null(entry.RemoveLeftBehind(AppImage));
        Assert.Equal(EntryOutcome.AlreadyThere.ToString(), entry.Decide(Share, AppImage).ToString());
        Assert.Equal(written, File.ReadAllBytes(Entry().FilePath));
    }

    [Fact]
    public void With_no_entry_of_the_user_or_no_program_nothing_is_said()
    {
        Assert.Null(Entry().RemoveLeftBehind(PackageProgram));

        FirstLaunchOfTheAppImage();
        Assert.Null(Entry().RemoveLeftBehind(null));
        Assert.True(File.Exists(Entry().FilePath));
    }

    [Fact]
    public void A_failure_to_read_is_said_in_one_line_and_thrown_by_nothing()
    {
        // A folder where the user's entry goes: reading it fails, on Windows as on Linux.
        Directory.CreateDirectory(Entry().FilePath);

        var line = Entry().RemoveLeftBehind(PackageProgram);

        Assert.StartsWith("desktop entry: the one an earlier launch wrote, not removed (", line, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, line, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Entry().FilePath));
    }

    [Fact]
    public void The_packaged_entry_is_the_one_the_appimages_of_0_6_0_to_0_8_0_wrote_from()
    {
        // An entry is known as the app's by its text: what EntryRules.Text writes from the entry of the package. Change
        // packaging/linux's entry, and the entries the earlier versions wrote are no longer known: keep their text known.
        var text = File.ReadAllText(PackagedEntryPath).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal("23c65cf8466d2d407acf59f2961babef69fafb655e2300cff6641f9259cac5d0", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
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
