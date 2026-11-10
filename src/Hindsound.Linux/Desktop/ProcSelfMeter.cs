using System.Globalization;
using Hindsound.App;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// Memory and processor time of this process. The memory comes from /proc/self/status: VmRSS is what Windows calls the
/// working set, and RssAnon the memory of the process's own, the nearest to Windows' private bytes.
/// </summary>
internal sealed class ProcSelfMeter : IProcessMeter
{
    public const string StatusFile = "/proc/self/status";

    public string Memory()
    {
        try
        {
            return MemoryLine(File.ReadAllText(StatusFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "memory not measured (" + ex.GetType().Name + ")";
        }
    }

    public double CpuMilliseconds() => Environment.CpuUsage.TotalTime.TotalMilliseconds;

    /// <summary>The line of the log for the text of /proc/self/status, in the words of the Windows meter.</summary>
    public static string MemoryLine(string status)
    {
        var rss = KilobytesOf(status, "VmRSS");
        var own = KilobytesOf(status, "RssAnon");
        if (rss is null || own is null)
        {
            return "memory not measured (no VmRSS or RssAnon)";
        }

        return string.Create(CultureInfo.InvariantCulture, $"working set {rss.Value / 1024.0:F1} MB, private {own.Value / 1024.0:F1} MB");
    }

    /// <summary>The value of a line such as "VmRSS:     1234 kB", in kB; null when the line is missing or odd.</summary>
    public static long? KilobytesOf(string status, string field)
    {
        foreach (var line in status.Split('\n'))
        {
            if (!line.StartsWith(field + ":", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line[(field.Length + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return parts is [var number, "kB"] && long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var kb) ? kb : null;
        }

        return null;
    }
}
