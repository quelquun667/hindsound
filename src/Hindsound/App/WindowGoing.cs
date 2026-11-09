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
