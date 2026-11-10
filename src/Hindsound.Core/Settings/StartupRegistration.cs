using System.IO;

namespace Hindsound.Settings;

/// <summary>Reads and writes values of the current user's Run key. Faked in tests.</summary>
public interface IRunKey
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}

/// <summary>Launch at Windows startup, through the Run key.</summary>
public sealed class StartupRegistration
{
    public const string DefaultValueName = "Hindsound";

    private readonly IRunKey _key;
    private readonly string? _exePath;
    private readonly string _valueName;

    public StartupRegistration(IRunKey key, string? exePath = null, string valueName = DefaultValueName)
    {
        _key = key;
        _exePath = exePath ?? Environment.ProcessPath;
        _valueName = valueName;
    }

    /// <summary>The command written to the key: the quoted path of the exe.</summary>
    public string? Command => string.IsNullOrWhiteSpace(_exePath) ? null : $"\"{_exePath}\"";

    /// <summary>True if the key holds the command for this exe (a stale path from a moved install counts as off).</summary>
    public bool IsEnabled() => Command is not null && string.Equals(_key.GetValue(_valueName), Command, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns false if the exe path is unknown or the registry refused.</summary>
    public bool Enable()
    {
        if (Command is null)
        {
            return false;
        }

        try
        {
            _key.SetValue(_valueName, Command);
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
            _key.DeleteValue(_valueName);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Makes the registry match the setting. Returns false if it could not.</summary>
    public bool Apply(bool startWithWindows) => startWithWindows ? (IsEnabled() || Enable()) : Disable();
}
