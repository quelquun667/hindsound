using System.IO;

namespace Hindsound.Settings;

/// <summary>What the startup entry of the session says about a program.</summary>
public enum RunEntry
{
    /// <summary>There is no entry.</summary>
    None,

    /// <summary>The entry starts this program.</summary>
    ThisProgram,

    /// <summary>The entry starts another path (a stale one, from a moved install): it counts as off.</summary>
    OtherProgram,
}

/// <summary>
/// The entry that starts the app with the user's session: the Run key on Windows, an autostart file elsewhere. Faked in tests.
/// A refusal throws <see cref="UnauthorizedAccessException"/>, <see cref="System.Security.SecurityException"/> or <see cref="IOException"/>.
/// </summary>
public interface IRunKey
{
    /// <summary>Whether the entry is there, and whether it starts <paramref name="program"/>.</summary>
    RunEntry State(string? program);

    /// <summary>Makes the entry start <paramref name="program"/>, in place of whatever it held.</summary>
    void Enable(string program);

    /// <summary>Removes the entry. Nothing to remove is fine.</summary>
    void Disable();
}

/// <summary>Launch with the user's session, through the platform's startup entry.</summary>
public sealed class StartupRegistration
{
    private readonly IRunKey _key;
    private readonly string? _exePath;

    public StartupRegistration(IRunKey key, string? exePath = null)
    {
        _key = key;
        _exePath = exePath ?? App.PlatformDefaults.ResolveLaunchPath();
    }

    /// <summary>The program the entry starts: this exe, or null when its path is unknown.</summary>
    public string? Program => string.IsNullOrWhiteSpace(_exePath) ? null : _exePath;

    /// <summary>True if the entry starts this exe (a stale path from a moved install counts as off).</summary>
    public bool IsEnabled() => Program is { } program && _key.State(program) == RunEntry.ThisProgram;

    /// <summary>Whether there is an entry at all, for this exe or not.</summary>
    public bool HasEntry() => _key.State(Program) != RunEntry.None;

    /// <summary>Returns false if the exe path is unknown or the system refused.</summary>
    public bool Enable()
    {
        if (Program is not { } program)
        {
            return false;
        }

        try
        {
            _key.Enable(program);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    public bool Disable()
    {
        try
        {
            _key.Disable();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Makes the entry match the setting. Returns false if it could not.</summary>
    public bool Apply(bool startWithWindows) => startWithWindows ? (IsEnabled() || Enable()) : Disable();
}
