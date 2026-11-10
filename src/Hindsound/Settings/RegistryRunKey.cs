using System.IO;
using Microsoft.Win32;

namespace Hindsound.Settings;

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
public sealed class RegistryRunKey : IRunKey
{
    public const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

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
