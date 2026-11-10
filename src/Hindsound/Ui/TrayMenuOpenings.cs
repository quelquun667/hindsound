using Hindsound.App;

namespace Hindsound.Ui;

/// <summary>
/// What the icon remembers between a message and its menu: where the right click was, and how many menus opened. A menu the
/// app opened tells its command itself, so the message must not name one as well: its wParam is the point of the click there.
/// </summary>
internal sealed class TrayMenuOpenings
{
    private nuint _at;
    private int _opens;

    /// <summary>Hands one message of the icon to the app, as <see cref="TrayMessages.Route"/> does.</summary>
    public void Receive(uint code, nuint wParam, Action<TrayGesture>? gesture, Action<TrayCommand>? chosen)
    {
        if (TrayMessages.GestureOf(code) == TrayGesture.Menu)
        {
            _at = wParam;
        }

        TrayMessages.Route(code, wParam, gesture, () => _opens, chosen);
    }

    /// <summary>A menu opens: where the right click was, in screen coordinates.</summary>
    public (int X, int Y) Open()
    {
        _opens++;
        return TrayMessages.PointOf(_at);
    }
}
