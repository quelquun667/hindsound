using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Hindsound.Settings;

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

public enum SettingsLoadStatus
{
    NotLoaded,
    Loaded,
    Missing,

    /// <summary>The file holds no valid settings. <see cref="SettingsStore.Load"/> kept it next to the original as a .bak file.</summary>
    Corrupt,

    /// <summary>The file is there but could not be opened (an editor, an antivirus, a sync tool). It was left as it is.</summary>
    Unreadable,
}

/// <summary>What one look at the file found. <paramref name="Settings"/> is null unless the status is <see cref="SettingsLoadStatus.Loaded"/>.</summary>
public readonly record struct SettingsRead(SettingsLoadStatus Status, AppSettings? Settings);

/// <summary>Reads and writes settings.json. The path is a parameter so tests never touch the real %AppData%.</summary>
public sealed class SettingsStore
{
    // Generated at build time: the published exe is trimmed and has no reflection-based serializer.
    private static readonly JsonTypeInfo<AppSettings> TypeInfo = new SettingsJsonContext(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Keeps "+" and "&" readable instead of + in the file the user may edit by hand.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    }).AppSettings;

    public SettingsStore(string? path = null)
    {
        Path = path ?? DefaultPath;
    }

    public static string DefaultPath { get; } = UserFolders.Under(
        Environment.SpecialFolder.ApplicationData, "Hindsound", "settings.json");

    public string Path { get; }

    public SettingsLoadStatus LastStatus { get; private set; }

    /// <summary>Path of the preserved copy when the last load found a broken file.</summary>
    public string? LastBackupPath { get; private set; }

    /// <summary>
    /// Never throws for a bad file: absent, broken or locked gives defaults. A broken file is moved to a .bak first; a
    /// locked one is left alone.
    /// </summary>
    public AppSettings Load()
    {
        LastBackupPath = null;
        var read = Read();
        LastStatus = read.Status;
        if (read.Status == SettingsLoadStatus.Corrupt)
        {
            LastBackupPath = KeepBrokenFile();
        }

        return read.Settings ?? new AppSettings().Normalize();
    }

    /// <summary>The same as <see cref="Load"/>, with another look at a locked file: what locks it often lets go within a moment.</summary>
    /// <param name="tries">How many looks at most.</param>
    /// <param name="wait">Called between two looks.</param>
    public AppSettings LoadPatiently(int tries, Action wait)
    {
        var settings = Load();
        for (var i = 1; i < tries && LastStatus == SettingsLoadStatus.Unreadable; i++)
        {
            wait();
            settings = Load();
        }

        return settings;
    }

    /// <summary>
    /// Looks at the file and changes nothing, neither on disk nor in this object: the file may be in the middle of an edit.
    /// Never throws for a bad or locked file.
    /// </summary>
    public SettingsRead Read()
    {
        try
        {
            var settings = JsonSerializer.Deserialize(File.ReadAllText(Path), TypeInfo);
            return settings is null
                ? new SettingsRead(SettingsLoadStatus.Corrupt, null)
                : new SettingsRead(SettingsLoadStatus.Loaded, settings.Normalize());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return new SettingsRead(SettingsLoadStatus.Corrupt, null);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new SettingsRead(SettingsLoadStatus.Missing, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsRead(SettingsLoadStatus.Unreadable, null);
        }
    }

    /// <summary>
    /// Copies the broken file to a .bak, for a write about to replace it. A copy, not a move: if that write fails, the file
    /// is still in its place. Null if the copy could not be made, in which case the file must not be replaced.
    /// </summary>
    public string? KeepCopyOfBrokenFile()
    {
        try
        {
            var target = AtomicFile.FreeBackupPath(Path);
            File.Copy(Path, target, overwrite: false);
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

    /// <summary>Writes the settings atomically: temporary file in the same folder, then replace.</summary>
    public void Save(AppSettings settings)
    {
        settings.Normalize();
        AtomicFile.Write(Path, JsonSerializer.Serialize(settings, TypeInfo));
    }

    private string? KeepBrokenFile()
    {
        try
        {
            var target = AtomicFile.FreeBackupPath(Path);
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
