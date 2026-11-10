using System.Security.Cryptography;
using System.Text;

namespace Hindsound.App;

/// <summary>What a second launch can tell the instance that runs.</summary>
internal enum InstanceMessage
{
    /// <summary>The user is looking for the app: show it.</summary>
    Show,
}

/// <summary>
/// One instance at a time per settings file: the first launch holds a name, a second one tells it something and leaves. The
/// name is held for as long as the channel lives.
/// </summary>
internal interface IInstanceChannel : IDisposable
{
    /// <summary>Whether this process is the one instance: false when another one held the name first.</summary>
    bool First { get; }

    /// <summary>From a second launch: tells the running instance. False if no running instance could be told.</summary>
    bool Tell(InstanceMessage message);

    /// <summary>In the running instance, once its loop exists: <paramref name="received"/> gets each message on the loop's thread.</summary>
    void Listen(IAppLoop loop, Action<InstanceMessage> received);
}

/// <summary>The rules of the single instance, the same on every system: the key of the name, and what a second launch does.</summary>
internal static class SingleInstance
{
    /// <summary>The exit code of a second launch.</summary>
    public const int ExitAnotherInstance = 2;

    /// <summary>The name of a real launch. Setup waits on it, so it never changes.</summary>
    public const string Name = "Hindsound.SingleInstance";

    private const string TestPrefix = "Hindsound.SingleInstance.Test.";

    /// <summary>
    /// A test run with its own settings file never reaches for the user's real instance. Nor does a version Setup started
    /// again after a failed update: waking the running one would open its settings in front of whatever the user is doing.
    /// </summary>
    public static bool WakesTheFirstInstance(AppOptions options) => options.SettingsPath is null && !options.UpdateFailed;

    /// <summary>
    /// A real launch keeps the plain name. A run with its own settings file gets a name of its own, so it can run beside the
    /// user's copy, and two runs on one file still refuse each other. The name has to be the same from one process to the next,
    /// which rules out <see cref="string.GetHashCode()"/>; Windows paths ignore case, so must the name, and no other system's
    /// do. It is a key, not a file name: a channel that needs a shorter or a per-user one derives it from this.
    /// </summary>
    public static string NameFor(AppOptions options) => NameFor(options, ignoreCase: OperatingSystem.IsWindows());

    /// <summary><see cref="NameFor(AppOptions)"/> with the case rule of the path given, for a test on any system.</summary>
    public static string NameFor(AppOptions options, bool ignoreCase)
    {
        if (options.SettingsPath is not { } path)
        {
            return Name;
        }

        var full = Path.GetFullPath(path);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ignoreCase ? full.ToUpperInvariant() : full));
        return TestPrefix + Convert.ToHexString(hash);
    }

    /// <summary>A second launch: says so, tells the running instance when it may, and answers the exit code.</summary>
    public static int Leave(IInstanceChannel channel, AppOptions options, Action<string> journal)
    {
        journal("another instance is running: leaving");

        // The icon may be hidden: without an answer, a click on the shortcut would look like nothing happened.
        if (WakesTheFirstInstance(options) && !channel.Tell(InstanceMessage.Show))
        {
            journal("the running instance could not be told");
        }

        return ExitAnotherInstance;
    }
}
