using Hindsound.App;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// The words the app gives a new version, by channel. The installed Windows app keeps its own; a copy that installs nothing
/// (the portable Windows copy, Linux) says where to download it, also when an "auto" was saved before.
/// </summary>
public class UpdateNoticeTests
{
    private const uint Accent = 0xFF7CFC00;

    private static readonly UpdateVersion Next = new(0, 6, 0);

    private static UpdateChannel? ChannelNamed(string name) => name switch
    {
        "setup" => UpdateChannel.WindowsSetup,
        "portable" => UpdateChannel.WindowsPortable,
        "linux" => UpdateChannel.LinuxAnnounce,
        _ => null,
    };

    [Theory]
    [InlineData("setup", false, "Update available (0.6.0)", "Click to see what's new.")]
    [InlineData("setup", true, "Update ready (0.6.0)", "Installs when Hindsound closes. Click for details.")]
    [InlineData("none", false, "Update available (0.6.0)", "Click to see what's new.")]
    [InlineData("none", true, "Update ready (0.6.0)", "Installs when Hindsound closes. Click for details.")]
    public void The_installed_app_keeps_its_words(string channel, bool auto, string title, string body)
    {
        var content = UpdateNotice.For(ChannelNamed(channel), Next, auto ? UpdateMode.Auto : UpdateMode.Ask, Accent);

        Assert.Equal(title, content.Title);
        Assert.Equal(body, content.Body);
        Assert.True(content.ClickShowsUpdates);
    }

    [Theory]
    [InlineData("portable", false)]
    [InlineData("portable", true)]
    [InlineData("linux", false)]
    [InlineData("linux", true)]
    public void A_copy_that_installs_nothing_says_the_version_is_out(string channel, bool auto)
    {
        var content = UpdateNotice.For(ChannelNamed(channel), Next, auto ? UpdateMode.Auto : UpdateMode.Ask, Accent);

        Assert.Equal("Hindsound 0.6.0 is out", content.Title);
        Assert.Equal("Click to download it.", content.Body);
        Assert.False(content.ClickShowsUpdates);
    }

    [Fact]
    public void The_launch_line_names_the_manifest_of_the_channel()
    {
        Assert.Equal(
            "updates: active False (test run), channel hindsound-update-win-x64-portable.json",
            UpdateWiring.LaunchLine(UpdateWiring.TestRun, UpdateChannel.WindowsPortable));
        Assert.Equal(
            "updates: active True (installed), channel hindsound-update-win-x64.json",
            UpdateWiring.LaunchLine(UpdateWiring.Installed, UpdateChannel.WindowsSetup));
        Assert.Equal(
            "updates: active True (announces only), channel hindsound-update-linux-x64.json",
            UpdateWiring.LaunchLine(UpdateWiring.AnnouncesOnly, UpdateChannel.LinuxAnnounce));
    }
}
