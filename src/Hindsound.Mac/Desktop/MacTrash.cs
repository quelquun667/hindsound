using System.Runtime.Versioning;
using Hindsound.Library;
using Hindsound.Mac.Interop;

namespace Hindsound.Mac.Desktop;

/// <summary>
/// The Trash of macOS, through <c>-[NSFileManager trashItemAtURL:resultingItemURL:error:]</c>: the file goes to the Trash
/// of its volume (~/.Trash for the user's own disk) and the Finder can put it back. Never deletes a file for good.
/// </summary>
internal sealed unsafe class MacTrash(Action<string> journal) : IRecycleBin
{
    [SupportedOSPlatform("macos")]
    public RecycleOutcome Recycle(IReadOnlyList<string> paths)
    {
        // Seen before anything moves: a file gone, or a path the Trash cannot name.
        if (paths.Any(path => !path.StartsWith('/')))
        {
            return RecycleOutcome.CannotRecycle;
        }

        if (paths.Any(path => !File.Exists(path)))
        {
            return RecycleOutcome.Gone;
        }

        for (var i = 0; i < paths.Count; i++)
        {
            if (TrashOne(paths[i], out _) is { } error)
            {
                journal($"Trash: {error}");
                return OutcomeOfFailure(i);
            }
        }

        return RecycleOutcome.Recycled;
    }

    /// <summary>
    /// What a refusal of the file at <paramref name="index"/> leaves: before anything moved, every file is where it was;
    /// after, some are in the Trash and the disk must be looked at again.
    /// </summary>
    public static RecycleOutcome OutcomeOfFailure(int index) => index == 0 ? RecycleOutcome.CannotRecycle : RecycleOutcome.Failed;

    /// <summary>Sends one file to the Trash. Null when it went, with the path it has there; otherwise what the system said.</summary>
    [SupportedOSPlatform("macos")]
    internal static string? TrashOne(string path, out string? inTrash)
    {
        inTrash = null;
        using var pool = new ObjC.Pool();
        var url = ObjC.FileUrl(path);
        if (url == 0)
        {
            return "no file URL for the path";
        }

        try
        {
            var manager = ObjC.Send(AppKit.Class("NSFileManager"), "defaultManager");
            nint resulting = 0;
            nint error = 0;
            if (!AppKit.Trash(manager, url, &resulting, &error))
            {
                return ObjC.String(ObjC.Send(error, "localizedDescription")) ?? "refused, without a reason";
            }

            inTrash = resulting == 0 ? null : ObjC.String(ObjC.Send(resulting, "path"));
            return null;
        }
        finally
        {
            ObjC.Release(url);
        }
    }
}
