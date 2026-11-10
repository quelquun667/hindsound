using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.Audio;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Desktop;
using Hindsound.Linux.Export;
using Hindsound.Linux.Input;
using Hindsound.Linux.Updates;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux;

// Each part the portable code needs is one line below (or one line of App/LinuxShell.cs), and one class in its own file.
//
//   Audio/    PulseCaptureFactory.cs     capture: the monitor of the default output, and the microphone
//             PulseDeviceWatcher.cs      a change of the default output or of the microphone
//             ProcProcessSnapshot.cs     the process list, read from /proc
//             PulseAudioChoices.cs       the devices the settings would offer (LinuxShell)
//   Export/   LameMp3Encoder.cs          the MP3, through the libmp3lame.so.0 shipped with the app
//   App/      LinuxLoop.cs               the loop of the app: done
//             LinuxInstanceChannel.cs    one instance at a time, and what "hindsound" or "hindsound --save" tells it (Program)
//             LaunchPaths.cs             the file the session starts again: done
//             NoPreviewPlayer.cs         no listening in this version: done
//             NoSettingsProbe.cs         no window in this version: done
//   Desktop/  FreedesktopNotification.cs the notification, silent, with "Show in folder" (LinuxShell)
//             LinuxClipboard.cs          the clip on the clipboard as text/uri-list (LinuxShell)
//             LinuxFolderShell.cs        the folder of the clips, the clip selected (LinuxShell)
//             LinuxSessionEnd.cs         the end of the session (LinuxShell)
//             XdgFileOpener.cs           opening a file or a folder
//             ProcSelfMeter.cs           memory and processor time, from /proc/self
//             AutostartEntry.cs          starting with the session, through ~/.config/autostart
//             LinuxScreens.cs            the screens and the active window
//             LinuxQuietState.cs         do not disturb
//             SilentConfirmSound.cs      the app plays nothing on Linux: done
//   Input/    LinuxHotkeys.cs            the global shortcuts: the GlobalShortcuts portal, else XGrabKey on X11
//   Tray/     StatusNotifierTray.cs      the icon and its menu, through StatusNotifierItem (LinuxShell)
//   Updates/  LinuxHttpTransport.cs      the HTTP of the update check
//   DBus/     SessionBus.cs, BusCalls.cs the session bus every part shares: done

/// <summary>The Linux layer: the classes that reach the sound server, D-Bus and the desktop, handed to the portable code.</summary>
[SupportedOSPlatform("linux")]
internal static class LinuxPlatform
{
    private static readonly object Lock = new();
    private static bool _installed;
    private static Action<string> _log = _ => { };

    /// <summary>Fills <see cref="PlatformDefaults"/>. Called first thing by the entry point, and by the tests; once is enough.</summary>
    /// <param name="log">The log of the run: the parts not there yet say so in it.</param>
    public static void Install(Action<string> log)
    {
        lock (Lock)
        {
            if (_installed)
            {
                return;
            }

            _log = log;
            SessionBus.Shared = new SessionBus(SessionAddress, log);
            PlatformDefaults.CaptureFactory = () => new PulseCaptureFactory(log);
            PlatformDefaults.DeviceWatcher = () => new PulseDeviceWatcher(log);
            PlatformDefaults.ProcessSnapshot = () => new ProcProcessSnapshot();
            PlatformDefaults.RunKey = () => new AutostartEntry(log);
            PlatformDefaults.LaunchPath = LaunchPaths.Current;
            PlatformDefaults.Hotkeys = () => new LinuxHotkeys(log);
            PlatformDefaults.UpdateNetwork = () => new LinuxHttpTransport(log);
            PlatformDefaults.UpdateChannel = Hindsound.Updates.UpdateChannel.LinuxAnnounce;
            PlatformDefaults.Mp3Encoder = LameMp3Encoder.Open;
            PlatformDefaults.Desktop = Desktop;
            PlatformDefaults.Shell = () => new LinuxShell(log);
            _installed = true;
        }
    }

    /// <summary>The desktop parts of a Linux run.</summary>
    public static DesktopParts Desktop() =>
        new(new LinuxScreens(), new LinuxQuietState(), new SilentConfirmSound(), new XdgFileOpener(_log), new ProcSelfMeter());

    // Read when the bus is first needed, not at start: a test sets its own session before that.
    private static string? SessionAddress()
    {
        try
        {
            return DBusAddress.Session;
        }
        catch (Exception ex)
        {
            _log("the address of the session bus could not be read: " + ex.GetType().Name);
            return null;
        }
    }
}
