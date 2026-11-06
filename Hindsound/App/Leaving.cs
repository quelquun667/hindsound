namespace Hindsound.App;

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
