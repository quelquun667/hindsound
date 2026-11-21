using System.Runtime.Versioning;
using Hindsound.Mac.App;

namespace Hindsound.Mac.Tests.App;

/// <summary>
/// The relaunch of <c>--save</c> with no running instance, with a stand-in for <c>open</c>: its code says whether the bundle
/// started. The real <c>open</c> is proven by the mac job, with the app itself.
/// </summary>
[Trait("Category", "Mac")]
[SupportedOSPlatform("macos")]
public sealed class MacRelaunchNativeTests
{
    [Fact]
    public void The_launch_leaves_with_success_when_open_starts_the_bundle()
    {
        var lines = new List<string>();

        Assert.Equal(MacLaunch.Started, MacLaunch.Relaunch("/Applications/Hindsound.app", ["--settings", "/tmp/s.json"], lines.Add, "/usr/bin/true"));
        Assert.Contains(lines, line => line.Contains("starting /Applications/Hindsound.app through Launch Services", StringComparison.Ordinal));
        Assert.Contains("--save: open ended with code 0", lines);
    }

    [Fact]
    public void The_launch_fails_when_open_fails_or_cannot_run()
    {
        var lines = new List<string>();

        Assert.Equal(MacLaunch.NotStarted, MacLaunch.Relaunch("/Applications/Hindsound.app", [], lines.Add, "/usr/bin/false"));
        Assert.Contains("--save: open ended with code 1", lines);
        Assert.Equal(MacLaunch.NotStarted, MacLaunch.Relaunch("/Applications/Hindsound.app", [], lines.Add, "/nonexistent/open"));
        Assert.Contains(lines, line => line.StartsWith("--save: open failed: ", StringComparison.Ordinal));
    }
}
