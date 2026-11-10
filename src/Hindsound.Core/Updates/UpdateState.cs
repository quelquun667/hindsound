using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hindsound.Updates;

/// <summary>The Setup waiting on disk. Its size and hash travel with it: at launch there is no manifest to compare with.</summary>
internal sealed record PendingSetup(string Version, string Sha256, long Size);

/// <summary>updates\state.json: the technical state of the updater. The user's choices stay in settings.json.</summary>
internal sealed record UpdateStateData
{
    public DateTimeOffset? LastCheck { get; init; }

    public string? HighestSeen { get; init; }

    public Dictionary<string, int>? AttemptsByVersion { get; init; }

    /// <summary>The version the last started Setup should have installed. Compared with the running one at launch.</summary>
    public string? ExpectedVersion { get; init; }

    /// <summary>One notification per version, even across restarts.</summary>
    public string? AnnouncedVersion { get; init; }

    public PendingSetup? Pending { get; init; }

    /// <summary>Raises the anti-rollback floor. It never goes down.</summary>
    public UpdateStateData WithSeen(UpdateVersion version) =>
        UpdateVersion.TryParse(HighestSeen, out var highest) && highest >= version ? this : this with { HighestSeen = version.ToString() };

    public int AttemptsFor(UpdateVersion version) =>
        AttemptsByVersion is not null && AttemptsByVersion.TryGetValue(version.ToString(), out var attempts) ? attempts : 0;

    /// <summary>The record written just before Setup starts: one more attempt, and the version to expect at the next launch.</summary>
    public UpdateStateData WithAttempt(UpdateVersion version)
    {
        var attempts = AttemptsByVersion is null ? new Dictionary<string, int>() : new Dictionary<string, int>(AttemptsByVersion);
        attempts[version.ToString()] = AttemptsFor(version) + 1;
        return this with { AttemptsByVersion = attempts, ExpectedVersion = version.ToString() };
    }
}

// Generated at build time: the published exe is trimmed and has no reflection-based serializer.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(UpdateStateData))]
internal sealed partial class UpdateStateJson : JsonSerializerContext;

/// <summary>
/// Reads and writes state.json. A broken or locked file never throws: reading gives an empty state, and writing says it
/// failed, because the caller must not start Setup on an attempt that was not recorded.
/// </summary>
internal sealed class UpdateStateStore(string path)
{
    // A state of a few hundred bytes: anything far larger is not ours, and is not worth the memory.
    private const long MaxStateBytes = 64 * 1024;

    public static string DefaultFolder { get; } = Hindsound.Settings.UserFolders.Under(
        Environment.SpecialFolder.LocalApplicationData, "Hindsound", "updates");

    public UpdateStateData Load() => TryLoad(out var state) ? state : new UpdateStateData();

    /// <summary>
    /// False only when the file is there and cannot be read now, locked or refused: an empty state saved over it would
    /// give back the counted attempts and the anti-rollback floor. No file, no folder, or a broken file is a known empty state.
    /// </summary>
    public bool TryLoad(out UpdateStateData state)
    {
        state = new UpdateStateData();
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaxStateBytes)
            {
                return true;
            }

            var read = JsonSerializer.Deserialize(file, UpdateStateJson.Default.UpdateStateData);
            if (read is not null)
            {
                state = Sanitized(read);
            }

            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            // Both not-found exceptions are IOExceptions too. Taken for a lock, they would stop the updater on every new
            // install, where the updates folder does not exist yet.
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Atomic: the old file stays whole until the new one is complete on disk.</summary>
    public bool Save(UpdateStateData state)
    {
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, state, UpdateStateJson.Default.UpdateStateData);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UpdateFiles.TryDelete(temporary);
            return false;
        }
    }

    // A hand-edited count below zero would hand out extra attempts.
    private static UpdateStateData Sanitized(UpdateStateData state)
    {
        if (state.AttemptsByVersion is null)
        {
            return state;
        }

        var attempts = new Dictionary<string, int>();
        foreach (var (version, count) in state.AttemptsByVersion)
        {
            if (count > 0)
            {
                attempts[version] = count;
            }
        }

        return state with { AttemptsByVersion = attempts };
    }
}
