using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>A notice leaves the line once shown or given up on: nothing else goes with it.</summary>
public class NoticeQueueRemoveTests
{
    private static readonly NotificationContent Damaged = NotificationStates.SettingsDamaged(copyKept: true);
    private static readonly NotificationContent Paused = NotificationStates.Paused();
    private static readonly NotificationContent CaptureOn = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10");

    private readonly NoticeQueue _queue = new();

    [Fact]
    public void Removing_a_status_that_was_replaced_leaves_the_newer_one()
    {
        _queue.Add(Paused);
        _queue.Add(CaptureOn);

        _queue.Remove(Paused);

        Assert.Same(CaptureOn, _queue.Next());
    }

    [Fact]
    public void Removing_a_lasting_notice_leaves_the_status()
    {
        _queue.Add(Damaged);
        _queue.Add(Paused);

        _queue.Remove(Damaged);

        Assert.Same(Paused, _queue.Next());
    }

    [Fact]
    public void Removing_the_status_leaves_it_gone()
    {
        _queue.Add(Paused);

        _queue.Remove(Paused);

        Assert.Null(_queue.Next());
    }
}
