using System.IO;

namespace Hindsound.Settings;

/// <summary>
/// The folders of the user that hold the files of the app: settings, library, log, clips, rescued clips, update state.
/// Never a relative path, which would put those files wherever the process happened to start.
/// </summary>
internal static class UserFolders
{
    /// <summary>
    /// The folder, whether it exists yet or not; null when the system knows none (no home folder). With the default
    /// <see cref="Environment.SpecialFolderOption.None"/>, a folder that does not exist yet comes back as an empty string,
    /// as the Music folder of a Linux account without one does, and the folder of the app under it became relative. Every
    /// write creates its folder first, so a folder that does not exist yet is fine.
    /// </summary>
    public static string? Find(Environment.SpecialFolder folder) => Find(folder, Resolve);

    /// <param name="resolve">Gives the path of a special folder, as <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> does. For tests.</param>
    internal static string? Find(Environment.SpecialFolder folder, Func<Environment.SpecialFolder, string?> resolve) =>
        resolve(folder) is { Length: > 0 } path && Path.IsPathFullyQualified(path) ? path : null;

    /// <summary>
    /// <paramref name="parts"/> joined under the folder; under <see cref="FallbackFor"/> when the system knows no such
    /// folder. Always a full path.
    /// </summary>
    public static string Under(Environment.SpecialFolder folder, params string[] parts) => Under(folder, Resolve, parts);

    /// <param name="resolve">Gives the path of a special folder, as <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> does. For tests.</param>
    internal static string Under(Environment.SpecialFolder folder, Func<Environment.SpecialFolder, string?> resolve, params string[] parts) =>
        Path.Combine([Find(folder, resolve) ?? FallbackFor(folder), .. parts]);

    /// <summary>
    /// The stand-in for a folder the system does not know: <c>Hindsound/&lt;folder&gt;</c> in the temporary folder, one
    /// per special folder so that clips, settings and log never share a folder. The files there may not last, which beats
    /// a relative path or no file at all.
    /// </summary>
    public static string FallbackFor(Environment.SpecialFolder folder) =>
        Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Hindsound", folder.ToString());

    private static string Resolve(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
}
