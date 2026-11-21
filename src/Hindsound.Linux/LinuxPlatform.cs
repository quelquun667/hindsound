using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.Audio;
using Hindsound.Linux.DBus;
using Hindsound.Linux.Desktop;
using Hindsound.Linux.Input;
using Hindsound.Unix.Desktop;
using Hindsound.Unix.Export;
using Hindsound.Unix.Updates;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux;

// Each part the portable code needs is one line below (or one line of App/LinuxShell.cs), and one class in its own file.
//
//   Audio/    PulseCaptureFactory.cs     capture: the monitor of the default output, and the microphone
//             PulsePlaybackSilence.cs    the PC's sound read as silence while a clip is listened to (one per process)
//             PulsePreviewPlayer.cs      listening to a clip, through a playback stream (LinuxShell)
//             PulseDeviceWatcher.cs      a change of the default output or of the microphone
//             ProcProcessSnapshot.cs     the process list, read from /proc
//             PulseAudioChoices.cs       the devices and the applications the settings offer (LinuxShell)
//             PulseAppCaptureSource.cs   capture: one application and its children (PulseAppRules.cs, PulseAppMixer.cs)
//   Export/   LameMp3Encoder.cs          the MP3, through the libmp3lame.so.0 shipped with the app
//   App/      UnixLoop.cs               the loop of the app: done
//             UnixInstanceChannel.cs    one instance at a time, and what "hindsound" or "hindsound --save" tells it (Program)
//             LaunchPaths.cs             the file the session starts again: done
//             LinuxWindowSupport.cs      whether the window can be drawn here: SDL, FreeType, HarfBuzz, fontconfig, the fonts
//             NoSettingsProbe.cs         --settings-probe where the window cannot be drawn
//   Window/   UnixSettingsWindow.cs     the settings window, on SDL (LinuxShell); UnixSettingsProbe.cs, its probe
//             SdlNative.cs, SdlVideo.cs, SdlPump.cs, SdlLibrary.cs, SdlWindowSizing.cs   SDL 3.2 and the arithmetic of the window
//             LinuxDump.cs               --dump-settings, on the text of the window
//             Text/FreeTypeText.cs       the text of the window, with the fonts shipped with the app
//             Input/                     the keys, the text field, the clipboard text and the folder dialog of the window
//   Library/  UnixClipDecoder.cs        reading a clip back for the editor and the listening (LinuxShell)
//   Desktop/  FreedesktopNotification.cs the notification, silent, with "Show in folder" (LinuxShell)
//             LinuxClipboard.cs          the clip on the clipboard as text/uri-list (LinuxShell)
//             LinuxFolderShell.cs        the folder of the clips, the clip selected (LinuxShell)
//             LinuxSessionEnd.cs         the end of the session (LinuxShell)
//             XdgFileOpener.cs           opening a file or a folder
//             ProcSelfMeter.cs           memory and processor time, from /proc/self
//             AutostartEntry.cs          starting with the session, through ~/.config/autostart
//             LinuxScreens.cs            the screens and the active window
//             LinuxQuietState.cs         do not disturb
//             SilentConfirmSound.cs      no confirmation sound on Linux: done
//             FreedesktopTrash.cs        the Trash, where a deleted clip goes (LinuxShell)
//   Input/    LinuxHotkeys.cs            the global shortcuts: the GlobalShortcuts portal, else XGrabKey on X11
//   Tray/     StatusNotifierTray.cs      the icon and its menu, through StatusNotifierItem (LinuxShell)
//   Updates/  UnixHttpTransport.cs      the HTTP of the update check
//   DBus/     SessionBus.cs, BusCalls.cs the session bus every part shares: done

/// <summary>The Linux layer: the classes that reach the sound server, D-Bus and the desktop, handed to the portable code.</summary>
[SupportedOSPlatform("linux")]
internal static class LinuxPlatform
{
    private static readonly object Lock = new();
    private static bool _installed;
    private static Action<string> _log = _ => { };
    private static HotkeyChooser? _hotkeys;

    /// <summary>
    /// The one silence of the process: the listening holds it while a clip plays, and the capture of the PC's sound reads it.
    /// Both sides must share it, or a clip listened to would end up in the next clip.
    /// </summary>
    public static PulsePlaybackSilence Silence { get; } = new();

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
            PlatformDefaults.CaptureFactory = () => new PulseCaptureFactory(log, Silence);
            PlatformDefaults.DeviceWatcher = () => new PulseDeviceWatcher(log);
            PlatformDefaults.ProcessSnapshot = () => new ProcProcessSnapshot();
            PlatformDefaults.RunKey = () => new AutostartEntry(log);
            PlatformDefaults.LaunchPath = LaunchPaths.Current;
            PlatformDefaults.Hotkeys = () =>
            {
                var hotkeys = new LinuxHotkeys(log);
                Volatile.Write(ref _hotkeys, hotkeys);
                return hotkeys;
            };
            PlatformDefaults.Features = PlatformFeatures.Linux(ShortcutModeNow, () => SaveCommand(LaunchPaths.Current()));
            PlatformDefaults.UpdateNetwork = () => new UnixHttpTransport(log);
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

    // Read at each build of a page: the portal answers after the window may have opened, and the screen then builds again.
    private static ShortcutMode ShortcutModeNow() =>
        Volatile.Read(ref _hotkeys) is { } hotkeys ? HotkeyChoice.ModeOf(hotkeys.Layer, hotkeys.Pending) : ShortcutMode.Command;

    /// <summary>
    /// The command the Hotkeys page shows and copies for <paramref name="program"/>, the file launched: the one the notice
    /// names, or null where the notice falls back to words. Never a bare "hindsound --save": nothing puts an AppImage on the
    /// PATH. A .deb is started by /usr/bin/hindsound, a link to /usr/lib/hindsound/hindsound, and names that real program.
    /// </summary>
    internal static string? SaveCommand(string? program) => HotkeyChoice.SaveCommandFor(program);

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
