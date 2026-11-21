using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Mac.Interop;

namespace Hindsound.Mac.Desktop;

/// <summary>
/// The clip on the general pasteboard as the Finder puts a copied file there: NSURLs of files written by
/// <c>writeObjects:</c>, which other apps read as <c>public.file-url</c>.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacPasteboard(Action<string> log) : IClipboardFiles
{
    public bool TrySet(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || paths.Any(path => !path.StartsWith('/')))
        {
            log("pasteboard: only absolute paths can go on it");
            return false;
        }

        using var pool = new ObjC.Pool();
        var urls = new nint[paths.Count];
        try
        {
            for (var i = 0; i < paths.Count; i++)
            {
                urls[i] = ObjC.FileUrl(paths[i]);
                if (urls[i] == 0)
                {
                    log("pasteboard: no file URL for a path");
                    return false;
                }
            }

            var pasteboard = ObjC.Send(AppKit.Class("NSPasteboard"), "generalPasteboard");
            if (pasteboard == 0)
            {
                log("pasteboard: no general pasteboard");
                return false;
            }

            nint array;
            fixed (nint* objects = urls)
            {
                array = AppKit.Array(objects, (nuint)urls.Length);
            }

            ObjC.Send(pasteboard, "clearContents");
            if (array == 0 || !ObjC.SendBool(pasteboard, "writeObjects:", array))
            {
                log("pasteboard: writeObjects refused the files");
                return false;
            }

            return true;
        }
        finally
        {
            foreach (var url in urls)
            {
                ObjC.Release(url);
            }
        }
    }

    /// <summary>The file URL on the general pasteboard, as other apps read it; null when there is none.</summary>
    internal static string? FileUrlOnIt()
    {
        using var pool = new ObjC.Pool();
        var pasteboard = ObjC.Send(AppKit.Class("NSPasteboard"), "generalPasteboard");
        var type = ObjC.NSString("public.file-url");
        try
        {
            return ObjC.String(ObjC.Send(pasteboard, "stringForType:", type));
        }
        finally
        {
            ObjC.Release(type);
        }
    }
}
