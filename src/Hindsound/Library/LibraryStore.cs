using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hindsound.Settings;

namespace Hindsound.Library;

[JsonSerializable(typeof(LibraryData))]
internal sealed partial class LibraryJsonContext : JsonSerializerContext;

public enum LibraryStatus
{
    /// <summary>No file yet: nothing is labeled.</summary>
    Missing,

    Loaded,

    /// <summary>The file holds no valid library. It stays in place, and nothing replaces it short of <see cref="LibraryStore.StartOver"/>.</summary>
    Corrupt,

    /// <summary>The file is there but could not be opened (an editor, an antivirus, a sync tool). It was left as it is.</summary>
    Unreadable,

    /// <summary>A later version of the app wrote the file: readable as far as it goes, and never written over.</summary>
    TooNew,
}

/// <summary>library.json. Only what the user decided: a clip with no label has no entry, so the file stays small.</summary>
public sealed class LibraryData
{
    public int Version { get; set; } = LibraryStore.CurrentVersion;

    public List<LibraryCollection> Collections { get; set; } = [];

    public List<LibraryClip> Clips { get; set; } = [];
}

public sealed class LibraryCollection
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
}

/// <summary>A clip with a star or a collection.</summary>
public sealed class LibraryClip
{
    /// <summary>One file, or the two tracks of a clip saved as separate files.</summary>
    public List<LibraryFile> Files { get; set; } = [];

    public bool Favorite { get; set; }

    /// <summary>Ids, not names: renaming a collection changes one line.</summary>
    public List<string> Collections { get; set; } = [];

    /// <summary>When an action first found no file left. Null while a file is there.</summary>
    public DateTime? MissingSince { get; set; }

    internal bool IsBare => !Favorite && Collections.Count == 0;
}

/// <summary>Where a file was last seen. A class compared by reference: two tracks may look alike, and are two links.</summary>
public sealed class LibraryFile
{
    public string Path { get; set; } = "";

    public long Size { get; set; }

    public DateTime Written { get; set; }
}

/// <summary>What one look at the file found. <paramref name="Data"/> is never null: empty if the file gave nothing.</summary>
public sealed record LibraryRead(LibraryStatus Status, LibraryData Data)
{
    /// <summary>A file in any other state is the user's to sort out: it is shown as far as possible and not written over.</summary>
    public bool CanWrite => Status is LibraryStatus.Missing or LibraryStatus.Loaded;
}

public enum LibraryOutcome
{
    Done,

    /// <summary>The file may not be written in its state, see <see cref="LibraryRead.Status"/>. Nothing changed.</summary>
    ReadOnly,

    /// <summary>The write failed. The file is as it was.</summary>
    NotWritten,

    /// <summary>No name to give the collection.</summary>
    NameEmpty,

    /// <summary>Another collection has this name, in any case.</summary>
    NameTaken,

    /// <summary>No collection has this id: the file changed since it was read.</summary>
    UnknownCollection,

    /// <summary>The line has no file to know it by, so there is nothing to label. Nothing changed.</summary>
    NoFile,

    /// <summary>A fresh look at the folders no longer finds the clip of the line. Nothing was written. Told by <see cref="LibraryReads"/>, not by the store.</summary>
    ClipGone,

    /// <summary>
    /// The fresh look did not read the folder of the clip (held back for a save in progress, refused, absent), so the clip may well be
    /// there and is not called gone. Nothing was written. Told by <see cref="LibraryReads"/>, not by the store.
    /// </summary>
    FolderNotRead,
}

/// <summary>What an action did, and the library it leaves.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Library">The library as of now. If the outcome is not <see cref="LibraryOutcome.Done"/>, the file as it is, without the change that was asked for.</param>
/// <param name="CollectionId">The collection just created.</param>
public readonly record struct LibraryChange(LibraryOutcome Outcome, LibraryRead Library, string? CollectionId = null);

/// <summary>
/// Reads and writes library.json, next to the settings file, so a test run with its own settings file has its own library.
/// Keeps nothing between two calls: every action reads the file again, so a hand edit made in between is not lost, and a
/// closed window costs nothing. Nothing here throws for a bad file. The names and the paths in the file are private: only
/// fixed words reach the log, never an exception's message, which holds the full path.
/// </summary>
public sealed class LibraryStore
{
    public const int CurrentVersion = 1;

    // Generated at build time: the published exe is trimmed and has no reflection-based serializer.
    private static readonly JsonTypeInfo<LibraryData> TypeInfo = new LibraryJsonContext(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    }).LibraryData;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // One gate for every store, not one per instance: the app may make a store for each call, and a read on one thread must
    // not look at the file while an action on another replaces it (it would find it missing, or refuse to open it, and a
    // write that meets a reader fails). Reentrant, so an action can read under it. Held for file work only, never for long.
    private static readonly object Gate = new();

    private readonly Action<string> _journal;

    /// <param name="settingsPath">The settings file. The library goes in the same folder.</param>
    /// <param name="journal">Where failures are told.</param>
    public LibraryStore(string settingsPath, Action<string>? journal = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(settingsPath))!, "library.json");
        _journal = journal ?? (_ => { });
    }

    public string Path { get; }

    /// <summary>Looks at the file and changes nothing on disk, not even the date a clip went missing.</summary>
    public LibraryRead Read()
    {
        lock (Gate)
        {
            return ReadFile();
        }
    }

    private LibraryRead ReadFile()
    {
        try
        {
            var read = Parse(File.ReadAllText(Path));
            if (read.Status is LibraryStatus.Corrupt or LibraryStatus.TooNew)
            {
                _journal($"library: the file is {read.Status}, it is left as it is");
            }

            return read;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            _journal($"library: the file has an error, it is left as it is ({Describe(ex)})");
            return new LibraryRead(LibraryStatus.Corrupt, new LibraryData());
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new LibraryRead(LibraryStatus.Missing, new LibraryData());
        }
        catch (Exception ex)
        {
            _journal($"library: the file can't be read ({Describe(ex)})");
            return new LibraryRead(LibraryStatus.Unreadable, new LibraryData());
        }
    }

    /// <summary>A new collection, with no clip in it.</summary>
    public LibraryChange CreateCollection(string name, ClipScan scan, DateTime nowUtc)
    {
        string? created = null;
        var change = Change(scan, nowUtc, data =>
        {
            var clean = name.Trim();
            if (Refusal(data, clean, except: null) is { } refusal)
            {
                return refusal;
            }

            created = FreeId(data);
            data.Collections.Add(new LibraryCollection { Id = created, Name = clean });
            return LibraryOutcome.Done;
        });
        return change.Outcome == LibraryOutcome.Done ? change with { CollectionId = created } : change;
    }

    public LibraryChange RenameCollection(string id, string name, ClipScan scan, DateTime nowUtc) =>
        Change(scan, nowUtc, data =>
        {
            if (data.Collections.Find(c => c.Id == id) is not { } collection)
            {
                return LibraryOutcome.UnknownCollection;
            }

            var clean = name.Trim();
            if (Refusal(data, clean, except: collection) is { } refusal)
            {
                return refusal;
            }

            collection.Name = clean;
            return LibraryOutcome.Done;
        });

    /// <summary>Takes the collection off its clips. Their files and their stars stay.</summary>
    public LibraryChange DeleteCollection(string id, ClipScan scan, DateTime nowUtc) =>
        Change(scan, nowUtc, data =>
        {
            if (data.Collections.RemoveAll(c => c.Id == id) == 0)
            {
                return LibraryOutcome.UnknownCollection;
            }

            foreach (var clip in data.Clips)
            {
                clip.Collections.Remove(id);
            }

            data.Clips.RemoveAll(clip => clip.IsBare);
            return LibraryOutcome.Done;
        });

    /// <summary>Ticks or unticks a collection on a clip. A clip can be in several.</summary>
    public LibraryChange SetInCollection(ClipGroup clip, string id, bool inCollection, ClipScan scan, DateTime nowUtc) =>
        Change(scan, nowUtc, data =>
        {
            if (HasNoFile(clip))
            {
                return LibraryOutcome.NoFile;
            }

            if (!data.Collections.Exists(c => c.Id == id))
            {
                return LibraryOutcome.UnknownCollection;
            }

            Label(data, clip, entry =>
            {
                entry.Collections.Remove(id);
                if (inCollection)
                {
                    entry.Collections.Add(id);
                }
            });
            return LibraryOutcome.Done;
        });

    public LibraryChange SetFavorite(ClipGroup clip, bool favorite, ClipScan scan, DateTime nowUtc) =>
        Change(scan, nowUtc, data =>
        {
            if (HasNoFile(clip))
            {
                return LibraryOutcome.NoFile;
            }

            Label(data, clip, entry => entry.Favorite = favorite);
            return LibraryOutcome.Done;
        });

    /// <summary>
    /// The only way past a broken file, and only when the user asks for it: an edit may yet repair the file, and an action
    /// on the way never replaces it. The file goes to a .bak first, as a copy: if the write fails, it is still in its place.
    /// </summary>
    public LibraryChange StartOver()
    {
        lock (Gate)
        {
            var read = ReadFile();
            if (read.Status != LibraryStatus.Corrupt)
            {
                return new LibraryChange(LibraryOutcome.ReadOnly, read);
            }

            try
            {
                File.Copy(Path, AtomicFile.FreeBackupPath(Path), overwrite: false);
                var data = new LibraryData();
                AtomicFile.Write(Path, JsonSerializer.Serialize(data, TypeInfo));
                return new LibraryChange(LibraryOutcome.Done, new LibraryRead(LibraryStatus.Loaded, data));
            }
            catch (Exception ex)
            {
                _journal($"library: not written ({Describe(ex)})");
                return new LibraryChange(LibraryOutcome.NotWritten, read);
            }
        }
    }

    private static LibraryRead Parse(string text)
    {
        using var document = JsonDocument.Parse(text, DocumentOptions);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var number))
        {
            return new LibraryRead(LibraryStatus.Corrupt, new LibraryData());
        }

        if (number > CurrentVersion)
        {
            return new LibraryRead(LibraryStatus.TooNew, ReadableInLaterVersion(text));
        }

        return number >= 1 && Sound(JsonSerializer.Deserialize(text, TypeInfo)) is { } data
            ? new LibraryRead(LibraryStatus.Loaded, data)
            : new LibraryRead(LibraryStatus.Corrupt, new LibraryData());
    }

    // A later version may have changed anything. That is not an error in the file, only less to show.
    private static LibraryData ReadableInLaterVersion(string text)
    {
        try
        {
            return Sound(JsonSerializer.Deserialize(text, TypeInfo)) ?? new LibraryData();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return new LibraryData();
        }
    }

    /// <summary>The data in its usable form, or null if a part of it makes no sense: writing it back would lose that part.</summary>
    private static LibraryData? Sound(LibraryData? data)
    {
        if (data is null)
        {
            return null;
        }

        data.Collections ??= [];
        data.Clips ??= [];
        var ids = new HashSet<string>();
        foreach (var collection in data.Collections)
        {
            if (collection is null || string.IsNullOrEmpty(collection.Id) || string.IsNullOrWhiteSpace(collection.Name) || !ids.Add(collection.Id))
            {
                return null;
            }
        }

        foreach (var clip in data.Clips)
        {
            if (clip?.Files is not { Count: > 0 } || clip.Files.Exists(file => file is null || string.IsNullOrEmpty(file.Path)))
            {
                return null;
            }

            // A hand edit can leave the id of a collection that is gone: that label means nothing.
            clip.Collections = clip.Collections is null ? [] : [.. clip.Collections.Where(id => id is not null && ids.Contains(id)).Distinct()];
        }

        data.Clips.RemoveAll(clip => clip.IsBare);
        return data;
    }

    // An entry with no file would be written, then judged damaged at the next read, and the whole library with it.
    private static bool HasNoFile(ClipGroup clip) => clip.Files.Count == 0;

    private static LibraryOutcome? Refusal(LibraryData data, string name, LibraryCollection? except)
    {
        if (name.Length == 0)
        {
            return LibraryOutcome.NameEmpty;
        }

        return data.Collections.Exists(c => c != except && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            ? LibraryOutcome.NameTaken
            : null;
    }

    private static string FreeId(LibraryData data)
    {
        for (var i = data.Collections.Count + 1; ; i++)
        {
            var id = "c" + i.ToString(CultureInfo.InvariantCulture);
            if (!data.Collections.Exists(c => c.Id == id))
            {
                return id;
            }
        }
    }

    // Every entry holding a file of the clip: two tracks labeled while apart have two entries, which act as one line.
    private static void Label(LibraryData data, ClipGroup clip, Action<LibraryClip> set)
    {
        var entries = data.Clips.FindAll(entry => entry.Files.Exists(file => clip.Files.Any(f => string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase))));
        if (entries.Count == 0)
        {
            var entry = new LibraryClip();
            data.Clips.Add(entry);
            entries.Add(entry);
        }

        // A track that came back after the labeling joins the entry, once however many entries cover the line: one survivor
        // must be enough to keep the labels, and a track the entry never cited could not be that survivor.
        var cited = new HashSet<string>(entries.SelectMany(entry => entry.Files.Select(file => file.Path)), StringComparer.OrdinalIgnoreCase);
        foreach (var f in clip.Files)
        {
            if (cited.Add(f.Path))
            {
                entries[0].Files.Add(new LibraryFile { Path = f.Path, Size = f.Size, Written = f.WrittenUtc });
            }
        }

        foreach (var entry in entries)
        {
            set(entry);
        }

        data.Clips.RemoveAll(entry => entry.IsBare);
    }

    private static string Describe(Exception ex) => string.Create(CultureInfo.InvariantCulture, $"{ex.GetType().Name}, 0x{ex.HResult:X8}");

    private LibraryChange Change(ClipScan scan, DateTime nowUtc, Func<LibraryData, LibraryOutcome> apply)
    {
        lock (Gate)
        {
            var read = ReadFile();
            if (!read.CanWrite)
            {
                _journal($"library: not written, the file is {read.Status}");
                return new LibraryChange(LibraryOutcome.ReadOnly, read);
            }

            var data = read.Data;
            data.Clips = ClipIndexMatch.Settled(data.Clips, scan, nowUtc);
            var outcome = apply(data);
            if (outcome != LibraryOutcome.Done)
            {
                // The file, not the entries this action settled or changed in memory: nothing of it was kept.
                return new LibraryChange(outcome, ReadFile());
            }

            try
            {
                // No copy of a sound file: it would keep for good the name of a collection the user deleted. Only a broken
                // file is copied, in StartOver, where the user asked to leave it behind.
                AtomicFile.Write(Path, JsonSerializer.Serialize(data, TypeInfo));
                return new LibraryChange(LibraryOutcome.Done, new LibraryRead(LibraryStatus.Loaded, data));
            }
            catch (Exception ex)
            {
                _journal($"library: not written ({Describe(ex)})");

                // What the disk holds, so a screen drawn from it shows no star and no collection that was not saved.
                return new LibraryChange(LibraryOutcome.NotWritten, ReadFile());
            }
        }
    }
}
