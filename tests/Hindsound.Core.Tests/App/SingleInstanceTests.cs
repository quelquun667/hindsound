using Hindsound.App;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>The single instance, as any system runs it: the name, and what a second launch says and tells.</summary>
public class SingleInstanceTests
{
    private sealed class FakeChannel(bool answers) : IInstanceChannel
    {
        public List<InstanceMessage> Told { get; } = [];

        public bool First => false;

        public bool Tell(InstanceMessage message)
        {
            Told.Add(message);
            return answers;
        }

        public void Listen(IAppLoop loop, Action<InstanceMessage> received) => throw new InvalidOperationException("a second launch does not listen");

        public void Dispose()
        {
        }
    }

    [Fact]
    public void A_second_real_launch_tells_the_running_one_to_show_itself_and_leaves()
    {
        var channel = new FakeChannel(answers: true);
        var journal = new List<string>();

        var code = SingleInstance.Leave(channel, AppOptions.Parse([]), journal.Add);

        Assert.Equal(SingleInstance.ExitAnotherInstance, code);
        Assert.Equal([InstanceMessage.Show], channel.Told);
        Assert.Equal(["another instance is running: leaving"], journal);
    }

    [Fact]
    public void A_running_one_that_cannot_be_told_is_written_down()
    {
        var journal = new List<string>();

        SingleInstance.Leave(new FakeChannel(answers: false), AppOptions.Parse([]), journal.Add);

        Assert.Equal(["another instance is running: leaving", "the running instance could not be told"], journal);
    }

    [Fact]
    public void A_test_run_or_a_failed_update_tells_nothing()
    {
        var test = new FakeChannel(answers: true);
        var update = new FakeChannel(answers: true);

        SingleInstance.Leave(test, AppOptions.Parse(["--settings", "test.json"]), _ => { });
        SingleInstance.Leave(update, AppOptions.Parse(["--update-failed"]), _ => { });

        Assert.Empty(test.Told);
        Assert.Empty(update.Told);
    }

    [Fact]
    public void A_real_launch_keeps_the_plain_name_and_a_test_run_gets_one_of_its_own()
    {
        Assert.Equal(SingleInstance.Name, SingleInstance.NameFor(AppOptions.Parse([])));
        Assert.StartsWith(SingleInstance.Name + ".Test.", SingleInstance.NameFor(AppOptions.Parse(["--settings", "a.json"])), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_settings_files_whose_paths_differ_only_in_case_are_one_file_only_where_paths_ignore_case()
    {
        var upper = AppOptions.Parse(["--settings", Path.Combine(Path.GetTempPath(), "A.json")]);
        var lower = AppOptions.Parse(["--settings", Path.Combine(Path.GetTempPath(), "a.json")]);

        Assert.Equal(SingleInstance.NameFor(upper, ignoreCase: true), SingleInstance.NameFor(lower, ignoreCase: true));
        Assert.NotEqual(SingleInstance.NameFor(upper, ignoreCase: false), SingleInstance.NameFor(lower, ignoreCase: false));

        // The rule of the system running: Windows ignores case and the others do not.
        Assert.Equal(OperatingSystem.IsWindows(), SingleInstance.NameFor(upper) == SingleInstance.NameFor(lower));
    }
}

/// <summary>The startup entry as a model: enabled for this program or not, enable with a program, disable.</summary>
public class StartupModelTests
{
    private sealed class ModelEntry : IRunKey
    {
        public string? Program { get; set; }

        public RunEntry State(string? program) =>
            Program is null ? RunEntry.None : Program == program ? RunEntry.ThisProgram : RunEntry.OtherProgram;

        public void Enable(string program) => Program = program;

        public void Disable() => Program = null;
    }

    [Fact]
    public void The_entry_is_handed_the_program_itself_and_a_stale_one_counts_as_off_but_present()
    {
        var entry = new ModelEntry { Program = "/opt/old/hindsound" };
        var startup = new StartupRegistration(entry, "/opt/hindsound/hindsound");

        Assert.False(startup.IsEnabled());
        Assert.True(startup.HasEntry());
        Assert.True(startup.Apply(true));
        Assert.Equal("/opt/hindsound/hindsound", entry.Program);
        Assert.True(startup.IsEnabled());
        Assert.True(startup.Apply(false));
        Assert.False(startup.HasEntry());
    }
}
