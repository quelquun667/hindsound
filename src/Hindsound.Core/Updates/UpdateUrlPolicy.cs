namespace Hindsound.Updates;

/// <summary>
/// Where an update may come from. The manifest is not signed, so this and HTTPS are the only limit on what it can point
/// at. Everything compares the raw text, ordinally, before any URL parser gets a chance to normalize a trap away.
/// </summary>
internal static class UpdateUrlPolicy
{
    public const string ManifestUrl = "https://github.com/quelquun667/hindsound/releases/latest/download/hindsound-update-win-x64.json";

    /// <summary>The only place a Setup may be named at by the manifest.</summary>
    public const string ReleasePrefix = "https://github.com/quelquun667/hindsound/releases/download/";

    /// <summary>The page What's new opens, followed by v{version}.</summary>
    public const string ReleasePagePrefix = "https://github.com/quelquun667/hindsound/releases/tag/";

    private const string SetupExtension = ".exe";

    // GitHub answers a release download on github.com, then redirects to one of its asset hosts.
    private static readonly string[] AllowedHosts = ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    /// <summary>Built from a parsed version only, so the address cannot carry anything but X.Y.Z.</summary>
    public static string ReleasePage(UpdateVersion version) => ReleasePagePrefix + "v" + version;

    /// <summary>Is this exactly a file of this version's release: the prefix, then v{version}, then one plain file name?</summary>
    public static bool IsReleaseAsset(string? url, UpdateVersion version)
    {
        if (url is null || !url.StartsWith(ReleasePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = url[ReleasePrefix.Length..].Split('/');
        return segments.Length == 2
            && segments[0] == "v" + version
            && IsPlainSetupName(segments[1]);
    }

    /// <summary>Is this a place the transport may connect to, as the start of a request or as the target of a redirect?</summary>
    public static bool IsAllowedHop(string? url, out Uri? uri)
    {
        uri = null;
        if (url is null
            || !url.StartsWith("https://", StringComparison.Ordinal)
            || url.Contains('\\')
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        try
        {
            if (parsed.Scheme != Uri.UriSchemeHttps
                || parsed.UserInfo.Length != 0
                || parsed.Port != 443
                || !Array.Exists(AllowedHosts, host => host == parsed.IdnHost))
            {
                return false;
            }
        }
        catch (UriFormatException)
        {
            // TryCreate accepts a host like xn--é.com, and only reading IdnHost finds out it is not a name.
            return false;
        }

        uri = parsed;
        return true;
    }

    // Letters, digits, dot, dash and underscore: no way to write a separator, an escape, a query or an authority.
    private static bool IsPlainSetupName(string name)
    {
        if (!name.EndsWith(SetupExtension, StringComparison.Ordinal) || name.Length == SetupExtension.Length || name[0] == '.' || name.Contains(".."))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
