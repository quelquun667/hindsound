using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Linux.App;
using Hindsound.Linux.Desktop;
using Hindsound.Linux.Window;
using Hindsound.Unix.Export;
using Hindsound.Unix.Library;

namespace Hindsound.Linux;

/// <summary>The entry point of the Linux app: one instance at a time, the settings window where it can be drawn, <c>hindsound --save</c> for a key of the desktop.</summary>
[SupportedOSPlatform("linux")]
internal static class Program
{
    private const int ExitFailed = 3;

    private static int Main(string[] args)
    {
        var launch = LinuxLaunch.Read(args);
        var options = AppOptions.Parse(launch.Rest);
        var journal = new Journal(Journal.PathFor(options.SettingsPath));
        LinuxPlatform.Install(journal.Write);

        // Only the log can be done from here: on a thread other than the loop's, the runtime ends the process anyway.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => journal.Write("unhandled exception: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            journal.Write("unobserved task exception: " + e.Exception);
            e.SetObserved();
        };

        if (options.DumpSettingsFolder is not null)
        {
            // Not needed to draw, but the log then says whether a window could open here, and where SDL and the fonts come
            // from: the check of the packages reads it.
            LinuxWindowSupport.Available(journal.Write);
            return LinuxDump.Write(options.DumpSettingsFolder, options.DumpScale, journal.Write);
        }

        if (LinuxLaunch.WindowOnlyOption(options) is { } option)
        {
            journal.Write(option + " draws what the Linux app does not draw itself");
            return ExitFailed;
        }

        if (options.ProveMp3Folder is not null)
        {
            var proof = Mp3Proof.Write(options.ProveMp3Folder, journal);
            return proof == 0 ? Mp3ReadBack.Check(options.ProveMp3Folder, new UnixClipDecoder(), journal.Write) : proof;
        }

        using var instance = LinuxInstanceChannel.Take(options, journal.Write);
        if (!instance.First)
        {
            return SingleInstance.Leave(instance, options, journal.Write, launch.Message);
        }

        if (launch.Save)
        {
            // Nothing is kept yet: a clip now would be empty. The app starts, and the next --save finds it.
            journal.Write("--save with no running instance: starting the app instead");
        }

        // Before the app reaches the portal of global shortcuts, which knows an app only by its entry. A run off screen is a
        // test's, and writes nothing in the user's menu.
        if (!options.Offscreen)
        {
            ApplicationEntry.AtLaunch(LaunchPaths.Current(), journal.Write);
        }

        // A clip that cannot be encoded falls back to WAV without a word: a user who gets WAV files finds the reason here.
        journal.Write(LameLibrary.Shared is { } lame
            ? $"mp3: libmp3lame {lame.Version} from {lame.LoadedFrom}"
            : "mp3: no encoder, clips will be saved as WAV: " + LameLibrary.LastError);

        try
        {
            // The raw command line, not the options read from it: a typo in a test's options must not look like a real launch.
            var code = new HindsoundApp(options, journal, AppOptions.UpdaterMayRun(launch.Rest), LinuxPlatform.Desktop(), shell: new LinuxShell(journal.Write), instance: instance).Run();

            // A capture thread stuck in the sound server must not keep the process alive with the icon gone.
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
