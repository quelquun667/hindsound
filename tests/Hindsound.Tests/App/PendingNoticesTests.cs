using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Ui;
using K = Hindsound.Ui.NotificationKind;

namespace Hindsound.Tests.App;

public class PendingNoticesTests
{
    private readonly List<string> _lines = [];
    private readonly PendingNotices _pending;

    private static readonly NotificationContent Failed = NotificationStates.SaveFailed();
    private static readonly NotificationContent OtherFailure = NotificationStates.LastClipGone();
    private static readonly NotificationContent Ordinary = NotificationStates.NothingToSave();
    private static readonly NotificationContent OtherOrdinary = NotificationStates.StillSaving();
    private static readonly NotificationContent CaptureOn = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10");
    private static readonly NotificationContent NoDevice = NotificationStates.NoDevice();
    private static readonly NotificationContent MicLost = NotificationStates.MicLost();

    public PendingNoticesTests() => _pending = new PendingNotices(_lines.Add);

    private static AudioEngineStatus Status(CaptureActivity activity, bool micEnabled = true, bool micCapturing = true) =>
        new(activity, true, micEnabled, micCapturing, "Speakers", null, true, TimeSpan.Zero, 0);

    private List<NotificationContent> TakeAll(long now)
    {
        var taken = new List<NotificationContent>();
        while (_pending.TakeNext(now) is { } next)
        {
            taken.Add(next);
        }

        return taken;
    }

    [Fact]
    public void Nothing_waits_at_first()
    {
        Assert.False(_pending.Any);
        Assert.False(_pending.ImportantClipWaiting);
        Assert.Null(_pending.TakeNext(0));
    }

    [Fact]
    public void An_ordinary_clip_is_replaced_by_the_last_one()
    {
        _pending.AddClip(Ordinary, important: false, 0);
        _pending.AddClip(OtherOrdinary, important: false, 100);

        Assert.Equal([OtherOrdinary], TakeAll(200));
    }

    [Fact]
    public void An_ordinary_clip_is_given_up_after_30_s_with_a_line_in_the_journal()
    {
        _pending.AddClip(Ordinary, important: false, 1000);

        _pending.GiveUpOld(30_999);
        Assert.True(_pending.Any);
        Assert.Empty(_lines);

        _pending.GiveUpOld(31_000);
        Assert.False(_pending.Any);
        Assert.Contains(_lines, line => line.Contains("given up") && line.Contains(nameof(K.NothingToSave)));
        Assert.Null(_pending.TakeNext(31_000));
    }

    [Fact]
    public void The_limit_counts_from_the_last_clip_and_also_holds_when_nothing_looked_before()
    {
        _pending.AddClip(Ordinary, important: false, 0);
        _pending.AddClip(OtherOrdinary, important: false, 20_000);

        Assert.Equal(OtherOrdinary, _pending.TakeNext(49_999));

        _pending.AddClip(Ordinary, important: false, 0);
        Assert.Null(_pending.TakeNext(30_000));
    }

    [Fact]
    public void An_important_clip_waits_as_long_as_it_takes()
    {
        _pending.AddClip(Failed, important: true, 0);

        _pending.GiveUpOld(long.MaxValue / 2);

        Assert.True(_pending.ImportantClipWaiting);
        Assert.Equal([Failed], TakeAll(long.MaxValue / 2));
        Assert.False(_pending.ImportantClipWaiting);
    }

    [Fact]
    public void An_ordinary_clip_never_replaces_an_important_one()
    {
        _pending.AddClip(Failed, important: true, 0);
        _pending.AddClip(Ordinary, important: false, 100);

        Assert.True(_pending.ImportantClipWaiting);
        Assert.Equal(Failed, _pending.TakeNext(200));
    }

    [Fact]
    public void Only_an_important_clip_replaces_an_important_one()
    {
        _pending.AddClip(Failed, important: true, 0);
        _pending.AddClip(OtherFailure, important: true, 100);

        Assert.Equal([OtherFailure], TakeAll(200));
        Assert.Contains(_lines, line => line.Contains("replaced") && line.Contains(nameof(K.SaveFailed)));
    }

    [Fact]
    public void An_ordinary_clip_is_not_shown_after_an_important_one()
    {
        _pending.AddClip(Ordinary, important: false, 0);
        _pending.AddClip(Failed, important: true, 100);

        Assert.Equal([Failed], TakeAll(200));
        Assert.Contains(_lines, line => line.Contains(nameof(K.NothingToSave)));
    }

    [Fact]
    public void The_status_comes_first_then_the_important_clip_one_at_a_time()
    {
        _pending.AddClip(Failed, important: true, 0);
        _pending.AddStatus(CaptureOn, 100);

        Assert.Equal(CaptureOn, _pending.TakeNext(200));
        Assert.True(_pending.Any);
        Assert.Equal(Failed, _pending.TakeNext(200));
        Assert.False(_pending.Any);
    }

    [Fact]
    public void The_status_comes_before_an_ordinary_clip_and_leaves_it_in_place()
    {
        _pending.AddClip(Ordinary, important: false, 0);
        _pending.AddStatus(NoDevice, 100);

        Assert.Equal([NoDevice, Ordinary], TakeAll(200));
    }

    [Fact]
    public void An_ordinary_clip_can_run_out_of_time_behind_a_status()
    {
        _pending.AddClip(Ordinary, important: false, 0);
        _pending.AddStatus(NoDevice, 100);

        Assert.Equal(NoDevice, _pending.TakeNext(29_000));
        Assert.Null(_pending.TakeNext(31_000));
    }

    [Fact]
    public void The_last_status_replaces_the_one_before()
    {
        _pending.AddStatus(CaptureOn, 0);
        _pending.AddStatus(NoDevice, 100);

        Assert.Equal([NoDevice], TakeAll(200));
    }

    [Fact]
    public void A_waiting_no_device_goes_when_the_device_comes_back()
    {
        _pending.AddStatus(NoDevice, 0);

        Assert.Null(_pending.WithdrawCeased(Status(CaptureActivity.NoDevice)));
        Assert.True(_pending.Any);

        Assert.Equal(NoDevice, _pending.WithdrawCeased(Status(CaptureActivity.Capturing)));
        Assert.False(_pending.Any);
    }

    [Fact]
    public void A_waiting_mic_lost_goes_when_the_mic_comes_back_or_is_turned_off()
    {
        _pending.AddStatus(MicLost, 0);
        Assert.Null(_pending.WithdrawCeased(Status(CaptureActivity.Capturing, micCapturing: false)));
        Assert.Equal(MicLost, _pending.WithdrawCeased(Status(CaptureActivity.Capturing, micCapturing: true)));

        _pending.AddStatus(MicLost, 0);
        Assert.Equal(MicLost, _pending.WithdrawCeased(Status(CaptureActivity.Capturing, micEnabled: false, micCapturing: false)));
    }

    [Fact]
    public void A_waiting_capture_on_goes_when_the_capture_is_paused()
    {
        _pending.AddStatus(CaptureOn, 0);
        Assert.Null(_pending.WithdrawCeased(Status(CaptureActivity.Capturing)));
        Assert.Equal(CaptureOn, _pending.WithdrawCeased(Status(CaptureActivity.Paused)));
    }

    [Fact]
    public void Withdrawing_a_status_touches_no_clip()
    {
        _pending.AddClip(Failed, important: true, 0);
        _pending.AddClip(Ordinary, important: false, 0);

        Assert.Null(_pending.WithdrawCeased(Status(CaptureActivity.Capturing)));

        Assert.True(_pending.ImportantClipWaiting);
        Assert.True(_pending.Any);
    }

    [Fact]
    public void A_newer_clip_on_screen_ends_the_wait_of_an_ordinary_clip_only()
    {
        _pending.AddClip(Ordinary, important: false, 0);
        _pending.NewerClipShown();
        Assert.False(_pending.Any);

        _pending.AddClip(Failed, important: true, 0);
        _pending.NewerClipShown();
        Assert.True(_pending.ImportantClipWaiting);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(29_999, 500)]
    [InlineData(30_000, 2000)]
    [InlineData(3_600_000, 2000)]
    public void The_pace_drops_after_30_s(long elapsed, int delay)
    {
        _pending.AddClip(Failed, important: true, 1000);

        Assert.Equal(delay, _pending.PollDelayMs(1000 + elapsed));
    }

    [Fact]
    public void A_new_arrival_brings_the_fast_pace_back()
    {
        _pending.AddClip(Failed, important: true, 0);
        Assert.Equal(2000, _pending.PollDelayMs(40_000));

        _pending.AddStatus(NoDevice, 40_000);
        Assert.Equal(500, _pending.PollDelayMs(40_100));
    }

    [Theory]
    [InlineData(K.CaptureOn, true)]
    [InlineData(K.NoDevice, true)]
    [InlineData(K.MicLost, true)]
    [InlineData(K.Paused, false)]
    [InlineData(K.DeviceChanged, false)]
    [InlineData(K.MicBack, false)]
    [InlineData(K.Saving, false)]
    public void Which_statuses_are_important(K kind, bool important)
    {
        Assert.Equal(important, PendingNotices.IsImportantStatus(kind));
    }
}
