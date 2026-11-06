namespace Hindsound.App;

/// <summary>The entry point: no interface framework, one instance at a time.</summary>
internal static class Program
{
    private const string MutexName = @"Local\Hindsound.SingleInstance";
    private const int ExitAnotherInstance = 2;
    private const int ExitFailed = 3;

    [STAThread]
    private static int Main(string[] args)
    {
        var options = AppOptions.Parse(args);
        var journal = new Journal(Journal.PathFor(options.SettingsPath));

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

        using var instance = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            journal.Write("another instance is running: leaving");
            return ExitAnotherInstance;
        }

        try
        {
            var code = new HindsoundApp(options, journal).Run();

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
