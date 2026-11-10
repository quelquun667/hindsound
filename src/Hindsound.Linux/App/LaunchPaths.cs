namespace Hindsound.Linux.App;

/// <summary>
/// The file a person launches the app by. An AppImage runs from a mount that changes on every launch; its runtime puts the
/// path of the image itself in <c>APPIMAGE</c>, and that is the file to start again with the session.
/// </summary>
internal static class LaunchPaths
{
    public const string AppImageVariable = "APPIMAGE";

    public static string? Current() => Resolve(Environment.GetEnvironmentVariable, Environment.ProcessPath);

    /// <summary><paramref name="processPath"/>, unless <c>APPIMAGE</c> holds an absolute path.</summary>
    public static string? Resolve(Func<string, string?> environment, string? processPath) =>
        environment(AppImageVariable) is { Length: > 0 } image && image.StartsWith('/') ? image : processPath;
}
