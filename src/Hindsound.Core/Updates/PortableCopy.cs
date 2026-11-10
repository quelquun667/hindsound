namespace Hindsound.Updates;

/// <summary>
/// The portable Windows copy: the zip holds a portable.txt next to Hindsound.exe, and the Setup never writes one. Read once at
/// launch, from the folder of the exe.
/// </summary>
internal static class PortableCopy
{
    public const string MarkerName = "portable.txt";

    /// <summary>The line of the log when both files are there: the installed copy keeps its Setup.</summary>
    public const string MarkerIgnoredLine = "updates: portable.txt ignored, this copy was installed by Setup";

    public static bool HasMarker(string exeFolder) => File.Exists(Path.Combine(exeFolder, MarkerName));

    /// <summary>
    /// The channel of a Windows copy. A copy the Setup installed stays installed, whatever lies next to it: its uninstaller
    /// wins over portable.txt, and <paramref name="markerIgnored"/> says so for the log. Without portable.txt, nothing
    /// changes: an installed copy updates itself, and an exe copied by hand is "not installed".
    /// </summary>
    public static UpdateChannel WindowsChannel(string? exeFolder, out bool markerIgnored)
    {
        markerIgnored = false;
        if (exeFolder is null || !HasMarker(exeFolder))
        {
            return UpdateChannel.WindowsSetup;
        }

        if (InstallKind.UpdaterIsActive(exeFolder))
        {
            markerIgnored = true;
            return UpdateChannel.WindowsSetup;
        }

        return UpdateChannel.WindowsPortable;
    }
}
