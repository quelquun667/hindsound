using Hindsound.App;
using Hindsound.Settings;

namespace Hindsound.Tests.Settings;

/// <summary>The program of the startup entry: the platform may name another path than the running executable (an AppImage).</summary>
[Collection("PlatformDefaults")]
public class StartupRegistrationLaunchPathTests
{
    private sealed class RecordingKey : IRunKey
    {
        public string? Started { get; private set; }

        public RunEntry State(string? program) => program is not null && program == Started ? RunEntry.ThisProgram : RunEntry.None;

        public void Enable(string program) => Started = program;

        public void Disable() => Started = null;
    }

    [Fact]
    public void The_entry_starts_the_launch_path_the_platform_names()
    {
        var before = PlatformDefaults.LaunchPath;
        PlatformDefaults.LaunchPath = () => "/home/someone/Apps/Hindsound.AppImage";
        try
        {
            var key = new RecordingKey();
            var startup = new StartupRegistration(key);

            Assert.True(startup.Enable());
            Assert.Equal("/home/someone/Apps/Hindsound.AppImage", key.Started);
            Assert.True(startup.IsEnabled());
        }
        finally
        {
            PlatformDefaults.LaunchPath = before;
        }
    }

    [Fact]
    public void Without_a_launch_path_the_entry_starts_the_running_executable()
    {
        var before = PlatformDefaults.LaunchPath;
        PlatformDefaults.LaunchPath = () => null;
        try
        {
            var key = new RecordingKey();

            Assert.True(new StartupRegistration(key).Enable());
            Assert.Equal(Environment.ProcessPath, key.Started);
        }
        finally
        {
            PlatformDefaults.LaunchPath = before;
        }
    }
}
