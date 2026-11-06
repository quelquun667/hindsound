using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>What a request to quit comes to.</summary>
internal enum LeaveStep
{
    LeaveNow,
    WaitForClips,
}

/// <summary>
/// Quit, from the menu or from WM_CLOSE, while a clip is being written. Leaving at once would cut the file, so the app stays
/// until the last clip is done, for <see cref="WaitLimit"/> at most, and a second Quit means now. Pure: tested without any window.
/// </summary>
internal sealed class LeaveFlow
{
    public static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(5);

    /// <summary>Quit was asked for and clips are still being written.</summary>
    public bool Waiting { get; private set; }

    /// <summary>Nothing that would keep the user busy opens on an app that is on its way out.</summary>
    public bool MayOpenSettings => !Waiting;

    public LeaveStep Asked(bool clipsBusy)
    {
        if (Waiting || !clipsBusy)
        {
            return LeaveStep.LeaveNow;
        }

        Waiting = true;
        return LeaveStep.WaitForClips;
    }

    /// <summary>A clip has ended. True when that was what the app stayed for.</summary>
    public bool ClipEnded(bool clipsBusy) => Waiting && !clipsBusy;

    /// <summary>The limit has come. True when the app was still waiting then.</summary>
    public bool TimeIsUp() => Waiting;

    /// <summary>How long the app stays for a problem that is on screen, if the notification never says it has left.</summary>
    public const int NoticeHoldMs = 5000;

    private bool _holdingForNotice;

    /// <summary>What the user must read before the app goes: a clip that went wrong, was rescued, or is silent. A good clip needs nothing: the closing message announced it.</summary>
    public static bool IsProblem(NotificationKind kind, bool important) => important || kind == NotificationKind.Silent;

    /// <summary>The last clip has ended. True when a problem is still to be read, so the app stays for it: <see cref="NoticeGone"/> or <see cref="NoticeLimit"/> then says when to go. A second Quit and the limit of the wait still apply.</summary>
    /// <param name="lastClipHasProblem">The last clip ended with a problem.</param>
    /// <param name="onScreen">A notification is on screen.</param>
    /// <param name="anyWaiting">Something waits for the desktop.</param>
    /// <param name="importantClipWaiting">What waits for the desktop includes a clip with a problem, the last one or not.</param>
    public bool HoldForNotice(bool lastClipHasProblem, bool onScreen, bool anyWaiting, bool importantClipWaiting)
    {
        _holdingForNotice = Waiting && (importantClipWaiting || (lastClipHasProblem && (onScreen || anyWaiting)));
        return _holdingForNotice;
    }

    /// <summary>A notification has left, or what waited was given up on. True when the app was only staying for that.</summary>
    public bool NoticeGone(bool onScreen, bool anyWaiting) => _holdingForNotice && !onScreen && !anyWaiting;

    /// <summary>The problem has been on screen long enough. True unless something else still waits for the desktop.</summary>
    public bool NoticeLimit(bool anyWaiting) => _holdingForNotice && !anyWaiting;

    /// <summary>The last clip has ended, with what waits for the desktop as it is. Only a clip can keep the app: "Saving your clip" goes now, and another status can be lost with the app.</summary>
    /// <param name="lastClipHasProblem">The last clip ended with a problem.</param>
    /// <param name="onScreen">A notification is on screen.</param>
    /// <param name="pending">What waits for the desktop.</param>
    public bool HoldForNotice(bool lastClipHasProblem, bool onScreen, PendingNotices pending)
    {
        pending.LastClipDone();
        return HoldForNotice(lastClipHasProblem, onScreen, pending.ClipWaiting, pending.ImportantClipWaiting);
    }

    /// <summary>See <see cref="NoticeGone(bool, bool)"/>: only a clip that waits keeps the app.</summary>
    public bool NoticeGone(bool onScreen, PendingNotices pending) => NoticeGone(onScreen, pending.ClipWaiting);

    /// <summary>See <see cref="NoticeLimit(bool)"/>: only a clip that waits keeps the app.</summary>
    public bool NoticeLimit(PendingNotices pending) => NoticeLimit(pending.ClipWaiting);
}

/// <summary>
/// Whether Windows is told why it has to wait at the end of the session. The app always answers that the session may end:
/// the reason only tells the user, on the screen Windows shows, that a clip is on its way to the disk. Pure: tested without any window.
/// </summary>
internal sealed class SessionEndBlock
{
    public const string Reason = "Saving your clip";

    /// <summary>The clips get this much in all at the end of a session: Windows does not wait long.</summary>
    public static readonly TimeSpan ClipsLimit = TimeSpan.FromSeconds(15);

    private bool _held;

    /// <summary>Windows asks if the session may end. True when the reason has to be given now.</summary>
    public bool Asked(bool clipsBusy)
    {
        if (!clipsBusy || _held)
        {
            return false;
        }

        _held = true;
        return true;
    }

    /// <summary>The clips have had their time, or the session goes on after all. True when the reason has to be taken back.</summary>
    public bool Over()
    {
        var held = _held;
        _held = false;
        return held;
    }
}
