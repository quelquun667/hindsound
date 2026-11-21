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

    public static MacLaunch Read(IReadOnlyList<string> args)
    {
        var rest = args.Where(arg => arg is not SaveOption and not ProveLoopOption).ToArray();
        return new MacLaunch(args.Contains(SaveOption), args.Contains(ProveLoopOption), rest);
    }
}
