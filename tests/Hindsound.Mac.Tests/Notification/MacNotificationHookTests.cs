using Hindsound.Mac.Notification;

namespace Hindsound.Mac.Tests.Notification;

public sealed class MacNotificationHookTests
{
    [Fact]
    public void The_window_stays_on_every_space_above_the_floating_windows_and_there_when_the_app_is_not_active()
    {
        Assert.Equal(
            [
                new WindowMessage("setCollectionBehavior:", 0x151, WindowArgument.UInt),
                new WindowMessage("setLevel:", 25, WindowArgument.Int),
                new WindowMessage("setHidesOnDeactivate:", 0, WindowArgument.Bool),
            ],
            MacNotificationWindow.OnCreate);
    }
}
