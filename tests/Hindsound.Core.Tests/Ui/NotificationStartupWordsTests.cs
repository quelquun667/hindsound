using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>The notification of a start with the system that could not change names the toggle as the system does.</summary>
public class NotificationStartupWordsTests
{
    [Fact]
    public void On_Windows_the_title_names_Start_with_Windows()
    {
        Assert.Equal("Couldn't change Start with Windows", NotificationStates.StartupNotChanged(PlatformFeatures.Windows).Title);
    }

    [Fact]
    public void On_Linux_the_title_names_the_session_and_never_Windows()
    {
        var title = NotificationStates.StartupNotChanged(PlatformFeatures.Linux(() => ShortcutMode.Capture)).Title;

        Assert.Equal("Couldn't change Start with the session", title);
        Assert.DoesNotContain("Windows", title, StringComparison.Ordinal);
    }
}
