using System.Runtime.Versioning;
using System.Text.Json;
using Hindsound.App;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

/// <summary>The whole app on the Linux layer: it starts, runs its loop, hears a save from a second launch, and ends.</summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(PlatformDefaultsCollection.Name)]
public sealed class LinuxAppSmokeTests
{
    // Hands InstanceMessage.Save to the app a moment after it listens, as hindsound --save would.
    private sealed class SaveAfterStart : IInstanceChannel
    {
        public bool First => true;

        public bool Tell(InstanceMessage message) => false;

        public void Listen(IAppLoop loop, Action<InstanceMessage> received) => loop.After(300, () => received(InstanceMessage.Save));

        public void Dispose()
        {
        }
    }

    [Fact]
    public void The_app_runs_on_the_linux_layer_and_a_save_from_a_second_launch_reaches_it()
    {
        var folder = Directory.CreateTempSubdirectory("hindsound-linux-").FullName;
        try
        {
            var settings = Path.Combine(folder, "settings.json");
            File.WriteAllText(settings, $$$"""
                {"audio":{"micDeviceId":"disabled"},
                 "export":{"outputFolder":{{{JsonSerializer.Serialize(Path.Combine(folder, "clips"))}}},"copyToClipboard":false},
                 "general":{"startWithWindows":false,"confirmSound":"never"}}
                """);
            var logPath = Path.Combine(folder, "settings.log");
            var journal = new Journal(logPath);
            LinuxPlatform.Install(journal.Write);
            var options = AppOptions.Parse(["--settings", settings, "--no-mic", "--no-hotkeys", "--no-clipboard", "--offscreen", "--exit-after", "2"]);

            var code = new HindsoundApp(options, journal, updaterMayRun: false, LinuxPlatform.Desktop(), shell: new LinuxShell(journal.Write), instance: new SaveAfterStart()).Run();

            var lines = Journal.ReadShared(logPath);
            Assert.Equal(0, code);
            Assert.Contains(lines, line => line.Contains("a second launch asks to save a clip", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("mic enabled False", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("fatal", StringComparison.Ordinal) || line.Contains("unhandled", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A thread of the app may still hold the log a moment after the run: the system cleans its temporary folder.
            }
        }
    }
}
