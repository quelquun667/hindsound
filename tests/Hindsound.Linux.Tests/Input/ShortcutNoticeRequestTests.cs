using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// The notice of a desktop with no global shortcut is written by the same code as the other notifications (the one whose
/// bytes the tests of the desktop read back from a server), so what it says is what its request holds.
/// </summary>
public sealed class ShortcutNoticeRequestTests
{
    [Fact]
    public void The_notice_asks_for_normal_urgency_and_no_sound()
    {
        var request = ShortcutNoticeRequest.Build();

        Assert.Equal((byte)1, request.Urgency);
        Assert.True(request.SuppressSound);
    }

    [Fact]
    public void The_notice_says_how_to_save_from_the_keyboard_and_offers_no_action()
    {
        var request = ShortcutNoticeRequest.Build();

        Assert.Equal("Hindsound", request.Summary);
        Assert.Equal(HotkeyChoice.NoShortcutText, request.Body);
        Assert.Empty(request.Actions);
        Assert.Equal(0u, request.ReplacesId);
        Assert.Equal("io.github.quelquun667.Hindsound", request.DesktopEntry);
        Assert.Equal(-1, request.ExpireTimeout);
    }
}
