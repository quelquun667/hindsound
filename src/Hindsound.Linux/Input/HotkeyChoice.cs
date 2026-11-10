namespace Hindsound.Linux.Input;

/// <summary>Which layer grabs the global shortcuts.</summary>
internal enum HotkeyLayer
{
    /// <summary>The desktop portal (org.freedesktop.portal.GlobalShortcuts): Wayland, and any desktop that has it.</summary>
    Portal,

    /// <summary>XGrabKey on the root window of the X display.</summary>
    X11,
}

/// <summary>The choice of a shortcut layer and what the app says when there is none. Pure logic.</summary>
internal static class HotkeyChoice
{
    /// <summary>Logged, and shown once, when no layer can grab a key.</summary>
    public const string NoShortcutText = "No global shortcut on this desktop. Bind hindsound --save to a key in your keyboard settings.";

    /// <summary>The marker that says the notice was shown, so it is shown at the first launch only.</summary>
    public const string NoticeMarkerName = "no-global-shortcut-notice";

    /// <summary>Whether the session is Wayland: its socket is named, or the session says so.</summary>
    public static bool IsWayland(Func<string, string?> environment) =>
        !string.IsNullOrEmpty(environment("WAYLAND_DISPLAY"))
        || string.Equals(environment("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    public static bool HasXDisplay(Func<string, string?> environment) => !string.IsNullOrEmpty(environment("DISPLAY"));

    /// <summary>
    /// The layer to try first. In an X11 session, XGrabKey: it binds at once, with no dialog. Under Wayland, the portal
    /// only: an X display there is Xwayland, whose clients get keys only while one of their windows has the focus, so a
    /// grab on it would look bound and never fire. When the first layer fails, the X11 session tries the portal next;
    /// Wayland has nothing else.
    /// </summary>
    public static HotkeyLayer First(bool wayland, bool xDisplay) => !wayland && xDisplay ? HotkeyLayer.X11 : HotkeyLayer.Portal;

    /// <summary>
    /// Where the marker goes: <c>$XDG_CONFIG_HOME/Hindsound</c>, else <c>~/.config/Hindsound</c>, the folder of the
    /// settings. A relative XDG_CONFIG_HOME is ignored, as the XDG base directory specification asks. Null without a home.
    /// </summary>
    public static string? NoticeMarkerPath(Func<string, string?> environment)
    {
        var config = environment("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(config) || !config.StartsWith('/'))
        {
            var home = environment("HOME");
            if (string.IsNullOrEmpty(home) || !home.StartsWith('/'))
            {
                return null;
            }

            config = home.TrimEnd('/') + "/.config";
        }

        return config.TrimEnd('/') + "/Hindsound/" + NoticeMarkerName;
    }

    /// <summary>
    /// Claims the notice: true the first time, when the marker did not exist yet (it is written then). A marker that
    /// cannot be written is logged, and the notice still goes out: better twice than never.
    /// </summary>
    public static bool ClaimNotice(string? markerPath, Action<string> log)
    {
        if (markerPath is null)
        {
            return true;
        }

        try
        {
            if (File.Exists(markerPath))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            File.WriteAllText(markerPath, "shown\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log("hotkeys: the notice marker could not be written: " + ex.GetType().Name);
        }

        return true;
    }
}
