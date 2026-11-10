using Hindsound.App;

namespace Hindsound.Tests.App;

/// <summary><c>hindsound --save</c> launched while the app runs: the running one is told to save, whatever the settings file.</summary>
public sealed class SingleInstanceSaveTests
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
    public void A_save_tells_the_running_instance_to_save_and_leaves()
    {
        var channel = new FakeChannel(answers: true);
        var journal = new List<string>();

        var code = SingleInstance.Leave(channel, AppOptions.Parse([]), journal.Add, InstanceMessage.Save);

        Assert.Equal(SingleInstance.ExitAnotherInstance, code);
        Assert.Equal([InstanceMessage.Save], channel.Told);
        Assert.Equal(["another instance is running: leaving"], journal);
    }

    [Fact]
    public void A_save_reaches_the_instance_of_its_own_settings_file()
    {
        var channel = new FakeChannel(answers: true);

        SingleInstance.Leave(channel, AppOptions.Parse(["--settings", "test.json"]), _ => { }, InstanceMessage.Save);

        Assert.Equal([InstanceMessage.Save], channel.Told);
    }

    [Fact]
    public void A_save_nobody_heard_is_logged()
    {
        var channel = new FakeChannel(answers: false);
        var journal = new List<string>();

        SingleInstance.Leave(channel, AppOptions.Parse([]), journal.Add, InstanceMessage.Save);

        Assert.Equal(["another instance is running: leaving", "the running instance could not be told to save a clip"], journal);
    }

    [Fact]
    public void A_plain_second_launch_still_asks_to_be_shown()
    {
        var channel = new FakeChannel(answers: true);

        SingleInstance.Leave(channel, AppOptions.Parse([]), _ => { }, InstanceMessage.Show);

        Assert.Equal([InstanceMessage.Show], channel.Told);
    }
}
