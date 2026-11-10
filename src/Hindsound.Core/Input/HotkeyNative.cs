namespace Hindsound.Input;

/// <summary>Outcome of one registration attempt at the native layer.</summary>
public enum HotkeyRegistration
{
    /// <summary>The shortcut is registered under the given id.</summary>
    Registered,

    /// <summary>Windows refused it (another app holds the combination).</summary>
    Refused,

    /// <summary>
    /// The native layer did not answer in time. The request was cancelled, or undone if it had already run: nothing
    /// stays registered under the id.
    /// </summary>
    TimedOut,
}

/// <summary><c>--no-hotkeys</c>: says yes to every shortcut and registers none. Nothing on the keyboard can reach this run.</summary>
public sealed class UnregisteredHotkeys : IHotkeyNative
{
    public event Action<int>? HotkeyPressed { add { } remove { } }

    public bool Register(int id, uint modifiers, uint virtualKey) => true;

    public void Unregister(int id)
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>The Win32 side of the shortcuts, so tests can replace it.</summary>
public interface IHotkeyNative : IDisposable
{
    /// <summary>Raised with the id of a pressed shortcut, on the native layer's own thread.</summary>
    event Action<int>? HotkeyPressed;

    bool Register(int id, uint modifiers, uint virtualKey);

    /// <summary>
    /// Like <see cref="Register"/> but tells a refusal from a timeout. The default maps <see cref="Register"/>, so
    /// existing implementations keep working.
    /// </summary>
    HotkeyRegistration TryRegister(int id, uint modifiers, uint virtualKey) =>
        Register(id, modifiers, virtualKey) ? HotkeyRegistration.Registered : HotkeyRegistration.Refused;

    void Unregister(int id);
}
