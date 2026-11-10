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
            PlatformDefaults.Hotkeys = () => new Win32HotkeyNative();
            PlatformDefaults.UpdateNetwork = () => new WinHttpTransport();
            _installed = true;
        }
    }

    /// <summary>The desktop parts of a Windows run.</summary>
    public static DesktopParts Desktop() =>
        new(new WindowsScreens(), new WindowsQuietState(), new WindowsConfirmSound(), new ShellFileOpener(), new WindowsProcessMeter());
}

/// <param name="active">Where the active window is; the real reading of the desktop when null. Given by the tests only.</param>
/// <param name="cursor">Where the cursor is; the real reading of the desktop when null. Given by the tests only.</param>
internal sealed class WindowsScreens(Func<ScreenInfo>? active = null, Func<ScreenInfo>? cursor = null) : IScreens
{
    private readonly Func<ScreenInfo> _active = active ?? ForegroundWindow.ActiveScreen;
    private readonly Func<ScreenInfo> _cursor = cursor ?? ForegroundWindow.CursorScreen;

    public ForegroundInfo Foreground() => ForegroundWindow.Read();

    public bool ForegroundCovers() => ForegroundWindow.ForegroundCovers();

    public bool ForegroundCovers(ScreenInfo screen) => ForegroundWindow.CoversScreen(screen);

    public ScreenInfo ActiveScreen() => _active();

    public ScreenInfo CursorScreen() => _cursor();
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
