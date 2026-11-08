using Hindsound.App;

namespace Hindsound.Tests.App;

/// <summary>Setup waits on the plain name; a run on its own settings file must stay out of the way of the real copy.</summary>
public class MutexNameTests
{
    private const string RealName = @"Local\Hindsound.SingleInstance";
    private const string TestPrefix = @"Local\Hindsound.SingleInstance.Test.";

    private static string NameFor(params string[] args) => Program.MutexNameFor(AppOptions.Parse(args));

    [Fact]
    public void A_plain_launch_keeps_the_name_setup_waits_on()
    {
        Assert.Equal(RealName, NameFor());
        Assert.Equal(RealName, NameFor("--offscreen", "--no-mic"));
    }

    [Fact]
    public void The_installer_waits_on_that_very_name()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "installer", "Hindsound.iss")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        Assert.Contains(RealName, File.ReadAllText(Path.Combine(folder.FullName, "installer", "Hindsound.iss")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_with_its_own_settings_gets_another_name()
    {
        var name = NameFor("--settings", @"C:\t\a.json");

        Assert.NotEqual(RealName, name);
        Assert.StartsWith(TestPrefix, name, StringComparison.Ordinal);
    }

    [Fact]
    public void The_name_is_the_sha256_of_the_full_path()
    {
        // Worked out apart from the code: the name of a file must be the same from one process, and one version, to the next.
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(@"C:\T\A.JSON")));

        Assert.Equal(TestPrefix + expected, NameFor("--settings", @"C:\t\a.json"));
        Assert.Matches(@"^Local\\Hindsound\.SingleInstance\.Test\.[0-9A-F]{64}$", NameFor("--settings", @"C:\t\a.json"));
    }

    [Fact]
    public void Two_runs_on_one_file_refuse_each_other_however_the_path_is_written()
    {
        var full = Path.GetFullPath("same-settings.json");

        Assert.Equal(NameFor("--settings", full), NameFor("--settings", "same-settings.json"));
        Assert.Equal(NameFor("--settings", full), NameFor("--settings", full.ToUpperInvariant()));
    }

    [Fact]
    public void Two_files_never_share_a_name()
    {
        Assert.NotEqual(NameFor("--settings", @"C:\t\a.json"), NameFor("--settings", @"C:\t\b.json"));
    }
}
