using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// The words of an update announcement, on an app whose updates install (no update flow of the platform that only tells, as
/// in any test run): Windows keeps its own, never those of a release page.
/// </summary>
public sealed class AppUpdateAnnounceTextTests : IDisposable
{
    private static readonly UpdateVersion Next = new(0, 6, 0);

    private readonly ShellRunRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Theory]
    [InlineData(false, "Update available (0.6.0)", "Click to see what's new.")]
    [InlineData(true, "Update ready (0.6.0)", "Installs when Hindsound closes. Click for details.")]
    public void An_announcement_shows_the_words_of_its_mode(bool auto, string title, string body)
    {
        _rig.Run(rig => ((IUpdateHost)rig.App!).Announce(Next, auto ? UpdateMode.Auto : UpdateMode.Ask));

        Assert.Contains(_rig.Log(), line => line.Contains("updates: active False", StringComparison.Ordinal));
        var shown = Assert.Single(_rig.Shell.Notification.Presented, content => content.Kind == Hindsound.Ui.NotificationKind.UpdateReady);
        Assert.Equal(title, shown.Title);
        Assert.Equal(body, shown.Body);
    }
}
