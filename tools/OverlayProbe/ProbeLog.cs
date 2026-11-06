using System.IO;

namespace OverlayProbe;

internal sealed class ProbeLog
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hindsound", "probe");

    public static readonly string FilePath = Path.Combine(Folder, "probe.log");

    public event Action<string>? LineWritten;

    public ProbeLog()
    {
        Directory.CreateDirectory(Folder);
    }

    public void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        File.AppendAllText(FilePath, line + Environment.NewLine);
        LineWritten?.Invoke(line);
    }
}
