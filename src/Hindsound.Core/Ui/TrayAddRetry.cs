namespace Hindsound.Ui;

/// <summary>
/// An icon refused by the shell, as at the opening of a session, when Explorer is not ready yet: when to try again and what
/// to write about it. Without an icon there is no way to quit with the mouse. Pure: tested without any window.
/// </summary>
internal sealed class TrayAddRetry
{
    public const int DelayMs = 2000;
    public const int MaxTries = 60; // 2 min

    private bool _failureWritten;
    private bool _armed;
    private int _tries;

    /// <summary>Takes the result of a call. Returns what the journal gets: one line at the failure, one at the success, nothing else.</summary>
    public string? Note(bool there)
    {
        if (there)
        {
            _tries = 0;
            if (!_failureWritten)
            {
                return null;
            }

            _failureWritten = false;
            return "the tray icon is there now";
        }

        if (_failureWritten)
        {
            return null;
        }

        _failureWritten = true;
        return $"the tray icon could not be added: trying again every {DelayMs / 1000} s, {MaxTries} times";
    }

    /// <summary>Whether to arm the next try now. The caller has <see cref="Fired"/> called when it runs.</summary>
    public bool ShouldArm(bool there)
    {
        if (there || _armed || _tries >= MaxTries)
        {
            return false;
        }

        _armed = true;
        _tries++;
        return true;
    }

    public void Fired() => _armed = false;
}
