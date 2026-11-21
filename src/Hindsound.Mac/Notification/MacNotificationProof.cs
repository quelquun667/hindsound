using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Mac.App;
using Hindsound.Mac.Window;
using Hindsound.Ui;
using Hindsound.Unix.App;
using Hindsound.Unix.Notification;

namespace Hindsound.Mac.Notification;

/// <summary>
/// <c>--prove-notification folder</c>, for the CI: shows a real notification (a saved clip, with its bars) through SDL's Cocoa
/// window, on the run loop of the main thread, and says in the console what it sees. It writes <c>folder/ready</c> once the
/// notification is up, with where it is and how many pixels of the accent the picture has, and holds it there until
/// <c>folder/captured</c> appears (the CI takes its screenshot meanwhile); then it clicks it with a mouse event of the system,
/// lets it go and leaves. 0 when the window showed, the app in front stayed in front, the window never had the focus, and the
/// notification left the screen; 3 otherwise. The click is reported, not required: the runner may refuse synthetic events.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacNotificationProof
{
    public const string Option = "--prove-notification";

    private const int ExitFailed = 3;
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>The folder after <see cref="Option"/>, or null when it was not asked.</summary>
    public static string? Folder(string[] args)
    {
        var at = Array.IndexOf(args, Option);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>A saved clip of 30 seconds, copied: the notification with bars, the accent in them.</summary>
    internal static NotificationContent Sample(uint accent)
    {
        float[] bars =
        [
            0.25f, 0.25f, 0.3f, 0.39f, 0.42f, 0.4f, 0.15f, 0.12f, 0.38f, 0.44f, 0.51f, 0.26f,
            0.14f, 0.23f, 0.35f, 0.44f, 0.36f, 0.35f, 0.78f, 0.84f, 0.88f, 0.59f, 0.8f, 0.8f,
        ];
        var thirty = TimeSpan.FromSeconds(30);
        return NotificationStates.ForClip(new ClipResult(["clip.mp3"], thirty, 1, false, bars, false, false), thirty, true, true, accent);
    }

    public static int Run(string folder, Action<string> say)
    {
        void Log(string line) => say("notification-proof " + line);

        Directory.CreateDirectory(folder);
        var ready = Path.Combine(folder, "ready");
        var captured = Path.Combine(folder, "captured");
        File.Delete(ready);
        File.Delete(captured);
        NativeLibrary.Load(AppKit);

        var before = Frontmost();
        Log($"focus.before pid={before.Pid} name={before.Name} self={Environment.ProcessId}");

        using var pump = new MacRunLoopPump();
        using var loop = new UnixLoop(nowMs: null, idle: pump);
        SdlNotificationSurface? surface = null;
        using var view = new NotificationPresenter(loop, () => surface = new SdlNotificationSurface(new MacNotificationHook()), MacText.Create, Log)
        {
            HoldOpen = true,
        };

        var accent = NotificationPalette.Lime;
        int clicks = 0, finished = 0;
        var ok = true;
        view.Clicked = () => clicks++;
        view.Finished = () =>
        {
            finished++;
            loop.Quit();
        };

        void Check(string what, bool good, string detail)
        {
            Log($"{what} {(good ? "ok" : "FAILED")} {detail}");
            ok &= good;
        }

        void ClickIt()
        {
            var bounds = view.Bounds;
            var x = bounds.Left + bounds.Width * 0.6;
            var y = bounds.Top + bounds.Height * 0.5;
            Log($"click.post_access {(CGPreflightPostEventAccess() != 0 ? 1 : 0)} at={x.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)}");
            Post(5, x, y);
            Post(1, x, y);
            loop.After(120, () =>
            {
                Post(2, x, y);
                loop.After(1200, () =>
                {
                    Log($"click.received {clicks}");
                    Log($"focus.after_click app_active={(AppActive() ? 1 : 0)} front={Frontmost().Name}");
                    view.HoldOpen = false;
                });
            });
        }

        void WaitForCapture(int turn)
        {
            if (File.Exists(captured))
            {
                Log("capture.done");
                ClickIt();
            }
            else if (turn >= 900)
            {
                Log("capture.never_came");
                view.HoldOpen = false;
            }
            else
            {
                loop.After(100, () => WaitForCapture(turn + 1));
            }
        }

        loop.Post(() =>
        {
            view.Present(Sample(accent), new NotificationSpot(default, 96, ClickThrough: false, LeftEdge: false), accent);
            loop.After(800, () =>
            {
                var after = Frontmost();
                var shown = surface is { Hidden: false } && view.Frames > 0;
                Check("window.shown", shown, $"frames={view.Frames} scale={view.Scale.ToString(CultureInfo.InvariantCulture)}");
                Check("focus.front_kept", after.Pid == before.Pid && after.Pid != Environment.ProcessId, $"pid={after.Pid} name={after.Name}");
                Check("focus.app_not_active", !AppActive(), string.Empty);
                Check("focus.window_not_key", surface is { HasFocus: false }, string.Empty);

                var bounds = view.Bounds;
                var screen = surface?.MainScreen() ?? default;
                var expected = view.Picture?.Count(accent) ?? 0;
                var lines = new[]
                {
                    $"x={bounds.Left}", $"y={bounds.Top}", $"w={bounds.Width}", $"h={bounds.Height}",
                    $"screen_w={screen.Width}", $"screen_h={screen.Height}",
                    $"accent={accent:X6}", $"expected={expected}",
                    $"scale={view.Scale.ToString(CultureInfo.InvariantCulture)}",
                };
                Log("window.place " + string.Join(' ', lines));
                File.WriteAllLines(ready, lines);
                WaitForCapture(0);
            });
        });

        // Never stuck: whatever happens, the proof ends.
        loop.After(150_000, () =>
        {
            Log("timeout");
            loop.Quit();
        });

        loop.Run();
        Check("finished", finished == 1, $"clicks={clicks}");
        return ok ? 0 : ExitFailed;
    }

    /// <summary>The app in front, as the Dock sees it: its process and its name; 0 and "none" when there is none.</summary>
    internal static (int Pid, string Name) Frontmost()
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var workspace = MacNotificationHook.Messages.Send(MacNotificationHook.Messages.Class("NSWorkspace"), "sharedWorkspace");
            var app = workspace == 0 ? 0 : MacNotificationHook.Messages.Send(workspace, "frontmostApplication");
            return app == 0
                ? (0, "none")
                : (MacNotificationHook.Messages.SendInt32(app, "processIdentifier"),
                    MacNotificationHook.Messages.Text(MacNotificationHook.Messages.Send(app, "localizedName")) ?? "?");
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    // NSApp exists once SDL's video has started; asking before would make a plain one in place of SDL's.
    private static bool AppActive()
    {
        var application = MacNotificationHook.Messages.Send(MacNotificationHook.Messages.Class("NSApplication"), "sharedApplication");
        return application != 0 && MacNotificationHook.Messages.SendBool(application, "isActive");
    }

    // A mouse event of the system at a point of the desktop, in points from the top left of the main screen.
    private static void Post(uint type, double x, double y)
    {
        var e = CGEventCreateMouseEvent(0, type, new CGPoint(x, y), 0);
        if (e == 0)
        {
            return;
        }

        CGEventPost(0, e);
        CFRelease(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint(double x, double y)
    {
        public readonly double X = x, Y = y;
    }

    [LibraryImport(CoreGraphics)]
    private static partial nint CGEventCreateMouseEvent(nint source, uint type, CGPoint position, uint button);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventPost(uint tap, nint e);

    [LibraryImport(CoreGraphics)]
    private static partial byte CGPreflightPostEventAccess();

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);

    [LibraryImport("/usr/lib/libobjc.A.dylib")]
    private static partial nint objc_autoreleasePoolPush();

    [LibraryImport("/usr/lib/libobjc.A.dylib")]
    private static partial void objc_autoreleasePoolPop(nint pool);
}
