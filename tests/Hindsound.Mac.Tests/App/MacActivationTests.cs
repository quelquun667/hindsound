using Hindsound.Mac.App;

namespace Hindsound.Mac.Tests.App;

/// <summary>Which message brings the app forward when a window of it opens: the one of macOS 14 when the app has it.</summary>
public sealed class MacActivationTests
{
    [Fact]
    public void An_app_that_answers_activate_gets_the_message_of_macOS_14()
    {
        var asked = new List<string>();

        var message = MacActivation.MessageFor(selector =>
        {
            asked.Add(selector);
            return true;
        });

        Assert.Equal("activate", message);
        Assert.Equal(["activate"], asked);
    }

    [Fact]
    public void An_app_that_does_not_answer_activate_gets_the_older_message()
    {
        Assert.Equal("activateIgnoringOtherApps:", MacActivation.MessageFor(_ => false));
    }
}
