namespace Hindsound.App;

/// <summary>The entry point: no interface framework, one instance at a time.</summary>
internal static class Program
{
    private const int ExitFailed = 3;

    /// <inheritdoc cref="SingleInstance.WakesTheFirstInstance"/>
    internal static bool WakesTheFirstInstance(AppOptions options) => SingleInstance.WakesTheFirstInstance(options);

    /// <summary>
    /// The mutex of <see cref="SingleInstance.NameFor"/>, in the namespace of the user's session. Setup waits on the plain
    /// name, so a real launch keeps it.
    /// </summary>
    internal static string MutexNameFor(AppOptions options) => @"Local\" + SingleInstance.NameFor(options);

    /// <summary>Holds the name for as long as the channel lives. <paramref name="first"/> says whether nobody held it before.</summary>
    internal static WindowsInstanceChannel TakeInstance(AppOptions options, out bool first) => new(new Mutex(true, MutexNameFor(options), out first), first);

    [STAThread]
    private static int Main(string[] args)
    {
        WindowsPlatform.Install();
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

        using var instance = TakeInstance(options, out var first);
        if (!first)
        {
            return SingleInstance.Leave(instance, options, journal.Write);
        }

        try
        {
            // The raw command line, not the options read from it: a typo in a test's options must not look like a real launch.
            var code = new HindsoundApp(options, journal, AppOptions.UpdaterMayRun(args), WindowsPlatform.Desktop(), instance: instance).Run();

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
