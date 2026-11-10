using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Updates;

namespace Hindsound.App;

/// <summary>
/// The parts of the system that the portable code makes for itself when nobody hands it one: the real capture, the device
/// watch, the process list, the startup entry, the global shortcuts and the update network. A test hands its fakes to the
/// constructors instead. The platform layer fills these once, first thing at startup, before anything reads them.
/// </summary>
internal static class PlatformDefaults
{
    public static Func<ICaptureSourceFactory>? CaptureFactory { get; set; }

    public static Func<IDeviceWatcher>? DeviceWatcher { get; set; }

    public static Func<IProcessSnapshot>? ProcessSnapshot { get; set; }

    /// <summary>The entry that starts the app with the session.</summary>
    public static Func<IRunKey>? RunKey { get; set; }

    public static Func<IHotkeyNative>? Hotkeys { get; set; }

    /// <summary>The transport of a real update check, over the network.</summary>
    public static Func<IUpdateTransport>? UpdateNetwork { get; set; }

    /// <summary>A new one from <paramref name="make"/>. Without a platform layer there is nothing to fall back on.</summary>
    public static T Make<T>(Func<T>? make, string what) =>
        make is null ? throw new InvalidOperationException($"No platform layer gives {what}.") : make();
}

/// <summary>The active window and the screens, as the notifications, the tray clicks and the <c>{app}</c> of a file name see them.</summary>
internal interface IScreens
{
    /// <summary>The active window, from outside.</summary>
    ForegroundInfo Foreground();

    /// <summary>Whether the active window covers its own screen.</summary>
    bool ForegroundCovers();

    /// <summary>Whether the active window covers <paramref name="screen"/>, which need not be the one it is on.</summary>
    bool ForegroundCovers(ScreenInfo screen);

    /// <summary>The screen of the active window.</summary>
    ScreenInfo ActiveScreen();

    /// <summary>The screen under the mouse pointer.</summary>
    ScreenInfo CursorScreen();
}

/// <summary>What the system says about the user's availability: presentation, full-screen game, quiet hours.</summary>
internal interface IQuietState
{
    UserNotificationState Read();
}

/// <summary>The confirmation played when no notification can show.</summary>
internal interface IConfirmSound
{
    /// <summary>Starts playing and returns at once. Never throws.</summary>
    bool Play();
}

/// <summary>Opens a file or a folder with what the system gives for it.</summary>
internal interface IFileOpener
{
    /// <summary>Null when it opened; otherwise what went wrong, for the log.</summary>
    string? Open(string target);
}

/// <summary>Memory and processor time of this process, for the log.</summary>
internal interface IProcessMeter
{
    /// <summary>Working set and private bytes, in MB, as a line for the log.</summary>
    string Memory();

    /// <summary>Processor time used so far, in milliseconds.</summary>
    double CpuMilliseconds();
}

/// <summary>The desktop as the app reaches it: one small part per job, each from the platform layer.</summary>
internal sealed record DesktopParts(IScreens Screens, IQuietState Quiet, IConfirmSound Sound, IFileOpener Opener, IProcessMeter Meter);
