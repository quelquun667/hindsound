using System.Security.Cryptography;

namespace Hindsound.Linux.DBus;

/// <summary>
/// The names D-Bus accepts, as its specification words them (sections "Valid Names" and "Valid Object Paths"), and the
/// paths the desktop portal makes from them.
/// </summary>
internal static class DBusNames
{
    /// <summary>The id of the app: its bus name, its portal id and the name of its .desktop file.</summary>
    public const string AppId = "io.github.quelquun667.Hindsound";

    /// <summary>The longest bus name, interface or member.</summary>
    public const int MaxNameLength = 255;

    private const string PortalRequestRoot = "/org/freedesktop/portal/desktop/request/";

    /// <summary>The object path of a dotted name: <c>io.github.x.App</c> gives <c>/io/github/x/App</c>.</summary>
    public static string ObjectPathOf(string dottedName) => "/" + dottedName.Replace('.', '/');

    public static bool IsObjectPath(string? path)
    {
        if (path is not { Length: > 0 } || path[0] != '/')
        {
            return false;
        }

        return path == "/" || path[1..].Split('/').All(element => element.Length > 0 && element.All(IsPlainChar));
    }

    public static bool IsInterfaceName(string? name) => HasElements(name, hyphen: false, leadingDigit: false);

    /// <summary>Error names follow the rules of interface names.</summary>
    public static bool IsErrorName(string? name) => IsInterfaceName(name);

    public static bool IsMemberName(string? name) =>
        name is { Length: > 0 and <= MaxNameLength } && !char.IsAsciiDigit(name[0]) && name.All(IsPlainChar);

    /// <summary>A unique name (<c>:1.42</c>), whose elements may start with a digit, or a well-known one.</summary>
    public static bool IsBusName(string? name)
    {
        if (name is null || name.Length > MaxNameLength)
        {
            return false;
        }

        return name.StartsWith(':') ? HasElements(name[1..], hyphen: true, leadingDigit: true) : HasElements(name, hyphen: true, leadingDigit: false);
    }

    public static bool IsUniqueName(string? name) => name is not null && name.StartsWith(':') && IsBusName(name);

    /// <summary>The characters a token of the portal may hold: those of an element of an object path.</summary>
    public static bool IsToken(string? token) => token is { Length: > 0 } && token.All(IsPlainChar);

    /// <summary>A token hard to guess, as the portal asks: the prefix of the app and a random number.</summary>
    public static string NewToken() => "hindsound_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    /// <summary>
    /// The handle of a portal request: <c>/org/freedesktop/portal/desktop/request/SENDER/TOKEN</c>, SENDER being the
    /// caller's unique name without its colon and with every period turned into an underscore, TOKEN the <c>handle_token</c>
    /// of the call. Known before the call, so the Response signal can be watched before the answer can come.
    /// </summary>
    public static string PortalRequestPath(string uniqueName, string token)
    {
        if (!IsUniqueName(uniqueName))
        {
            throw new ArgumentException("not a unique bus name", nameof(uniqueName));
        }

        if (!IsToken(token))
        {
            throw new ArgumentException("not a token of the portal", nameof(token));
        }

        return PortalRequestRoot + uniqueName[1..].Replace('.', '_') + "/" + token;
    }

    private static bool HasElements(string? name, bool hyphen, bool leadingDigit)
    {
        if (name is null || name.Length > MaxNameLength)
        {
            return false;
        }

        var elements = name.Split('.');
        if (elements.Length < 2)
        {
            return false;
        }

        foreach (var element in elements)
        {
            if (element.Length == 0 || (!leadingDigit && char.IsAsciiDigit(element[0])))
            {
                return false;
            }

            if (!element.All(c => IsPlainChar(c) || (hyphen && c == '-')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPlainChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
