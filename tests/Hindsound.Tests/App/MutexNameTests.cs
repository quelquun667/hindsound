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
        // The one argument of the launch Setup starts again after a failed update: the old copy still running holds this name.
        Assert.Equal(RealName, NameFor("--update-failed"));
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
        // Whole lines, not a piece of text: a longer name, or a line that no longer feeds AppMutex, must not pass.
        var lines = File.ReadAllLines(Path.Combine(folder.FullName, "installer", "Hindsound.iss")).Select(line => line.Trim()).ToList();
        Assert.Contains("#define AppMutexName \"" + RealName + "\"", lines);
        Assert.Contains("AppMutex={#AppMutexName}", lines);
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

    // Only with --settings: a test must never hold the real name, which Setup and a real launch wait on.
    [Fact]
    public void The_mutex_taken_is_the_one_of_the_settings_file()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var options = AppOptions.Parse(["--settings", file]);

        using var held = Program.TakeInstance(options, out var first);
        Assert.True(first);

        using var again = Program.TakeInstance(options, out var second);
        Assert.False(second);

        using var other = Program.TakeInstance(AppOptions.Parse(["--settings", file + ".other"]), out var third);
        Assert.True(third);
    }

    [Fact]
    public void Two_files_never_share_a_name()
    {
        Assert.NotEqual(NameFor("--settings", @"C:\t\a.json"), NameFor("--settings", @"C:\t\b.json"));
    }
}
