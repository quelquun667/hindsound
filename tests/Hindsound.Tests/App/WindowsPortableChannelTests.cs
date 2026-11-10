using Hindsound.App;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// The Windows layer picks the channel from the folder of the exe. The test host's folder holds no portable.txt: the channel
/// stays the installed app's, as before, and nothing is ignored.
/// </summary>
public class WindowsPortableChannelTests
{
    [Fact]
    public void Without_portable_txt_the_windows_layer_leaves_the_installed_channel()
    {
        WindowsPlatform.Install();

        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, PortableCopy.MarkerName)));
        Assert.Null(PlatformDefaults.UpdateChannel);
        Assert.Equal(UpdateChannel.WindowsSetup, UpdateWiring.Channel);
        Assert.False(WindowsPlatform.PortableMarkerIgnored);
    }
}
