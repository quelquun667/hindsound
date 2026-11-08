namespace Hindsound.Tests.App;

/// <summary>Main takes the single-instance mutex by the name of its settings file: MutexNameTests cannot see which call Main makes.</summary>
public class LaunchInstanceTests
{
    [Fact]
    public void Main_takes_the_instance_by_the_name_of_the_settings_file()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "src", "Hindsound", "App", "Program.cs")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);

        // Whole lines, as for the installer: a Main that builds its own Mutex on the plain name would leave MutexNameTests green.
        var lines = File.ReadAllLines(Path.Combine(folder.FullName, "src", "Hindsound", "App", "Program.cs")).Select(line => line.Trim()).ToList();
        var take = lines.IndexOf("using var instance = TakeInstance(options, out var first);");
        Assert.True(take >= 0, "Main must take the instance through TakeInstance(options, ...)");
        Assert.Equal(1, lines.Count(line => line.Contains("TakeInstance(", StringComparison.Ordinal) && line.StartsWith("using var", StringComparison.Ordinal)));
        Assert.Equal(1, lines.Count(line => line.Contains("new Mutex(", StringComparison.Ordinal) || line.Contains("new(true", StringComparison.Ordinal)));

        // Before the app is built: a second copy must have left by then.
        var app = lines.FindIndex(line => line.Contains("new HindsoundApp(", StringComparison.Ordinal));
        Assert.True(app > take, "the instance is taken before the app is built");
    }
}
