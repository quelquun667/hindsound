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

    /// <summary>The manifest an update check reads, and whether a newer version installs or is only told. Null: the installed Windows app's.</summary>
    public static UpdateChannel? UpdateChannel { get; set; }

    /// <summary>
    /// The MP3 encoder, where NAudio.Lame cannot load its library (it does so through Windows calls): given the file stream,
    /// the channel count and the bitrate, a stream that takes 16-bit interleaved PCM at <see cref="Audio.InternalFormat.SampleRate"/>
    /// and writes MP3 to the file stream. Null keeps NAudio.Lame, as on Windows.
    /// </summary>
    public static Func<Stream, int, int, Stream>? Mp3Encoder { get; set; }

    /// <summary>The desktop as the app reads it: the screens, the quiet state, the sound, the opener, the meter.</summary>
    public static Func<DesktopParts>? Desktop { get; set; }

    /// <summary>The loop, the notification, the icon, the window and the rest the running app shows itself through.</summary>
    public static Func<IAppShell>? Shell { get; set; }

    /// <summary>
    /// The path a person launches the app by, when that is not the running executable: an AppImage runs from a temporary mount
    /// that changes on every launch, and the file to start again (and to replace on an update) is the image itself.
    /// Null, or a platform that gives none, means the running executable.
    /// </summary>
    public static Func<string?>? LaunchPath { get; set; }

    /// <summary>The program a startup entry starts, and the folder an update replaces files in.</summary>
    public static string? ResolveLaunchPath() => LaunchPath?.Invoke() is { Length: > 0 } path ? path : Environment.ProcessPath;

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

    /// <summary>The screen of the active window; null when the system does not say (Wayland keeps it to itself).</summary>
    ScreenInfo? ActiveScreen();

    /// <summary>The screen under the mouse pointer; null when the system does not say.</summary>
    ScreenInfo? CursorScreen();

    /// <summary>The main screen, always known: what the app falls back on when the other two are not.</summary>
    ScreenInfo PrimaryScreen();
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
