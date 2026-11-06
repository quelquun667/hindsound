using System.Globalization;
using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>A small text log for diagnosis. Writing it can never fail the app.</summary>
internal sealed unsafe class Journal
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
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hindsound", "hindsound.log")
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

    /// <summary>Working set and private bytes of this process, in MB.</summary>
    public static string Memory()
    {
        Native.PROCESS_MEMORY_COUNTERS_EX counters = default;
        counters.Size = (uint)sizeof(Native.PROCESS_MEMORY_COUNTERS_EX);
        Native.K32GetProcessMemoryInfo(Native.GetCurrentProcess(), &counters, counters.Size);
        const double mb = 1024 * 1024;
        return string.Create(CultureInfo.InvariantCulture, $"working set {counters.WorkingSet / mb:F1} MB, private {counters.Private / mb:F1} MB");
    }

    /// <summary>CPU time used by this process so far, in milliseconds.</summary>
    public static double CpuMilliseconds()
    {
        long creation, exit, kernel, user;
        Native.GetProcessTimes(Native.GetCurrentProcess(), &creation, &exit, &kernel, &user);
        return (kernel + user) / 10000.0;
    }
}
