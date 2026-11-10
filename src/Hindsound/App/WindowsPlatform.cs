using System.Globalization;
using Hindsound.Audio;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Updates;

namespace Hindsound.App;

/// <summary>The Windows layer: the classes that reach Win32, WASAPI and the registry, handed to the portable code.</summary>
internal static class WindowsPlatform
{
    private static readonly object Lock = new();
    private static bool _installed;

    /// <summary>portable.txt lies next to an installed copy, which stays installed: the entry point says it in the log.</summary>
    public static bool PortableMarkerIgnored { get; private set; }

    /// <summary>Fills <see cref="PlatformDefaults"/>. Called first thing by the entry point, and by the tests; once is enough.</summary>
    public static void Install()
    {
        lock (Lock)
        {
            if (_installed)
            {
                return;
            }

            PlatformDefaults.CaptureFactory = () => new WasapiCaptureSourceFactory();
            PlatformDefaults.DeviceWatcher = () => new DeviceWatcher();
            PlatformDefaults.ProcessSnapshot = () => new ToolHelpProcessSnapshot();
            PlatformDefaults.RunKey = () => new RegistryRunKey();
            PlatformDefaults.LaunchPath = () => Environment.ProcessPath;
            // RegisterHotKey grabs one combination at a time; the batch the app binds through is made of it.
            PlatformDefaults.Hotkeys = () => new KeyByKeyHotkeys(new Win32HotkeyNative());
            PlatformDefaults.UpdateNetwork = () => new WinHttpTransport();

            // Only the zip's copy changes channel: without portable.txt the default stays the installed app's, as before.
            var channel = ChannelFor(AppContext.BaseDirectory, out var markerIgnored);
            PortableMarkerIgnored = markerIgnored;
            if (channel is not null)
            {
                PlatformDefaults.UpdateChannel = channel;
            }

            PlatformDefaults.Desktop = Desktop;
            PlatformDefaults.Shell = () => new WindowsShell();
            _installed = true;
        }
    }

    /// <summary>
    /// The channel a copy in <paramref name="exeFolder"/> sets, or null when it keeps the installed app's default. Only the
    /// folder of the exe tells the zip's copy apart, so the tests point it at folders of their own.
    /// </summary>
    internal static UpdateChannel? ChannelFor(string exeFolder, out bool markerIgnored)
    {
        var channel = PortableCopy.WindowsChannel(exeFolder, out markerIgnored);
        return channel.AnnouncesOnly ? channel : null;
    }

    /// <summary>The desktop parts of a Windows run.</summary>
    public static DesktopParts Desktop() =>
        new(new WindowsScreens(), new WindowsQuietState(), new WindowsConfirmSound(), new ShellFileOpener(), new WindowsProcessMeter());
}

/// <summary>Windows always answers both screens: a window or a pointer off every screen still has the nearest or main one.</summary>
/// <param name="active">Where the active window is; the real reading of the desktop when null. Given by the tests only.</param>
/// <param name="cursor">Where the cursor is; the real reading of the desktop when null. Given by the tests only.</param>
internal sealed class WindowsScreens(Func<ScreenInfo>? active = null, Func<ScreenInfo>? cursor = null) : IScreens
{
    private readonly Func<ScreenInfo> _active = active ?? ForegroundWindow.ActiveScreen;
    private readonly Func<ScreenInfo> _cursor = cursor ?? ForegroundWindow.CursorScreen;

    public ForegroundInfo Foreground() => ForegroundWindow.Read();

    public bool ForegroundCovers() => ForegroundWindow.ForegroundCovers();

    public bool ForegroundCovers(ScreenInfo screen) => ForegroundWindow.CoversScreen(screen);

    public ScreenInfo? ActiveScreen() => _active();

    public ScreenInfo? CursorScreen() => _cursor();

    public ScreenInfo PrimaryScreen() => ForegroundWindow.PrimaryScreen();
}

internal sealed unsafe class WindowsQuietState : IQuietState
{
    public UserNotificationState Read()
    {
        int raw;
        var result = Native.SHQueryUserNotificationState(&raw);
        return FromResult(result, raw);
    }

    /// <summary>The state the call gave, or Unknown when its result is not S_OK, in which case <paramref name="raw"/> was not written.</summary>
    internal static UserNotificationState FromResult(int result, int raw) =>
        result == 0 ? (UserNotificationState)raw : UserNotificationState.Unknown;
}

internal sealed class WindowsConfirmSound : IConfirmSound
{
    public bool Play() => ConfirmSound.Play();
}

internal sealed class ShellFileOpener : IFileOpener
{
    private const int LargestShellError = 32;

    public string? Open(string target)
    {
        var result = Native.ShellExecuteW(0, null, target, null, null, Native.SW_SHOWNORMAL);
        return result <= LargestShellError ? $"shell error {result}" : null;
    }
}

internal sealed unsafe class WindowsProcessMeter : IProcessMeter
{
    public string Memory()
    {
        Native.PROCESS_MEMORY_COUNTERS_EX counters = default;
        counters.Size = (uint)sizeof(Native.PROCESS_MEMORY_COUNTERS_EX);
        Native.K32GetProcessMemoryInfo(Native.GetCurrentProcess(), &counters, counters.Size);
        const double mb = 1024 * 1024;
        return string.Create(CultureInfo.InvariantCulture, $"working set {counters.WorkingSet / mb:F1} MB, private {counters.Private / mb:F1} MB");
    }

    public double CpuMilliseconds()
    {
        long creation, exit, kernel, user;
        Native.GetProcessTimes(Native.GetCurrentProcess(), &creation, &exit, &kernel, &user);
        return (kernel + user) / 10000.0;
    }
}
