using Hindsound.App;
using Hindsound.Settings;
using S = Hindsound.App.UserNotificationState;

namespace Hindsound.Tests.App;

public class FullscreenPolicyTests
{
    private static GeneralSettings General(string fullscreen = "auto", string sound = "whenNoOverlay", bool notifications = true) =>
        new() { FullscreenGames = fullscreen, ConfirmSound = sound, Notifications = notifications };

    //                state                       covers  overlay click  sound  catch-up
    [Theory]
    [InlineData(S.AcceptsNotifications, false, true, false, false, false)]
    [InlineData(S.AcceptsNotifications, true, true, true, false, false)]
    [InlineData(S.QuietTime, false, true, false, false, false)]
    [InlineData(S.Busy, false, true, true, false, false)]
    [InlineData(S.Busy, true, true, true, false, false)]
    [InlineData(S.App, false, true, true, false, false)]
    [InlineData(S.Unknown, false, true, true, false, false)]
    [InlineData(S.RunningD3dFullScreen, false, false, false, true, true)]
    [InlineData(S.RunningD3dFullScreen, true, false, false, true, true)]
    [InlineData(S.PresentationMode, false, false, false, true, true)]
    [InlineData(S.NotPresent, false, false, false, true, true)]
    public void Auto_table(S state, bool covers, bool overlay, bool clickThrough, bool sound, bool catchUp)
    {
        var decision = FullscreenPolicy.ForClip(state, covers, General(), saved: true, important: false);
        Assert.Equal(new Presentation(overlay, clickThrough, sound, catchUp), decision);
    }

    [Theory]
    [InlineData(S.AcceptsNotifications, false, true, false, false, false)]
    [InlineData(S.AcceptsNotifications, true, false, false, true, true)]
    [InlineData(S.Busy, false, false, false, true, true)]
    [InlineData(S.RunningD3dFullScreen, false, false, false, true, true)]
    public void Sound_only_table(S state, bool covers, bool overlay, bool clickThrough, bool sound, bool catchUp)
    {
        var decision = FullscreenPolicy.ForClip(state, covers, General(fullscreen: "soundOnly"), saved: true, important: false);
        Assert.Equal(new Presentation(overlay, clickThrough, sound, catchUp), decision);
    }

    [Theory]
    [InlineData("whenNoOverlay", S.AcceptsNotifications, false)]
    [InlineData("whenNoOverlay", S.RunningD3dFullScreen, true)]
    [InlineData("always", S.AcceptsNotifications, true)]
    [InlineData("always", S.RunningD3dFullScreen, true)]
    [InlineData("never", S.AcceptsNotifications, false)]
    [InlineData("never", S.RunningD3dFullScreen, false)]
    public void The_sound_setting_decides(string setting, S state, bool sound)
    {
        var decision = FullscreenPolicy.ForClip(state, false, General(sound: setting), saved: true, important: false);
        Assert.Equal(sound, decision.PlaySound);
    }

    [Fact]
    public void A_clip_that_failed_never_sounds_like_a_saved_one()
    {
        var decision = FullscreenPolicy.ForClip(S.RunningD3dFullScreen, false, General(sound: "always"), saved: false, important: true);
        Assert.False(decision.PlaySound);
        Assert.True(decision.CatchUpLater);
    }

    [Theory]
    [InlineData("always", S.AcceptsNotifications)]
    [InlineData("always", S.RunningD3dFullScreen)]
    [InlineData("whenNoOverlay", S.RunningD3dFullScreen)]
    public void A_clip_saved_but_not_copied_never_sounds_like_success(string setting, S state)
    {
        var decision = FullscreenPolicy.ForClip(state, false, General(sound: setting), saved: true, important: true, copyFailed: true);
        Assert.False(decision.PlaySound);
    }

    [Fact]
    public void A_copy_that_failed_is_shown_even_with_notifications_off()
    {
        var onDesktop = FullscreenPolicy.ForClip(S.AcceptsNotifications, false, General(notifications: false), saved: true, important: true, copyFailed: true);
        Assert.True(onDesktop.ShowOverlay);
        Assert.False(onDesktop.PlaySound);

        var inGame = FullscreenPolicy.ForClip(S.RunningD3dFullScreen, false, General(notifications: false), saved: true, important: true, copyFailed: true);
        Assert.False(inGame.ShowOverlay);
        Assert.True(inGame.CatchUpLater);
        Assert.False(inGame.PlaySound);
    }

    [Fact]
    public void Notifications_off_shows_nothing_and_keeps_nothing_for_later()
    {
        var decision = FullscreenPolicy.ForClip(S.RunningD3dFullScreen, false, General(notifications: false), saved: true, important: false);
        Assert.False(decision.ShowOverlay);
        Assert.False(decision.CatchUpLater);
    }

    [Fact]
    public void Notifications_off_still_shows_what_the_user_must_know()
    {
        var decision = FullscreenPolicy.ForClip(S.AcceptsNotifications, false, General(notifications: false), saved: true, important: true);
        Assert.True(decision.ShowOverlay);
    }

    [Theory]
    [InlineData(S.AcceptsNotifications, false, true, false)]
    [InlineData(S.Busy, false, true, true)]
    [InlineData(S.RunningD3dFullScreen, false, false, false)]
    public void A_status_is_silent_and_is_never_kept_for_later(S state, bool covers, bool overlay, bool clickThrough)
    {
        var decision = FullscreenPolicy.ForStatus(state, covers, General(), important: false);
        Assert.Equal(new Presentation(overlay, clickThrough, false, false), decision);
    }

    [Theory]
    [InlineData(S.AcceptsNotifications, false, true)]
    [InlineData(S.QuietTime, false, true)]
    [InlineData(S.AcceptsNotifications, true, false)]
    [InlineData(S.RunningD3dFullScreen, false, false)]
    [InlineData(S.Busy, false, false)]
    [InlineData(S.PresentationMode, false, false)]
    [InlineData(S.NotPresent, false, false)]
    public void Back_on_the_desktop(S state, bool covers, bool expected)
    {
        Assert.Equal(expected, FullscreenPolicy.BackOnDesktop(state, covers));
    }
}
