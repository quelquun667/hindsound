using Hindsound.Linux.Desktop;
using Hindsound.Ui;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The urgency each notification asks for: a clip gets a banner on GNOME and Plasma, which show a low one only in their list;
/// the other notices of the app stay low. All of them stay silent.
/// </summary>
public sealed class NotificationUrgencyTests
{
    private static NotificationContent Content(string? clip) =>
        new(NotificationKind.Silent, "Hindsound", "Saving", [], 0, NotificationVisual.Bars, 0, []) { ClipPath = clip };

    [Fact]
    public void A_clip_asks_for_normal_urgency_and_no_sound()
    {
        var request = NotificationRequest.For(Content("/home/u/Music/Hindsound/a.mp3"), 3, markup: true);

        Assert.Equal(NotificationRequest.NormalUrgency, request.Urgency);
        Assert.Equal((byte)1, NotificationRequest.NormalUrgency);
        Assert.True(request.SuppressSound);
    }

    [Fact]
    public void A_notice_with_no_clip_stays_low_and_silent()
    {
        var request = NotificationRequest.For(Content(null), 3, markup: true);

        Assert.Equal((byte)0, request.Urgency);
        Assert.True(request.SuppressSound);
    }
}
