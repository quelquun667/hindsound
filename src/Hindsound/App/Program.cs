using System.Security.Cryptography;
using System.Text;

namespace Hindsound.App;

/// <summary>The entry point: no interface framework, one instance at a time.</summary>
internal static class Program
{
    private const string MutexName = @"Local\Hindsound.SingleInstance";
    private const string TestMutexPrefix = @"Local\Hindsound.SingleInstance.Test.";
    private const int ExitAnotherInstance = 2;
    private const int ExitFailed = 3;

    /// <summary>
    /// A test run with its own settings file never reaches for the user's real instance. Nor does a version Setup started
    /// again after a failed update: waking the running one would open its settings in front of whatever the user is doing.
    /// </summary>
    internal static bool WakesTheFirstInstance(AppOptions options) => options.SettingsPath is null && !options.UpdateFailed;

    /// <summary>
    /// Setup waits on the plain name, so a real launch keeps it. A run with its own settings file gets a name of its own, so
    /// it can run beside the user's copy, and two runs on one file still refuse each other. The name has to be the same from
    /// one process to the next, which rules out <see cref="string.GetHashCode()"/>; Windows paths ignore case, so must the name.
    /// </summary>
    internal static string MutexNameFor(AppOptions options)
    {
        if (options.SettingsPath is not { } path)
        {
            return MutexName;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
        return TestMutexPrefix + Convert.ToHexString(hash);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var options = AppOptions.Parse(args);
        var journal = new Journal(Journal.PathFor(options.SettingsPath));
        Ui.TextEngine.Log = journal.Write;

        // Only the log can be done from here: on a thread other than the loop's, the runtime ends the process anyway.
        // Every thread this app starts catches its own exceptions, so this is for those of a library.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => journal.Write("unhandled exception: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            journal.Write("unobserved task exception: " + e.Exception);
            e.SetObserved();
        };

        if (options.DumpNotificationsFolder is not null)
        {
            return NotificationDump.Write(options.DumpNotificationsFolder, journal);
        }

        if (options.ProveMp3Folder is not null)
        {
            return Mp3Proof.Write(options.ProveMp3Folder, journal);
        }

        if (options.DumpSettingsFolder is not null)
        {
            try
            {
                return Ui.Window.SettingsDump.Write(options.DumpSettingsFolder, options.DumpScale);
            }
            catch (Exception ex)
            {
                journal.Write("dump failed: " + ex);
                return ExitFailed;
            }
        }

        using var instance = new Mutex(true, MutexNameFor(options), out var first);
        if (!first)
        {
            journal.Write("another instance is running: leaving");

            // The icon may be hidden: without an answer, a click on the shortcut would look like nothing happened.
            if (WakesTheFirstInstance(options) && !Ui.UiLoop.WakeRunningInstance())
            {
                journal.Write("the running instance could not be told");
            }

            return ExitAnotherInstance;
        }

        try
        {
            // The raw command line, not the options read from it: a typo in a test's options must not look like a real launch.
            var code = new HindsoundApp(options, journal, AppOptions.UpdaterMayRun(args)).Run();

            // A capture thread stuck in the driver must not keep the process alive with the icon gone.
            Environment.Exit(code);
            return code;
        }
        catch (Exception ex)
        {
            journal.Write("fatal: " + ex);

            // The same as a normal exit: nothing may be left capturing behind a process that has given up.
            Environment.Exit(ExitFailed);
            return ExitFailed;
        }
    }
}
