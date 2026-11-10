using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>Which way out of the app may install the update waiting on disk.</summary>
internal static class UpdateExit
{
    /// <summary>
    /// Only Quit from the menu: closing the window from outside, --exit-after, the end of the session or an exception
    /// is never a moment the user picked to restart into a new version.
    /// </summary>
    public static bool ChoosesQuit(TrayCommand command) => command == TrayCommand.Quit;

    /// <summary>True when Setup started. The flow is asked only after Quit from the menu.</summary>
    public static bool InstallOnLeaving(bool quitChosen, Func<bool>? applyOnQuit) => quitChosen && applyOnQuit?.Invoke() == true;
}

internal enum SaveThenUpdateStep
{
    /// <summary>No "Save it, then update" is waiting.</summary>
    None,

    /// <summary>Another clip is still being written: a hotkey may have started one meanwhile.</summary>
    Wait,

    /// <summary>Every clip has a result: the click of the card goes through now.</summary>
    Update,

    /// <summary>One clip was not saved: nothing is updated, the memory it was meant to keep would be lost.</summary>
    ClipFailed,
}

/// <summary>The "Save it, then update" button of the card: the update waits for the clip, and gives up if it was not saved.</summary>
internal sealed class SaveThenUpdate
{
    private bool _anyFailed;

    public bool Waiting { get; private set; }

    /// <summary>The last wait ended on a clip that was not saved. The card says so until it closes or is used again.</summary>
    public bool ClipFailed { get; private set; }

    public void Start()
    {
        Waiting = true;
        ClipFailed = false;
        _anyFailed = false;
    }

    /// <summary>The window or the card closed: no update comes out of it any more.</summary>
    public void Stop()
    {
        Waiting = false;
        ClipFailed = false;
        _anyFailed = false;
    }

    /// <param name="saved">The clip that just ended has a file.</param>
    /// <param name="clipsInProgress">Another clip is still being written or copied.</param>
    public SaveThenUpdateStep ClipEnded(bool saved, bool clipsInProgress)
    {
        if (!Waiting)
        {
            return SaveThenUpdateStep.None;
        }

        _anyFailed |= !saved;
        if (clipsInProgress)
        {
            return SaveThenUpdateStep.Wait;
        }

        Waiting = false;
        ClipFailed = _anyFailed;
        return _anyFailed ? SaveThenUpdateStep.ClipFailed : SaveThenUpdateStep.Update;
    }
}
