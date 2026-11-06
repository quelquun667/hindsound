using Hindsound.Audio;
using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>
/// What waits for the return to the desktop, because no window may go over a game in exclusive full screen.
/// Three places, so nothing here grows however long the game lasts. Pure: tested without any window.
/// </summary>
/// <param name="journal">Where it says what it lets go of.</param>
internal sealed class PendingNotices(Action<string> journal)
{
    /// <summary>"Clip saved, paste it" means little after this long.</summary>
    public const long OrdinaryClipLimitMs = 30_000;

    /// <summary>The time after the last arrival during which the return to the desktop is looked for often.</summary>
    public const long FastPollingLimitMs = 30_000;

    public const int FastPollMs = 500;
    public const int SlowPollMs = 2000;

    private NotificationContent? _status;
    private NotificationContent? _importantClip;
    private NotificationContent? _ordinaryClip;
    private long _ordinaryClipSince;
    private long _lastArrival;

    /// <summary>Anything at all waits: without this, nothing looks at the screen.</summary>
    public bool Any => _status is not null || _importantClip is not null || _ordinaryClip is not null;

    public bool ImportantClipWaiting => _importantClip is not null;

    /// <summary>A status that must reach the user, whatever the notifications setting says, and even if it has to wait.</summary>
    public static bool IsImportantStatus(NotificationKind kind) =>
        kind is NotificationKind.CaptureOn or NotificationKind.NoDevice or NotificationKind.MicLost or NotificationKind.ClosingAfterClip;

    /// <summary>The last of a kind replaces the one before. An ordinary clip never takes the place of an important one.</summary>
    public void AddClip(NotificationContent content, bool important, long nowMs)
    {
        _lastArrival = nowMs;
        if (important)
        {
            if (_importantClip is { } older)
            {
                journal($"notification {older.Kind} replaced while waiting for the desktop: {older.Title} / {older.Body}");
            }

            _importantClip = content;
            return;
        }

        _ordinaryClip = content;
        _ordinaryClipSince = nowMs;
    }

    public void AddStatus(NotificationContent content, long nowMs)
    {
        _lastArrival = nowMs;
        _status = content;
    }

    /// <summary>The waiting status, once what it says is no longer true. It is returned, or null if nothing changed.</summary>
    public NotificationContent? WithdrawCeased(AudioEngineStatus status)
    {
        if (_status is not { } waiting || !StatusTransitions.HasCeased(waiting.Kind, status))
        {
            return null;
        }

        _status = null;
        journal($"notification {waiting.Kind} no longer waits: it stopped being true");
        return waiting;
    }

    /// <summary>A newer clip is on screen: an older ordinary one has nothing left to say.</summary>
    public void NewerClipShown() => _ordinaryClip = null;

    /// <summary>Lets go of an ordinary clip that waited too long.</summary>
    public void GiveUpOld(long nowMs)
    {
        if (_ordinaryClip is { } clip && nowMs - _ordinaryClipSince >= OrdinaryClipLimitMs)
        {
            journal($"notification {clip.Kind} given up: no return to the desktop within {OrdinaryClipLimitMs / 1000} s");
            _ordinaryClip = null;
        }
    }

    /// <summary>
    /// The next one to show, now that the user is back: the status, then the important clip. The ordinary clip only if no
    /// important clip was waiting: it would come on top of a problem and say all is well.
    /// </summary>
    public NotificationContent? TakeNext(long nowMs)
    {
        GiveUpOld(nowMs);
        if (_status is { } status)
        {
            _status = null;
            return status;
        }

        if (_importantClip is { } important)
        {
            if (_ordinaryClip is { } dropped)
            {
                journal($"notification {dropped.Kind} given up: a clip with a problem comes first");
                _ordinaryClip = null;
            }

            _importantClip = null;
            return important;
        }

        var ordinary = _ordinaryClip;
        _ordinaryClip = null;
        return ordinary;
    }

    /// <summary>How long until the next look at the screen. A game can last for hours: the pace drops, it never stops.</summary>
    public int PollDelayMs(long nowMs) => PaceAfter(nowMs - _lastArrival);

    /// <summary>The pace of anything that looks at the screen while it waits for the desktop.</summary>
    public static int PaceAfter(long waitedMs) => waitedMs < FastPollingLimitMs ? FastPollMs : SlowPollMs;
}
