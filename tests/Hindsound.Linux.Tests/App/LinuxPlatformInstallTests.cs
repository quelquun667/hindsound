using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Linux.Updates;
using Hindsound.Updates;

namespace Hindsound.Linux.Tests.App;

/// <summary>What the Linux layer hands the portable code for the updates: the HTTP of Linux, and a check that only tells.</summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(PlatformDefaultsCollection.Name)]
public sealed class LinuxPlatformInstallTests
{
    [Fact]
    public void The_linux_layer_announces_new_versions_over_its_own_http()
    {
        LinuxPlatform.Install(_ => { });

        Assert.Equal(UpdateChannel.LinuxAnnounce, PlatformDefaults.UpdateChannel);
        Assert.IsType<LinuxHttpTransport>(PlatformDefaults.Make(PlatformDefaults.UpdateNetwork, "the update network"));
    }
}
