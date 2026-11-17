namespace Hindsound.App;

/// <summary>
/// The name a clip takes for <c>{app}</c> in its file name. When the app records one program alone, the clip is that program's,
/// whatever window is in front when the shortcut is pressed; otherwise it is the window in front, as it has always been.
/// </summary>
internal static class ClipAppName
{
    private const string ExeSuffix = ".exe";

    /// <summary>
    /// <paramref name="sourceApp"/> is the program recorded alone, or null for the whole PC. <paramref name="displayName"/> is the
    /// name the list of programs gives it, when that is known. <paramref name="foregroundName"/> is the window in front.
    /// </summary>
    public static string? For(string? sourceApp, string? displayName, string? foregroundName)
    {
        var exe = sourceApp?.Trim();
        if (string.IsNullOrEmpty(exe))
        {
            return foregroundName;
        }

        var shown = displayName?.Trim();
        if (!string.IsNullOrEmpty(shown))
        {
            return shown;
        }

        var bare = exe.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase) ? exe[..^ExeSuffix.Length].TrimEnd() : exe;
        return bare.Length > 0 ? bare : foregroundName;
    }
}
