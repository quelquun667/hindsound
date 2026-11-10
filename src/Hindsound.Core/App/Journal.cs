using System.Globalization;

namespace Hindsound.App;

/// <summary>A small text log for diagnosis. Writing it can never fail the app.</summary>
internal sealed class Journal
{
    private const long MaxBytes = 1024 * 1024;

    private readonly object _lock = new();

    public Journal(string path)
    {
        Path = path;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
            var file = new FileInfo(path);
            if (file.Exists && file.Length > MaxBytes)
            {
                file.Delete();
            }
        }
        catch (Exception)
        {
            // The log is optional.
        }
    }

    /// <summary>Next to a file given by <c>--settings</c>, so a test leaves nothing elsewhere; otherwise in %LOCALAPPDATA%\Hindsound.</summary>
    public static string PathFor(string? settingsPath) =>
        settingsPath is null
            ? Hindsound.Settings.UserFolders.Under(Environment.SpecialFolder.LocalApplicationData, "Hindsound", "hindsound.log")
            : System.IO.Path.ChangeExtension(System.IO.Path.GetFullPath(settingsPath), ".log");

    public string Path { get; }

    public void Write(string line)
    {
        try
        {
            var text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine;
            lock (_lock)
            {
                File.AppendAllText(Path, text);
            }
        }
        catch (Exception)
        {
            // The log is optional.
        }
    }

    /// <summary>
    /// The lines of a log another thread may be writing. A plain read refuses the file while a write holds it, and holds it
    /// against the next write: this one shares it both ways.
    /// </summary>
    public static List<string> ReadShared(string path)
    {
        var lines = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
