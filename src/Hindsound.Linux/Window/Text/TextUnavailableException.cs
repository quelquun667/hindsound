namespace Hindsound.Linux.Window.Text;

/// <summary>
/// The text of the window cannot start: a library of the system is missing, or a font the app ships. The message names the
/// library or the font, never a path, so it can go to the journal as it is; the app then runs without a window.
/// </summary>
internal sealed class TextUnavailableException : Exception
{
    public TextUnavailableException(string message)
        : base(message)
    {
    }

    public TextUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
