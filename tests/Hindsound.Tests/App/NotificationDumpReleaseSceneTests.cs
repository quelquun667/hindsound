using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>--dump-notifications draws the announcement of a copy that installs nothing, so its banner can be looked at.</summary>
public class NotificationDumpReleaseSceneTests
{
    [Fact]
    public void The_dump_has_the_release_notice_scene()
    {
        var samples = NotificationDump.Samples(NotificationPalette.Lime);

        var scene = Assert.Single(samples, s => s.Name == "release-out");
        Assert.Equal(ReleaseNotice.Title(new UpdateVersion(0, 2, 0)), scene.Content.Title);
        Assert.Equal(ReleaseNotice.Body, scene.Content.Body);
        Assert.Equal(samples.Count, samples.Select(s => s.Name).Distinct().Count());
    }
}
