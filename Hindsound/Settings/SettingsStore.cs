using System.IO;
using System.Text.Json;

namespace Hindsound.Settings;

public enum SettingsLoadStatus
{
    NotLoaded,
    Loaded,
    Missing,

    /// <summary>The file could not be read as settings; it was kept next to the original as a .bak file.</summary>
    Corrupt,
}

/// <summary>Reads and writes settings.json. The path is a parameter so tests never touch the real %AppData%.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Keeps "+" and "&" readable instead of + in the file the user may edit by hand.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public SettingsStore(string? path = null)
    {
        Path = path ?? DefaultPath;
    }

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hindsound", "settings.json");

    public string Path { get; }

    public SettingsLoadStatus LastStatus { get; private set; }

    /// <summary>Path of the preserved copy when the last load found a broken file.</summary>
    public string? LastBackupPath { get; private set; }

    /// <summary>Never throws for a bad file: absent or broken gives defaults (a broken file is moved to a .bak first).</summary>
    public AppSettings Load()
    {
        LastBackupPath = null;
        if (!File.Exists(Path))
        {
            LastStatus = SettingsLoadStatus.Missing;
            return new AppSettings().Normalize();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options);
            if (settings is not null)
            {
                LastStatus = SettingsLoadStatus.Loaded;
                return settings.Normalize();
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Fall through to the broken-file handling below.
        }

        LastStatus = SettingsLoadStatus.Corrupt;
        LastBackupPath = KeepBrokenFile();
        return new AppSettings().Normalize();
    }

    /// <summary>Writes the settings atomically: temporary file in the same folder, then replace.</summary>
    public void Save(AppSettings settings)
    {
        settings.Normalize();
        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        Directory.CreateDirectory(folder);

        var temp = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(Path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            if (File.Exists(Path))
            {
                File.Replace(temp, Path, null);
            }
            else
            {
                File.Move(temp, Path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    // Never overwrites an earlier .bak: settings.json.bak, then settings.json.1.bak, .2.bak, ...
    private string? KeepBrokenFile()
    {
        var target = Path + ".bak";
        for (var i = 1; File.Exists(target); i++)
        {
            target = $"{Path}.{i}.bak";
        }

        try
        {
            File.Move(Path, target);
            return target;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
