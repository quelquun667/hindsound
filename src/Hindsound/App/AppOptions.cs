using System.Globalization;

namespace Hindsound.App;

/// <summary>The command line. These options stay in the shipped exe: they are how a problem gets diagnosed on a user's PC.</summary>
/// <param name="SettingsPath"><c>--settings file</c>: this file instead of %AppData%\Hindsound\settings.json. The Windows startup entry is then left alone.</param>
/// <param name="ExitAfterSeconds"><c>--exit-after seconds</c>: leaves by itself, as Quit would.</param>
/// <param name="SelfTestSave"><c>--selftest-save</c>: goes through the whole clip path once, writes the result to the log, and leaves.</param>
/// <param name="Offscreen"><c>--offscreen</c>: draws the notifications outside every screen.</param>
/// <param name="DumpNotificationsFolder"><c>--dump-notifications folder</c>: writes each state as a BMP file, shows nothing, and leaves.</param>
/// <param name="DumpSettingsFolder"><c>--dump-settings folder</c>: writes each page of the settings window as a PNG file, shows nothing, and leaves.</param>
/// <param name="SettingsProbe"><c>--settings-probe</c>: opens the settings window, sends it keys, measures the memory, and leaves.</param>
/// <param name="ForceFirstRun"><c>--first-run</c>: opens the first-launch card even though a settings file exists.</param>
internal sealed record AppOptions(
    string? SettingsPath = null,
    double? ExitAfterSeconds = null,
    bool SelfTestSave = false,
    bool Offscreen = false,
    string? DumpNotificationsFolder = null,
    string? DumpSettingsFolder = null,
    bool SettingsProbe = false,
    bool ForceFirstRun = false)
{
    /// <summary><c>--scale n</c>: the scale of <c>--dump-settings</c>, 1 for 100 %, 1.5 for 150 %.</summary>
    public double DumpScale { get; init; } = 1;

    /// <summary><c>--selftest-delay seconds</c>: how long the capture runs before the self-test presses save. By default 3 s.</summary>
    public double SelfTestDelaySeconds { get; init; } = 3;

    /// <summary><c>--selftest-fail card|reload|run</c>: throws at that point, to check on the real exe that the app survives it or leaves cleanly.</summary>
    public string? SelfTestFail { get; init; }

    /// <summary><c>--selftest-game seconds</c>: the app sees no real screen state: for that long after the start, a game in exclusive full screen; then the plain desktop. To check what waits for the desktop on the real exe, whatever the PC is doing.</summary>
    public double? SelfTestGameSeconds { get; init; }

    /// <summary><c>--selftest-clip-gone</c>: after the self-test clip, asks for a copy of a last clip whose files are not there. The clipboard is never reached.</summary>
    public bool SelfTestClipGone { get; init; }

    /// <summary><c>--no-mic</c>: the microphone stays off whatever the settings file says, at the start and at every read of the file. The file keeps its own value.</summary>
    public bool NoMic { get; init; }

    /// <summary><c>--no-clipboard</c>: the clipboard is never touched, whatever the settings file says, for a new clip and for Copy last clip alike. The file keeps its own value.</summary>
    public bool NoClipboard { get; init; }

    /// <summary><c>--no-hotkeys</c>: no global shortcut is registered in this run, whatever the settings file says, so a press never reaches it and it holds no combination. The app sees shortcuts that work. The file keeps its own value.</summary>
    public bool NoHotkeys { get; init; }

    /// <summary><c>--prove-mp3 folder</c>: writes one second of a sine into the folder as an MP3, through the path of a real clip, and leaves: 0, or another code if the clip came out as WAV. No capture, no microphone, no clipboard, no sound. See <see cref="Mp3Proof"/>.</summary>
    public string? ProveMp3Folder { get; init; }

    /// <summary>The Windows startup entry, for a run that may touch it. A test run with its own settings file never does, for reading either.</summary>
    public Settings.IRunKey? RunKey() => SettingsPath is null ? new Settings.RegistryRunKey() : null;

    /// <summary>What the shortcuts of this run go through.</summary>
    public Input.IHotkeyNative NewHotkeyLayer() => NoHotkeys ? new Input.UnregisteredHotkeys() : new Input.Win32HotkeyNative();

    /// <summary><c>--selftest-slow-save seconds</c>: every clip takes that much longer to write. To check on the real exe what leaving does while a clip is being written.</summary>
    public double? SelfTestSlowSaveSeconds { get; init; }

    /// <summary><c>--update-failed</c>: passed by Setup to the version it started again after failing to install a new one.</summary>
    public bool UpdateFailed { get; init; }

    /// <summary><c>--selftest-update folder</c>: with --settings, the whole updater on the files of that folder, with no network, and no Setup is ever started. Without --settings, nothing.</summary>
    public string? SelfTestUpdateFolder { get; init; }

    /// <summary><c>--selftest-update-check</c>: with --settings, one real GET of the manifest, written to the log, then leaves. Without --settings, nothing.</summary>
    public bool SelfTestUpdateCheck { get; init; }

    public const string FailCard = "card", FailReload = "reload", FailRun = "run";

    /// <summary>The only argument a real run may carry: Setup passes it to the version it started again.</summary>
    public const string UpdateFailedOption = "--update-failed";

    /// <summary>
    /// Started as a user starts it: the raw command line is empty, or holds only what Setup passes. Anything else, even an
    /// option nobody reads or a value equal to the default, means a test run, which never reaches the network nor a Setup.
    /// The parsed options could not tell: an unknown or incomplete option is ignored, so a typing error would pass for a real run.
    /// </summary>
    public static bool UpdaterMayRun(IReadOnlyList<string> args) => args.All(arg => arg == UpdateFailedOption);

    /// <summary>A folder after an option: a value that starts with "--" is the next option, the folder was forgotten.</summary>
    private static bool IsFolder(string? value) => value is not null && !value.StartsWith("--", StringComparison.Ordinal);

    /// <summary>Unknown or incomplete options are ignored: the app is started by Windows, nobody could read an error.</summary>
    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        var options = new AppOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var next = i + 1 < args.Count ? args[i + 1] : null;
            switch (args[i])
            {
                case "--settings" when next is not null:
                    options = options with { SettingsPath = next };
                    i++;
                    break;
                case "--exit-after" when next is not null:
                    if (double.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                    {
                        options = options with { ExitAfterSeconds = seconds };
                    }

                    i++;
                    break;
                case "--selftest-delay" when next is not null:
                    if (double.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay) && delay > 0)
                    {
                        options = options with { SelfTestDelaySeconds = delay };
                    }

                    i++;
                    break;
                case "--selftest-game" when next is not null:
                    if (double.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out var game) && game > 0)
                    {
                        options = options with { SelfTestGameSeconds = game };
                    }

                    i++;
                    break;
                case "--selftest-slow-save" when next is not null:
                    if (double.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out var slow) && slow > 0)
                    {
                        options = options with { SelfTestSlowSaveSeconds = slow };
                    }

                    i++;
                    break;
                case "--selftest-fail" when next is not null:
                    options = options with { SelfTestFail = next };
                    i++;
                    break;
                case "--selftest-clip-gone":
                    options = options with { SelfTestClipGone = true };
                    break;
                case "--selftest-save":
                    options = options with { SelfTestSave = true };
                    break;
                case "--no-mic":
                    options = options with { NoMic = true };
                    break;
                case "--no-clipboard":
                    options = options with { NoClipboard = true };
                    break;
                case "--no-hotkeys":
                    options = options with { NoHotkeys = true };
                    break;
                case "--offscreen":
                    options = options with { Offscreen = true };
                    break;
                case "--dump-settings" when IsFolder(next):
                    options = options with { DumpSettingsFolder = next };
                    i++;
                    break;
                case "--scale" when next is not null:
                    if (double.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale is >= 1 and <= 4)
                    {
                        options = options with { DumpScale = scale };
                    }

                    i++;
                    break;
                case "--first-run":
                    options = options with { ForceFirstRun = true };
                    break;
                case "--settings-probe":
                    options = options with { SettingsProbe = true };
                    break;
                case "--dump-notifications" when IsFolder(next):
                    options = options with { DumpNotificationsFolder = next };
                    i++;
                    break;
                case "--prove-mp3" when IsFolder(next):
                    options = options with { ProveMp3Folder = next };
                    i++;
                    break;
                case UpdateFailedOption:
                    options = options with { UpdateFailed = true };
                    break;
                case "--selftest-update" when IsFolder(next):
                    options = options with { SelfTestUpdateFolder = next };
                    i++;
                    break;
                case "--selftest-update-check":
                    options = options with { SelfTestUpdateCheck = true };
                    break;
            }
        }

        // The update self-tests write the state of the updater and may reach GitHub: only a run with its own settings file,
        // and so its own log, may ask for them. Alone, they are ignored like an unknown option.
        if (options.SettingsPath is null)
        {
            options = options with { SelfTestUpdateFolder = null, SelfTestUpdateCheck = false };
        }

        return options;
    }
}
