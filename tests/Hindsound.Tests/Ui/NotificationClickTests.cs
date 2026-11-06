using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.Ui;

/// <summary>Where a click on a notification leads: to where the problem gets solved, and never to a clips folder it would create.</summary>
public class NotificationClickTests
{
    [Fact]
    public void Each_settings_notification_opens_the_settings()
    {
        NotificationContent[] all =
        [
            NotificationStates.SettingsDamaged(copyKept: true),
            NotificationStates.SettingsDamaged(copyKept: false),
            NotificationStates.SettingsUnreadable(),
            NotificationStates.SettingsHasError(),
            NotificationStates.SettingsNotSaved(),
            NotificationStates.StartupNotChanged(),
        ];

        Assert.All(all, content =>
        {
            Assert.Equal(NoticeRank.Lasting, NoticeQueue.RankOf(content.Kind));
            Assert.True(content.ClickOpensSettings);
        });
    }

    [Fact]
    public void A_notification_about_a_clip_stays_with_the_clips()
    {
        Assert.False(NotificationStates.SaveFailed().ClickOpensSettings);
        Assert.False(NotificationStates.NothingToSave().ClickOpensSettings);
        Assert.False(NotificationStates.LastClipGone().ClickOpensSettings);
    }
}
