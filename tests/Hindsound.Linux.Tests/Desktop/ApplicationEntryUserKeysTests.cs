using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The entry of the menu keeps what the user chose about it: hidden from the menu, it stays hidden after a new version. And
/// the icons it brings change the time of the theme folder, which an icon cache looks at, even into size folders already there.
/// </summary>
public sealed class ApplicationEntryUserKeysTests : IDisposable
{
    private const string Program = "/home/u/Apps/Hindsound 0.6.0.AppImage";

    private readonly string _root = Directory.CreateTempSubdirectory("hindsound-entry-keys-").FullName;

    public ApplicationEntryUserKeysTests()
    {
        Directory.CreateDirectory(Path.Combine(Share, "applications"));
        File.WriteAllText(Path.Combine(Share, "applications", ApplicationEntry.FileName), "[Desktop Entry]\nType=Application\nName=Hindsound\nExec=hindsound\n");
        Directory.CreateDirectory(Path.Combine(Share, "icons", "hicolor", "48x48", "apps"));
        File.WriteAllBytes(Path.Combine(Share, "icons", "hicolor", "48x48", "apps", ApplicationEntry.IconName), [0x89, 1]);
    }

    private string Share => Path.Combine(_root, "package", "share");

    private string DataHome => Path.Combine(_root, "home", ".local", "share");

    private string Theme => Path.Combine(DataHome, "icons", "hicolor");

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

    private ApplicationEntry Entry() => new(DataHome, [Path.Combine(_root, "usr", "share")]);

    [Theory]
    [InlineData("[Desktop Entry]\nNoDisplay=true\nExec=/old\n", "Hidden")]
    [InlineData("[Desktop Entry]\r\nExec=/old\r\nNoDisplay=true\r\n", "Hidden")]
    [InlineData("[Desktop Entry]\nNoDisplay=false\nExec=/old\n", "Install")]
    [InlineData("[Desktop Entry]\nNoDisplay=false\nExec=/opt/h\n", "AlreadyThere")]
    [InlineData("[Desktop Action x]\nNoDisplay=true\n[Desktop Entry]\nExec=/old\n", "Install")]
    public void An_entry_the_user_hid_from_the_menu_is_judged_hidden(string text, string expected) =>
        Assert.Equal(expected, EntryRules.Judge(text, "/opt/h").ToString());

    [Fact]
    public void An_entry_the_user_hid_from_the_menu_stays_hidden_after_a_new_version()
    {
        var old = "[Desktop Entry]\nType=Application\nName=Hindsound\nExec=\"/home/u/Apps/Hindsound 0.5.0.AppImage\"\nNoDisplay=true\n";
        Directory.CreateDirectory(Path.GetDirectoryName(Entry().FilePath)!);
        File.WriteAllText(Entry().FilePath, old);

        Assert.Equal("desktop entry: hidden by the user, left alone", Entry().Apply(Share, Program));
        Assert.Equal(old, File.ReadAllText(Entry().FilePath));
    }

    [Fact]
    public void Icons_copied_into_a_size_folder_already_there_change_the_time_of_the_theme_folder()
    {
        Directory.CreateDirectory(Path.Combine(Theme, "48x48", "apps"));
        var longAgo = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(Theme, longAgo);
        var before = DateTime.UtcNow.AddMinutes(-1);

        Assert.Equal("desktop entry: written to the menu of applications, with its icons", Entry().Apply(Share, Program));

        Assert.True(File.Exists(Path.Combine(Theme, "48x48", "apps", ApplicationEntry.IconName)));
        Assert.True(Directory.GetLastWriteTimeUtc(Theme) > before, $"the theme folder still dates from {Directory.GetLastWriteTimeUtc(Theme):O}");
    }
}
