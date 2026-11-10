using System.IO;
using Microsoft.Win32;

namespace Hindsound.Settings;

/// <summary>Values of the current user's Run key, by name. Faked in tests, which must never write the real one.</summary>
public interface IRunKeyValues
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}

/// <summary>
/// The startup entry of Windows: one value of HKCU\Software\Microsoft\Windows\CurrentVersion\Run, named
/// <see cref="DefaultValueName"/>, that holds the quoted path of the exe.
/// </summary>
/// <param name="values">The Run key itself when null. Given by the tests only.</param>
/// <param name="valueName">The name of the value.</param>
public sealed class RegistryRunKey(IRunKeyValues? values = null, string valueName = RegistryRunKey.DefaultValueName) : IRunKey
{
    public const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string DefaultValueName = "Hindsound";

    private readonly IRunKeyValues _values = values ?? new CurrentUserRunValues();

    /// <summary>The command written to the key: the quoted path of the exe.</summary>
    public static string CommandFor(string program) => $"\"{program}\"";

    public RunEntry State(string? program)
    {
        var value = _values.GetValue(valueName);
        if (value is null)
        {
            return RunEntry.None;
        }

        // Windows paths ignore case.
        return program is not null && string.Equals(value, CommandFor(program), StringComparison.OrdinalIgnoreCase)
            ? RunEntry.ThisProgram
            : RunEntry.OtherProgram;
    }

    public void Enable(string program) => _values.SetValue(valueName, CommandFor(program));

    public void Disable() => _values.DeleteValue(valueName);

    private sealed class CurrentUserRunValues : IRunKeyValues
    {
        public string? GetValue(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey);
            return key?.GetValue(name) as string;
        }

        public void SetValue(string name, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey);
            key.SetValue(name, value, RegistryValueKind.String);
        }

        public void DeleteValue(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
