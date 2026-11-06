using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>How much a notification may push another one aside.</summary>
public enum NoticeRank
{
    /// <summary>A status: replaced in place by anything, and waits behind a notification that must stay.</summary>
    Ordinary,

    /// <summary>The answer to something the user just did: shown at once, and not cut short.</summary>
    Answer,

    /// <summary>Says something about the settings that must be read: it goes through its whole life, one after the other.</summary>
    Lasting,
}

/// <summary>
/// When a lasting notification that can't show yet looks again. It stays in line until it has shown, so it keeps looking at
/// the pace of what waits for the desktop. Pure: tested without any window.
/// </summary>
internal sealed class NoticeRetry
{
    private bool _armed;
    private long? _since;

    /// <summary>The delay to wait before the next look, or null if one is already on its way.</summary>
    public int? Arm(long nowMs)
    {
        if (_armed)
        {
            return null;
        }

        _armed = true;
        _since ??= nowMs;
        return PendingNotices.PaceAfter(nowMs - _since.Value);
    }

    /// <summary>The look is happening.</summary>
    public void Fired() => _armed = false;

    /// <summary>It has shown: the next one starts at the fast pace again.</summary>
    public void Shown() => _since = null;
}

/// <summary>
/// Which notification comes next. The notification window replaces what it shows in place, so "Capture on", arriving a
/// moment after the start, would wipe out the news that the settings were lost. Pure: tested without any window.
/// </summary>
internal sealed class NoticeQueue
{
    private readonly List<NotificationContent> _lasting = [];
    private NotificationContent? _ordinary;
    private NotificationContent? _onScreen;

    private bool Protected => _onScreen is { } shown && RankOf(shown.Kind) != NoticeRank.Ordinary;

    public static NoticeRank RankOf(NotificationKind kind) => kind switch
    {
        NotificationKind.SettingsDamaged or NotificationKind.SettingsUnreadable or NotificationKind.SettingsHasError or NotificationKind.SettingsNotSaved
            or NotificationKind.StartupNotChanged => NoticeRank.Lasting,
        NotificationKind.NoDevice or NotificationKind.Paused or NotificationKind.CaptureOn or NotificationKind.DeviceChanged
            or NotificationKind.MicLost or NotificationKind.MicBack => NoticeRank.Ordinary,
        _ => NoticeRank.Answer,
    };

    /// <summary>Puts a notification in line, behind the others of its rank. Only the latest status is worth keeping.</summary>
    public void Add(NotificationContent content)
    {
        if (RankOf(content.Kind) == NoticeRank.Lasting)
        {
            _lasting.Add(content);
        }
        else
        {
            _ordinary = content;
        }
    }

    /// <summary>
    /// Whether a notification that is not a lasting one shows right now. Otherwise it was put in line. An answer pushes a
    /// lasting one aside, which starts over once the answer has left.
    /// </summary>
    public bool ShowsNow(NotificationContent content)
    {
        if (RankOf(content.Kind) == NoticeRank.Answer)
        {
            if (_onScreen is { } shown && RankOf(shown.Kind) == NoticeRank.Lasting)
            {
                _lasting.Insert(0, shown);
            }

            return true;
        }

        if (Protected)
        {
            Add(content);
            return false;
        }

        return true;
    }

    /// <summary>The one to show next: null if none, or if what is on screen must stay. It stays in line until <see cref="Remove"/>.</summary>
    public NotificationContent? Next()
    {
        if (Protected)
        {
            return null;
        }

        return _lasting.Count > 0 ? _lasting[0] : _ordinary;
    }

    /// <summary>It was shown or given up on.</summary>
    public void Remove(NotificationContent content)
    {
        if (!_lasting.Remove(content) && ReferenceEquals(_ordinary, content))
        {
            _ordinary = null;
        }
    }

    public void Shown(NotificationContent content) => _onScreen = content;

    /// <summary>The notification has left the screen.</summary>
    public void Finished() => _onScreen = null;
}
