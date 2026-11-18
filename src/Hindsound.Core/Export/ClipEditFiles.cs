using System.IO;
using Hindsound.Library;
using Hindsound.Settings;

namespace Hindsound.Export;

/// <summary>What the reading of a clip gave the editor, and what it keeps for its estimates.</summary>
/// <param name="Load">The answer for the screen.</param>
/// <param name="Channels">The channels of each file, in the order of the files. Empty unless loaded.</param>
internal sealed record ClipEditReading(EditorLoad Load, IReadOnlyList<int> Channels);

/// <summary>
/// The disk side of the editor: a clip read for its wave, an edit written as a copy, in place of the original or for the
/// soundboard, the selection written as a WAV to be heard. Everything here blocks: it is called off the loop, one call at a
/// time. An original is never deleted: it goes to the Recycle Bin, or it stays. The log gets the type of an error and its code,
/// never a path or a message: a file name can hold a first name.
/// </summary>
/// <param name="decoder">Reads the files of a clip.</param>
/// <param name="recycleBin">Where a replaced original goes.</param>
/// <param name="index">The labels that follow an edit.</param>
/// <param name="journal">Told of failures, by type and code only.</param>
internal sealed class ClipEditFiles(IClipDecoder decoder, IRecycleBin recycleBin, IClipIndex index, Action<string> journal)
{
    /// <summary>What a temporary file of a replacement ends with: <see cref="ClipScanner"/> lists only mp3 and wav files, so it never shows.</summary>
    public const string TempExtension = ".hindsound-edit";

    /// <summary>The largest soundboard copy kept. A test makes it smaller to see what a bigger one does.</summary>
    internal long SoundboardMaxBytes { get; init; } = Soundboard.MaxBytes;

    /// <summary>Reads each file of the clip once, for its bars. Gone if a file is missing, Unreadable if one can't be decoded.</summary>
    /// <param name="clip">The clip as the list knew it.</param>
    /// <param name="peaks">The bars of one file, read from its start.</param>
    /// <param name="cancel">The page was left.</param>
    public ClipEditReading Load(ClipGroup clip, Func<IDecodedStream, CancellationToken, IReadOnlyList<float>> peaks, CancellationToken cancel)
    {
        var format = ClipEditRender.FormatOf(clip.Main.Path);
        if (clip.Files.Count == 0 || clip.Files.Any(file => !File.Exists(file.Path)))
        {
            return new ClipEditReading(new EditorLoad(EditorLoadOutcome.Gone, TimeSpan.Zero, [], format), []);
        }

        var bars = new List<IReadOnlyList<float>>(clip.Files.Count);
        var channels = new List<int>(clip.Files.Count);
        var duration = TimeSpan.Zero;
        try
        {
            foreach (var file in clip.Files)
            {
                cancel.ThrowIfCancellationRequested();
                using var stream = decoder.Open(file.Path);
                channels.Add(stream.Channels);
                duration = duration > stream.Duration ? duration : stream.Duration;
                bars.Add(peaks(stream, cancel));
            }
        }
        catch (Exception ex) when (ex is ClipDecodeException or IOException or UnauthorizedAccessException)
        {
            var gone = clip.Files.Any(file => !File.Exists(file.Path));
            journal($"editor: clip not read ({ClipFolders.Describe(ex)})");
            return new ClipEditReading(new EditorLoad(gone ? EditorLoadOutcome.Gone : EditorLoadOutcome.Unreadable, TimeSpan.Zero, [], format), []);
        }

        return new ClipEditReading(new EditorLoad(EditorLoadOutcome.Loaded, duration, bars, format), channels);
    }

    /// <summary>Writes the edit. A soundboard edit is always a copy. Never throws but for a cancel, after which nothing of the edit is left.</summary>
    public EditSaveResult Save(ClipGroup clip, EditRequest request, EditSave save, ExportSettings export, CancellationToken cancel)
    {
        if (clip.Files.Count == 0 || clip.Files.Any(file => !File.Exists(file.Path)))
        {
            return Result(EditSaveOutcome.SourceGone);
        }

        try
        {
            if (request.Mode == EditorMode.Soundboard)
            {
                return SaveSoundboard(clip, request, save, export, cancel);
            }

            return save.Choice == EditSaveChoice.Replace
                ? Replace(clip, request, export, cancel)
                : SaveCopy(clip, request, save, export, cancel);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            journal($"editor: edit not saved ({ClipFolders.Describe(ex)})");
            return Result(clip.Files.Any(file => !File.Exists(file.Path)) ? EditSaveOutcome.SourceGone : EditSaveOutcome.Failed);
        }
    }

    /// <summary>The selection with its volumes, every file mixed, as a 16-bit WAV at <paramref name="path"/>, which must not hold anything yet.</summary>
    public void WritePreview(ClipGroup clip, EditRequest request, string path, ExportSettings export, CancellationToken cancel)
    {
        var streams = OpenAll(clip);
        try
        {
            using var writer = Encoders.OpenWav(path, ClipEditRender.MixChannels(streams));
            ClipEditRender.Render(streams, request.GainsDb, request.Start, request.End, ClipEditRender.MixChannels(streams), ClipEditRender.Ceiling(export.PeakCeilingDb), writer, cancel);
        }
        finally
        {
            DisposeAll(streams);
        }
    }

    private EditSaveResult SaveCopy(ClipGroup clip, EditRequest request, EditSave save, ExportSettings export, CancellationToken cancel)
    {
        var format = ClipEditRender.FormatOf(clip.Main.Path);
        var paths = Reserve(clip, NameOf(save.CopyName, ClipNames.EditableName(clip) + EditLimits.CutSuffix), format);
        try
        {
            for (var i = 0; i < clip.Files.Count; i++)
            {
                WriteOne(clip.Files[i].Path, paths[i], format, GainOf(request, i), request, export, cancel);
            }
        }
        catch
        {
            // Only the copies being made go: they are this save's own files, the original was never written to.
            DeleteOurs(paths);
            throw;
        }

        Labels(index.CopyCollections(clip, paths), "copy");
        return new EditSaveResult(EditSaveOutcome.Saved, paths);
    }

    private EditSaveResult Replace(ClipGroup clip, EditRequest request, ExportSettings export, CancellationToken cancel)
    {
        var originals = clip.Files.Select(file => file.Path).ToArray();
        var temps = new List<string>(originals.Length);
        try
        {
            // Every new file is written in full before anything is sent to the Recycle Bin.
            foreach (var original in originals)
            {
                temps.Add(ReserveTemp(original));
            }

            for (var i = 0; i < originals.Length; i++)
            {
                WriteOne(originals[i], temps[i], ClipEditRender.FormatOf(originals[i]), GainOf(request, i), request, export, cancel);
            }

            cancel.ThrowIfCancellationRequested();
        }
        catch
        {
            DeleteOurs(temps);
            throw;
        }

        var recycled = recycleBin.Recycle(originals);
        if (recycled == RecycleOutcome.CannotRecycle)
        {
            DeleteOurs(temps);
            return Result(EditSaveOutcome.CannotRecycle);
        }

        if (recycled == RecycleOutcome.Failed && originals.All(File.Exists))
        {
            journal("editor: original not recycled");
            DeleteOurs(temps);
            return Result(EditSaveOutcome.Failed);
        }

        // From here the originals may be in the bin: the edit is never thrown away, whatever happens to it.
        var stuck = new List<int>();
        var left = Left(recycled, originals);
        if (left is not null)
        {
            journal(left);
            stuck.AddRange(Enumerable.Range(0, originals.Length));
        }

        for (var i = 0; i < originals.Length && left is null; i++)
        {
            try
            {
                File.Move(temps[i], originals[i]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                journal($"editor: edit not put in place ({ClipFolders.Describe(ex)})");
                stuck.Add(i);
            }
        }

        if (stuck.Count == 0)
        {
            Labels(index.MoveLabels(clip, originals), "replace");
            return new EditSaveResult(EditSaveOutcome.Saved, originals);
        }

        return new EditSaveResult(EditSaveOutcome.SavedAside, Rescue(clip, originals, temps, stuck));
    }

    /// <summary>
    /// Why no edited file may take the place of an original, for the log, or null if each may. Only part of the original went,
    /// and an edited file next to one that was not would read as one clip out of step; or a file of the original was gone before
    /// the bin could take it, and what the user deleted is not brought back under its name.
    /// </summary>
    private static string? Left(RecycleOutcome recycled, string[] originals) => recycled switch
    {
        RecycleOutcome.Gone => "editor: original gone before it went to the Recycle Bin",
        RecycleOutcome.Failed when originals.Any(File.Exists) => "editor: original recycled in part",
        _ => null,
    };

    /// <summary>
    /// The edits that could not take the name of their original take a name of their own, as a copy would. One that can't is
    /// removed rather than left hidden under <see cref="TempExtension"/>: the original can still come back from the Recycle Bin.
    /// </summary>
    private List<string> Rescue(ClipGroup clip, string[] originals, List<string> temps, List<int> stuck)
    {
        var paths = new List<string>(originals.Length);
        string[] reserved;
        try
        {
            reserved = Reserve(clip, ClipNames.EditableName(clip) + EditLimits.CutSuffix, ClipEditRender.FormatOf(clip.Main.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            journal($"editor: edit not kept, no name of its own ({ClipFolders.Describe(ex)})");
            DeleteOurs(stuck.Select(i => temps[i]));
            return [.. Enumerable.Range(0, originals.Length).Where(i => !stuck.Contains(i)).Select(i => originals[i])];
        }

        for (var i = 0; i < originals.Length; i++)
        {
            if (!stuck.Contains(i))
            {
                DeleteOurs([reserved[i]]);
                paths.Add(originals[i]);
                continue;
            }

            try
            {
                // The reservation is an empty file this save made: it is the one replaced.
                File.Move(temps[i], reserved[i], overwrite: true);
                paths.Add(reserved[i]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                journal($"editor: edit not kept, no name of its own ({ClipFolders.Describe(ex)})");
                DeleteOurs([reserved[i], temps[i]]);
            }
        }

        return paths;
    }

    private EditSaveResult SaveSoundboard(ClipGroup clip, EditRequest request, EditSave save, ExportSettings export, CancellationToken cancel)
    {
        var path = FileNameTemplate.ReserveUniquePaths(
            Path.GetDirectoryName(clip.Main.Path) ?? "",
            NameOf(save.CopyName, ClipNames.EditableName(clip) + Soundboard.CopySuffix),
            [""],
            ClipEditRender.ExtensionOf(ExportFormat.Mp3))[0];
        try
        {
            var streams = OpenAll(clip);
            try
            {
                // The screen keeps the selection short; whatever asks, the copy never holds more than Discord takes.
                var end = TimeSpan.FromTicks(Math.Min(request.End.Ticks, (request.Start + TimeSpan.FromSeconds(Soundboard.MaxSeconds)).Ticks));
                var channels = ClipEditRender.MixChannels(streams);
                using var writer = ClipEditRender.OpenWriter(path, ExportFormat.Mp3, channels, ClipEditRender.SoundboardBitrateKbps);
                ClipEditRender.Render(streams, request.GainsDb, request.Start, end, channels, ClipEditRender.Ceiling(export.PeakCeilingDb), writer, cancel);
            }
            finally
            {
                DisposeAll(streams);
            }
        }
        catch
        {
            DeleteOurs([path]);
            throw;
        }

        if (new FileInfo(path).Length > SoundboardMaxBytes)
        {
            DeleteOurs([path]);
            return Result(EditSaveOutcome.TooBig);
        }

        Labels(index.CopyCollections(clip, [path]), "soundboard");
        return new EditSaveResult(EditSaveOutcome.Saved, [path]);
    }

    private void WriteOne(string source, string target, ExportFormat format, double gainDb, EditRequest request, ExportSettings export, CancellationToken cancel)
    {
        using var stream = decoder.Open(source);
        using var writer = ClipEditRender.OpenWriter(target, format, stream.Channels, export.Mp3Bitrate);
        ClipEditRender.Render([stream], [gainDb], request.Start, request.End, stream.Channels, ClipEditRender.Ceiling(export.PeakCeilingDb), writer, cancel);
    }

    private List<IDecodedStream> OpenAll(ClipGroup clip)
    {
        var streams = new List<IDecodedStream>(clip.Files.Count);
        try
        {
            foreach (var file in clip.Files)
            {
                streams.Add(decoder.Open(file.Path));
            }
        }
        catch
        {
            DisposeAll(streams);
            throw;
        }

        return streams;
    }

    private static void DisposeAll(List<IDecodedStream> streams)
    {
        foreach (var stream in streams)
        {
            try { stream.Dispose(); } catch (Exception) { /* a stream that can't close holds nothing of ours */ }
        }
    }

    /// <summary>The paths of a copy: the name made unique for every file at once, each with the suffix of its track.</summary>
    private static string[] Reserve(ClipGroup clip, string name, ExportFormat format)
    {
        // The paths a renaming to "" would give hold only the suffix of each track and its extension.
        var suffixes = ClipNames.TargetPaths(clip, "").Select(Path.GetFileNameWithoutExtension).Select(suffix => suffix ?? "").ToArray();
        return FileNameTemplate.ReserveUniquePaths(Path.GetDirectoryName(clip.Main.Path) ?? "", name, suffixes, ClipEditRender.ExtensionOf(format));
    }

    /// <summary>An empty file next to the original, under a name the Library never lists, that no other save holds.</summary>
    private static string ReserveTemp(string original)
    {
        for (var attempt = 1; ; attempt++)
        {
            var path = attempt == 1 ? original + TempExtension : $"{original}.{attempt}{TempExtension}";
            try
            {
                new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None).Dispose();
                return path;
            }
            catch (IOException) when (File.Exists(path) && attempt < 100)
            {
            }
        }
    }

    private static string NameOf(string? asked, string fallback)
    {
        var name = asked is null ? "" : FileNameTemplate.Sanitize(asked);
        return name.Length > 0 ? name : fallback;
    }

    private static double GainOf(EditRequest request, int file) => file < request.GainsDb.Count ? request.GainsDb[file] : 0;

    private void Labels(LibraryOutcome outcome, string what)
    {
        if (outcome != LibraryOutcome.Done)
        {
            journal($"editor: labels of the {what} not written ({outcome})");
        }
    }

    /// <summary>Removes files this save made itself. Never an original.</summary>
    private void DeleteOurs(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                journal($"editor: a file of the edit not removed ({ClipFolders.Describe(ex)})");
            }
        }
    }

    private static EditSaveResult Result(EditSaveOutcome outcome) => new(outcome, []);
}
