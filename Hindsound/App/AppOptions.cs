using System.Globalization;

namespace Hindsound.App;

/// <summary>The command line. These options stay in the shipped exe: they are how a problem gets diagnosed on a user's PC.</summary>
/// <param name="SettingsPath"><c>--settings file</c>: this file instead of %AppData%\Hindsound\settings.json. The Windows startup entry is then left alone.</param>
/// <param name="ExitAfterSeconds"><c>--exit-after seconds</c>: leaves by itself, as Quit would.</param>
/// <param name="SelfTestSave"><c>--selftest-save</c>: goes through the whole clip path once, writes the result to the log, and leaves.</param>
/// <param name="Offscreen"><c>--offscreen</c>: draws the notifications outside every screen.</param>
/// <param name="DumpNotificationsFolder"><c>--dump-notifications folder</c>: writes each state as a BMP file, shows nothing, and leaves.</param>
internal sealed record AppOptions(
    string? SettingsPath = null,
    double? ExitAfterSeconds = null,
    bool SelfTestSave = false,
    bool Offscreen = false,
    string? DumpNotificationsFolder = null)
{
    /// <summary><c>--selftest-delay seconds</c>: how long the capture runs before the self-test presses save. By default 3 s.</summary>
    public double SelfTestDelaySeconds { get; init; } = 3;

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
                case "--selftest-save":
                    options = options with { SelfTestSave = true };
                    break;
                case "--offscreen":
                    options = options with { Offscreen = true };
                    break;
                case "--dump-notifications" when next is not null:
                    options = options with { DumpNotificationsFolder = next };
                    i++;
                    break;
            }
        }

        return options;
    }
}
