using System.Text;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// Files as text/uri-list (RFC 2483): one file:// URI per line, each line ended by CR LF. A file manager pastes that as files.
/// </summary>
internal static class UriList
{
    public const string MimeType = "text/uri-list";

    public static string Of(IEnumerable<string> paths)
    {
        var list = new StringBuilder();
        foreach (var path in paths)
        {
            list.Append(FileUri(path)).Append("\r\n");
        }

        return list.ToString();
    }

    /// <summary>
    /// The file:// URI of an absolute Linux path: its UTF-8 bytes, each one outside the unreserved characters of RFC 3986
    /// and the slash written as %XX. A space, a '#' or an accent in a clip name would otherwise break the URI.
    /// </summary>
    public static string FileUri(string path)
    {
        var uri = new StringBuilder("file://");
        foreach (var b in Encoding.UTF8.GetBytes(path))
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9')
                or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~' or (byte)'/')
            {
                uri.Append((char)b);
            }
            else
            {
                uri.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return uri.ToString();
    }
}
