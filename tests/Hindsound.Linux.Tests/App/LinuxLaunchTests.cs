using Hindsound.App;
using Hindsound.Linux.App;

namespace Hindsound.Linux.Tests.App;

public sealed class LinuxLaunchTests
{
    [Fact]
    public void Save_is_taken_out_of_the_options_and_asks_the_running_instance_to_save()
    {
        var launch = LinuxLaunch.Read(["--no-mic", "--save", "--settings", "a.json"]);

        Assert.True(launch.Save);
        Assert.Equal(["--no-mic", "--settings", "a.json"], launch.Rest);
        Assert.Equal(InstanceMessage.Save, launch.Message);
    }

    [Fact]
    public void A_plain_launch_asks_the_running_instance_to_show_itself()
    {
        var launch = LinuxLaunch.Read(["--no-mic"]);

        Assert.False(launch.Save);
        Assert.Equal(["--no-mic"], launch.Rest);
        Assert.Equal(InstanceMessage.Show, launch.Message);
    }

    [Fact]
    public void The_options_that_draw_a_window_are_named()
    {
        Assert.Equal("--dump-settings", LinuxLaunch.WindowOnlyOption(AppOptions.Parse(["--dump-settings", "out"])));
        Assert.Equal("--dump-notifications", LinuxLaunch.WindowOnlyOption(AppOptions.Parse(["--dump-notifications", "out"])));
        Assert.Null(LinuxLaunch.WindowOnlyOption(AppOptions.Parse(["--no-mic"])));
    }

    [Fact]
    public void An_appimage_is_started_again_by_its_image_not_by_its_temporary_mount()
    {
        Assert.Equal("/home/u/Hindsound.AppImage", LaunchPaths.Resolve(_ => "/home/u/Hindsound.AppImage", "/tmp/.mount_x/usr/bin/hindsound"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Hindsound.AppImage")]
    public void Without_an_absolute_image_the_executable_is_the_launch_path(string? image)
    {
        Assert.Equal("/opt/hindsound/hindsound", LaunchPaths.Resolve(_ => image, "/opt/hindsound/hindsound"));
    }
}
