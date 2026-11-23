using Hindsound.Library;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.App;

/// <summary>What the gestures need from the screen of the window: nothing but where it is and where to send it.</summary>
internal interface IWindowScreen
{
    Section Section { get; }

    void ShowSection(Section section);

    void ShowUpdates();

    void ShowUpdateCard();

    void SelectClip(string path);
}

/// <summary>A window that can open its editor on a clip, and say whether the editor it shows holds something to keep.</summary>
internal interface IEditorWindowScreen : IWindowScreen
{
    /// <summary>The editor is open with a change or a save under way: opening it on another clip would lose that.</summary>
    bool EditorHoldsWork { get; }

    /// <summary>Whether the window can open an editor at all.</summary>
    bool HasEditor { get; }

    /// <summary>Opens the editor on the clip, for a normal edit.</summary>
    void OpenEditorOn(ClipGroup clip);
}

/// <summary>What <see cref="WindowGoing.OpenEditor"/> did.</summary>
internal enum EditorOpening
{
    /// <summary>The editor is open on the clip.</summary>
    Opened,

    /// <summary>The window came to the front on what it shows: the editor, a card or its own business held something to keep.</summary>
    KeptWhatItShows,

    /// <summary>Nothing opened: the app is leaving, or this system has no editor.</summary>
    NotOpened,
}

/// <summary>
/// Carries out the route of a gesture: the table of <see cref="WindowRoutes"/> says where to go, this does it, on a window that is
/// already open as well as on a closed one. Apart from the app, so that the table is tested to be what the app does.
/// </summary>
internal sealed class WindowGoing
{
    private readonly Func<bool> _mayOpen;
    private readonly Func<IWindowScreen?> _current;
    private readonly Action _bringBack;
    private readonly Action<Section> _create;
    private readonly Action<string> _journal;

    public WindowGoing(Func<bool> mayOpen, Func<IWindowScreen?> current, Action bringBack, Action<Section> create, Action<string> journal)
    {
        _mayOpen = mayOpen;
        _current = current;
        _bringBack = bringBack;
        _create = create;
        _journal = journal;
    }

    /// <summary>The settings page seen last: where the Settings entry of the menu goes back to.</summary>
    public Section LastSettingsPage { get; private set; } = Section.Hotkeys;

    public void SettingsPageSeen(Section page) => LastSettingsPage = page;

    /// <summary>A click on the notification on screen.</summary>
    public void Click(Hindsound.Ui.NotificationContent? showing) => Go(WindowRoutes.ClickOn(showing), showing?.ClipPath);

    /// <param name="gesture">What the user did.</param>
    /// <param name="clip">The clip the gesture is about, chosen if the window ends up on the Library page: a card is not left for it.</param>
    public void Go(WindowGesture gesture, string? clip = null)
    {
        var screen = _current();
        var route = WindowRoutes.Of(gesture, screen?.Section, LastSettingsPage);
        _journal($"window gesture {gesture}: {route.Action} {route.Section}, the window was on {screen?.Section.ToString() ?? "nothing"}");
        Open(route.Section ?? Section.Now);
        if (route.Action == WindowAction.ShowUpdates)
        {
            _current()?.ShowUpdates();
        }
        else if (route.Action == WindowAction.ShowUpdateCard)
        {
            _current()?.ShowUpdateCard();
        }

        if (clip is not null && _current() is { Section: Section.Library } shown)
        {
            shown.SelectClip(clip);
        }
    }

    /// <summary>
    /// Opens the editor on a clip that was just saved, bringing the window to the front. What the window already shows is never
    /// replaced when it holds something to keep: an editor with a change or a save under way, or a card (the first-launch one would
    /// lose its steps, and the update one is a question being asked). The window then only comes to the front, as for any gesture.
    /// </summary>
    public EditorOpening OpenEditor(ClipGroup clip)
    {
        if (!_mayOpen())
        {
            _journal("editor not opened: the app is leaving");
            return EditorOpening.NotOpened;
        }

        if (_current() is { } open)
        {
            if (open is not IEditorWindowScreen { HasEditor: true } editable)
            {
                _journal("editor not opened: this window has no editor");
                return EditorOpening.NotOpened;
            }

            var why = editable.EditorHoldsWork
                ? "the editor holds changes or a save in progress"
                : editable.Section is Section.FirstRun or Section.UpdateCard ? "the window shows a card" : null;
            _bringBack();
            if (why is not null)
            {
                _journal($"editor not opened: {why}; the window came to the front on what it shows");
                return EditorOpening.KeptWhatItShows;
            }

            editable.OpenEditorOn(clip);
            return EditorOpening.Opened;
        }

        _create(Section.Library);
        if (_current() is IEditorWindowScreen { HasEditor: true } made)
        {
            made.OpenEditorOn(clip);
            return EditorOpening.Opened;
        }

        _journal("editor not opened: no window with an editor on this system");
        return EditorOpening.NotOpened;
    }

    /// <summary>Only one at a time: an open window is brought back and moved to the page, a closed one is made.</summary>
    /// <param name="target">The view to land on. <see cref="Section.FirstRun"/> opens the first-launch card.</param>
    public void Open(Section target)
    {
        if (!_mayOpen())
        {
            return;
        }

        if (_current() is { } open)
        {
            _bringBack();
            open.ShowSection(target);
            return;
        }

        _create(target);
    }
}
