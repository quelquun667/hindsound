using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Mac.App;
using Hindsound.Mac.Audio.Capture;
using Hindsound.Mac.Audio.Devices;
using Hindsound.Mac.Audio.Playback;
using Hindsound.Mac.Desktop;
using Hindsound.Mac.Notification;
using Hindsound.Mac.Window;
using Hindsound.Unix.App;
using Hindsound.Unix.Export;
using Hindsound.Unix.Library;
using Hindsound.Unix.Window;

namespace Hindsound.Mac;

/// <summary>
/// The entry point of the macOS app: one instance at a time, its loop on the main thread (the thread <c>Main</c> runs on,
/// which AppKit, the icon of the menu bar and the Carbon shortcuts need), and <c>hindsound --save</c> from a second launch.
/// The proofs of the CI (<c>--prove-…</c>, <c>--dump-settings</c>) run before it and leave.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class Program
{
    private const int ExitFailed = 3;

    private static int Main(string[] args)
    {
        if (MacNotificationProof.Folder(args) is { } notificationProof)
        {
            return MacNotificationProof.Run(notificationProof, Console.WriteLine);
        }

        if (MacCaptureProofOptions.Read(args) is { } captureProof)
        {
            return MacCaptureProof.Run(captureProof.Folder, captureProof.Denied, Console.WriteLine);
        }

        if (MacDevicesProof.Folder(args) is { } devicesProof)
        {
            return MacDevicesProof.Run(devicesProof, Console.WriteLine);
        }

        var launch = MacLaunch.Read(args);
        var options = AppOptions.Parse(launch.Rest);
        var journal = new Journal(Journal.PathFor(options.SettingsPath));
        MacPlatform.Install(journal.Write);

        AppDomain.CurrentDomain.UnhandledException += (_, e) => journal.Write("unhandled exception: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            journal.Write("unobserved task exception: " + e.Exception);
            e.SetObserved();
        };

        if (MacPreviewProof.Folder(args) is { } previewProof)
        {
            return MacPreviewProof.Run(previewProof, journal.Write);
        }

        if (options.DumpSettingsFolder is not null)
        {
            // The log then says whether a window could open here, and where SDL and the fonts come from.
            MacWindowSupport.Available(journal.Write);
            return UnixDump.Write(options.DumpSettingsFolder, options.DumpScale, MacText.Create, journal.Write);
        }

        if (options.ProveMp3Folder is not null)
        {
            journal.Write(LameLibrary.Shared is { } lame
                ? $"mp3: libmp3lame {lame.Version}"
                : "mp3: no encoder: " + LameLibrary.LastError);
            var proof = Mp3Proof.Write(options.ProveMp3Folder, journal);
            return proof == 0 ? Mp3ReadBack.Check(options.ProveMp3Folder, new UnixClipDecoder(), journal.Write) : proof;
        }

        using var instance = UnixInstanceChannel.Take(options, journal.Write);
        if (!instance.First)
        {
            return SingleInstance.Leave(instance, options, journal.Write, launch.Message);
        }

        if (launch.ProveLoop)
        {
            return MacLoopProof.Run(instance, journal.Write);
        }

        if (launch.Save)
        {
            // Nothing is kept yet: a clip now would be empty. The app starts, and the next --save finds it.
            journal.Write("--save with no running instance: starting the app instead");
        }

        // A clip that cannot be encoded falls back to WAV without a word: a user who gets WAV files finds the reason here.
        journal.Write(LameLibrary.Shared is { } encoder
            ? $"mp3: libmp3lame {encoder.Version} from {encoder.LoadedFrom}"
            : "mp3: no encoder, clips will be saved as WAV: " + LameLibrary.LastError);

        try
        {
            // On the thread of Main, which is the main thread of the process: the loop of the app must turn there. The raw
            // command line, not the options read from it: a typo in a test's options must not look like a real launch. The
            // update channel of macOS comes with step M4: until then no update is looked for (the log says "not installed").
            var code = new HindsoundApp(options, journal, AppOptions.UpdaterMayRun(launch.Rest), MacDesktop.Parts(journal.Write), shell: new MacShell(journal.Write), instance: instance).Run();

            // Launched through Launch Services (open), the app's code reaches no one: the log keeps it.
            journal.Write($"leaving with exit code {code}");

            // A capture thread stuck in Core Audio must not keep the process alive with the icon gone.
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
