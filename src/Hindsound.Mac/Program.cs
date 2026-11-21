using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Mac.App;
using Hindsound.Mac.Notification;
using Hindsound.Mac.Window;
using Hindsound.Unix.App;
using Hindsound.Unix.Export;
using Hindsound.Unix.Library;
using Hindsound.Unix.Window;

namespace Hindsound.Mac;

/// <summary>
/// The entry point of the macOS app. For now it draws the pages of its window (<c>--dump-settings</c>), proves its MP3
/// (<c>--prove-mp3</c>) and its loop (<c>--prove-loop</c>), and hands <c>--save</c> or a second launch to the instance
/// that runs; a plain first launch says in the log that the app cannot run yet (no capture before step M3) and leaves with
/// a failure code, never as if it ran.
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
            journal.Write("--save with no running instance");
        }

        journal.Write("the macOS app does not run yet: it draws its pages (--dump-settings) and proves its MP3 (--prove-mp3); "
            + "the capture, the icon and the shortcuts come with the next steps");
        return ExitFailed;
    }
}
