using Hindsound.Mac.Notification;

namespace Hindsound.Mac.Tests.Notification;

public sealed class MacNotificationHookTests
{
    [Fact]
    public void A_click_on_the_notification_does_not_bring_the_app_forward_as_on_Windows()
    {
        var message = Assert.Single(MacNotificationWindow.OnCreate, m => m.Selector == MacNotificationWindow.PreventsActivation);

        Assert.Equal(WindowArgument.Bool, message.Kind);
        Assert.Equal(1, message.Value);
        Assert.True(message.OnlyIfAnswered);
        Assert.Equal("_setPreventsActivation:", MacNotificationWindow.PreventsActivation);
    }

    [Fact]
    public void A_window_that_answers_the_message_not_public_gets_it_last()
    {
        var asked = new List<string>();

        var sent = MacNotificationWindow.ToSend(selector =>
        {
            asked.Add(selector);
            return true;
        }).ToList();

        Assert.Equal(MacNotificationWindow.OnCreate, sent);
        Assert.Equal(MacNotificationWindow.PreventsActivation, sent[^1].Selector);
        Assert.Equal([MacNotificationWindow.PreventsActivation], asked);
    }

    [Fact]
    public void A_window_that_does_not_answer_the_message_not_public_gets_only_the_public_ones()
    {
        var sent = MacNotificationWindow.ToSend(_ => false).ToList();

        Assert.DoesNotContain(sent, m => m.Selector == MacNotificationWindow.PreventsActivation);
        Assert.Equal(MacNotificationWindow.OnCreate.Where(m => !m.OnlyIfAnswered), sent);
        Assert.Equal(3, sent.Count);
    }

    [Fact]
    public void The_window_stays_on_every_space_above_the_floating_windows_and_there_when_the_app_is_not_active()
    {
        Assert.Equal(
            [
                new WindowMessage("setCollectionBehavior:", 0x151, WindowArgument.UInt),
                new WindowMessage("setLevel:", 25, WindowArgument.Int),
                new WindowMessage("setHidesOnDeactivate:", 0, WindowArgument.Bool),
            ],
            MacNotificationWindow.OnCreate.Where(m => !m.OnlyIfAnswered));
    }
}
