using System.ComponentModel;
using System.Diagnostics;
using Hindsound.App;

namespace Hindsound.Mac.App;

/// <summary>What the command line of the Mac app asks beyond <see cref="AppOptions"/>.</summary>
/// <param name="Save"><c>--save</c>: the running instance saves a clip, as its default shortcut does.</param>
/// <param name="ProveLoop"><c>--prove-loop</c>: the proof of the loop, the icon, the shortcuts and <c>--save</c>, for the CI.</param>
/// <param name="Rest">The other arguments, for <see cref="AppOptions.Parse"/>.</param>
internal sealed record MacLaunch(bool Save, bool ProveLoop, IReadOnlyList<string> Rest)
{
    public const string SaveOption = MacPlatform.SaveOption;

    public const string ProveLoopOption = "--prove-loop";

    /// <summary>What a second launch tells the running instance.</summary>
    public InstanceMessage Message => Save ? InstanceMessage.Save : InstanceMessage.Show;

    public const string OpenProgram = "/usr/bin/open";

    public const int Started = 0;

    public const int NotStarted = 3;

    private const string InsideBundle = "/Contents/MacOS/";

    public static MacLaunch Read(IReadOnlyList<string> args)
    {
        var rest = args.Where(arg => arg is not SaveOption and not ProveLoopOption).ToArray();
        return new MacLaunch(args.Contains(SaveOption), args.Contains(ProveLoopOption), rest);
    }

    /// <summary>
    /// The bundle a program runs from: <c>/Applications/Hindsound.app</c> for
    /// <c>/Applications/Hindsound.app/Contents/MacOS/hindsound</c>; null for a program outside a <c>.app</c>, such as the
    /// output of a build.
    /// </summary>
    public static string? BundleOf(string? program)
    {
        if (program is null || !program.StartsWith('/'))
        {
            return null;
        }

        var at = program.LastIndexOf(InsideBundle, StringComparison.Ordinal);
        if (at <= 0 || at + InsideBundle.Length == program.Length || program.IndexOf('/', at + InsideBundle.Length) >= 0)
        {
            return null;
        }

        var bundle = program[..at];
        return bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && bundle.LastIndexOf('/') < bundle.Length - ".app".Length - 1
            ? bundle
            : null;
    }

    /// <summary>
    /// The arguments of <c>open</c> that start the bundle as the Finder does, with the other options of this launch
    /// (<c>--save</c> left out: nothing is kept yet). <c>-n</c>: a new process even if Launch Services still counts this
    /// one, which is about to leave.
    /// </summary>
    public static IReadOnlyList<string> RelaunchArguments(string bundle, IReadOnlyList<string> rest) =>
        rest.Count == 0 ? ["-n", bundle] : ["-n", bundle, "--args", .. rest];

    /// <summary>
    /// <c>--save</c> with no running instance, from a program inside a bundle: started from Terminal or from the Shortcuts
    /// app, this process belongs to them, so macOS would ask them, not Hindsound, for the permissions of the capture, and
    /// the app would end with them. The bundle is started again through Launch Services (<c>open</c>), on its own; the
    /// caller must have let go of the single instance first.
    /// </summary>
    public static int Relaunch(string bundle, IReadOnlyList<string> rest, Action<string> log, string program = OpenProgram)
    {
        log($"--save with no running instance: starting {bundle} through Launch Services (open)");
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false };
            foreach (var argument in RelaunchArguments(bundle, rest))
            {
                start.ArgumentList.Add(argument);
            }

            using var open = Process.Start(start);
            if (open is null)
            {
                log("--save: open did not start");
                return NotStarted;
            }

            if (!open.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                log("--save: open did not end within 30 s");
                return NotStarted;
            }

            log($"--save: open ended with code {open.ExitCode}");
            return open.ExitCode == 0 ? Started : NotStarted;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            log("--save: open failed: " + ex.Message);
            return NotStarted;
        }
    }
}
