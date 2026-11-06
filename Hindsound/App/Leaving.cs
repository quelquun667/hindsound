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
