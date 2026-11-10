using System.Globalization;
using Hindsound.Audio;

namespace Hindsound.Linux.Audio;

/// <summary>
/// The process list, read from /proc: one folder per process, named by its id, whose stat file gives its name and its
/// parent. The name is the kernel's, the program's file name cut to 15 bytes. Lowest id first.
/// </summary>
internal sealed class ProcProcessSnapshot : IProcessSnapshot
{
    private readonly string _root;

    /// <param name="root">Where to read: /proc, or a folder made like it by a test.</param>
    public ProcProcessSnapshot(string root = "/proc") => _root = root;

    public IReadOnlyList<ProcessEntry> Take()
    {
        var entries = new List<ProcessEntry>();
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(_root))
            {
                if (!int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                {
                    continue;
                }

                string line;
                try
                {
                    line = File.ReadAllText(Path.Combine(folder, "stat"));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The process ended between the listing and the read.
                    continue;
                }

                if (ParseStat(line) is { } entry && entry.Id == id)
                {
                    entries.Add(entry);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No /proc, or the listing failed half way: what was read stands.
        }

        entries.Sort((a, b) => a.Id.CompareTo(b.Id));
        return entries;
    }

    /// <summary>
    /// Reads "pid (name) state ppid ..." from /proc/[pid]/stat. The name may hold spaces and parentheses of its own, so it
    /// runs to the last closing parenthesis of the line.
    /// </summary>
    /// <returns>The entry, or null if the line is not one.</returns>
    public static ProcessEntry? ParseStat(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var open = line.IndexOf(" (", StringComparison.Ordinal);
        var close = line.LastIndexOf(')');
        if (open <= 0 || close < open + 2)
        {
            return null;
        }

        if (!int.TryParse(line.AsSpan(0, open), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var name = line[(open + 2)..close];
        var rest = line[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (rest.Length < 2 || !int.TryParse(rest[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parentId))
        {
            return null;
        }

        return new ProcessEntry(id, parentId, name);
    }
}
