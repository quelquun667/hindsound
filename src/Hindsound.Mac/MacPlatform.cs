using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Input;
using Hindsound.Mac.App;
using Hindsound.Mac.Desktop;
using Hindsound.Mac.Input;
using Hindsound.Unix.Export;
using Hindsound.Unix.Updates;
using Hindsound.Unix.Window;

namespace Hindsound.Mac;

// Each part the portable code needs is one line below (or one member of App/MacShell.cs), and one class in its own file.
//
//   there:   the folders of macOS (Core: UserFolders, Journal), the MP3 (Hindsound.Unix, libmp3lame.0.dylib of the bundle),
//            the HTTP of the update check, the pages of the window and their text (FreeType, HarfBuzz and CoreText for
//            the fallback font: Window/), the loop on the events of NSApplication on the main thread (App/), the single
//            instance through a Unix socket and --save from a second launch (Hindsound.Unix, App/MacLaunch.cs), the icon
//            of the menu bar (Tray/), the Carbon shortcuts (Input/), the pasteboard, open and reveal, the Trash, opening
//            at login through a launch agent, the screens, the meter and the end of the session (Desktop/), the
//            Objective-C helper (Interop/ObjC.cs).
//   off:     the capture, the devices, the process list and the listening (step M3); the notification drawn by the app
//            (step M2b); the update channel of macOS (step M4). Each is left null here or throws in MacShell, and
//            PlatformFeatures.Mac turns off what the pages would offer for it.

/// <summary>The macOS layer: the classes handed to the portable code, and nothing that pretends to work.</summary>
internal static class MacPlatform
{
    /// <summary>The option that asks the running instance for a clip.</summary>
    public const string SaveOption = "--save";

    private static readonly object Lock = new();
    private static bool _installed;

    /// <summary>Fills <see cref="PlatformDefaults"/> with what the Mac app has. Once; the parts not there yet stay null.</summary>
    [SupportedOSPlatform("macos")]
    public static void Install(Action<string> log)
    {
        lock (Lock)
        {
            if (_installed)
            {
                return;
            }

            UnixLibraries.InstallResolver();
            PlatformDefaults.LaunchPath = () => Environment.ProcessPath;
            PlatformDefaults.Features = PlatformFeatures.Mac(() => SaveCommand(Environment.ProcessPath), ShortcutsNow);
            PlatformDefaults.Hotkeys = () => new KeyByKeyHotkeys(CarbonHotkeys.TryOpen(log) ?? throw new NotSupportedException("the Carbon shortcuts need the main thread"));
            PlatformDefaults.UpdateNetwork = () => new UnixHttpTransport(log);
            PlatformDefaults.Mp3Encoder = LameMp3Encoder.Open;
            PlatformDefaults.Shell = () => new MacShell(log);
            PlatformDefaults.RunKey = () => new LaunchAgentEntry();
            PlatformDefaults.Desktop = () => MacDesktop.Parts(log);
            _installed = true;
        }
    }

    /// <summary>The field of each action listens to the keyboard once the Carbon shortcuts run; until then, a command to bind.</summary>
    internal static ShortcutMode ShortcutsNow() => OperatingSystem.IsMacOS() && CarbonHotkeys.Ready ? ShortcutMode.Capture : ShortcutMode.Command;

    /// <summary>
    /// The command that saves a clip, for the Hotkeys page: the program inside the bundle with <c>--save</c>. Null when the
    /// path holds a character a shell would read: the page then says it in words.
    /// </summary>
    internal static string? SaveCommand(string? program) =>
        program is { Length: > 1 } && program[0] == '/' && program.All(IsPlain) ? program + " " + SaveOption : null;

    private static bool IsPlain(char c) => char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '_' or '-';
}
