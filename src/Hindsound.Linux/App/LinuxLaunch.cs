using Hindsound.App;

namespace Hindsound.Linux.App;

/// <summary>What the command line of the Linux app asks beyond <see cref="AppOptions"/>.</summary>
/// <param name="Save"><c>--save</c>: the running instance saves a clip, as its default shortcut does.</param>
/// <param name="Rest">The other arguments, for <see cref="AppOptions.Parse"/>.</param>
internal sealed record LinuxLaunch(bool Save, IReadOnlyList<string> Rest)
{
    public const string SaveOption = "--save";

    /// <summary>What a second launch tells the running instance.</summary>
    public InstanceMessage Message => Save ? InstanceMessage.Save : InstanceMessage.Show;

    public static LinuxLaunch Read(IReadOnlyList<string> args)
    {
        var rest = args.Where(arg => arg != SaveOption).ToArray();
        return new LinuxLaunch(rest.Length != args.Count, rest);
    }

    /// <summary>An option that only means something with a window, or null: the Linux app has none to draw.</summary>
    public static string? WindowOnlyOption(AppOptions options) =>
        options.DumpSettingsFolder is not null ? "--dump-settings"
        : options.DumpNotificationsFolder is not null ? "--dump-notifications"
        : null;
}
