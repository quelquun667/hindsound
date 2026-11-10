namespace Hindsound.Library;

/// <summary>
/// What the file system of the machine decides about a path. NTFS and APFS ignore case by default, ext4 and btrfs do not: two
/// names that differ only by case are two files there, and the library must keep them apart. Every comparison of a path in the
/// core goes through here; a comparison of an exe name, a setting or a tag does not.
/// </summary>
internal static class PathRules
{
    /// <summary>Whether two paths that differ only by case are one file, on this system.</summary>
    internal static bool IgnoresCase { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>The comparer of paths on this system.</summary>
    internal static StringComparer Comparer { get; } = ComparerFor(IgnoresCase);

    /// <summary>The comparison of paths on this system, for the calls that take a <see cref="StringComparison"/>.</summary>
    internal static StringComparison Comparison { get; } = ComparisonFor(IgnoresCase);

    internal static StringComparer ComparerFor(bool ignoresCase) => ignoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static StringComparison ComparisonFor(bool ignoresCase) => ignoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The key of a path where backslash is a name character and there is no <c>\\?\</c> form: the full path, without the
    /// separator at the end. The text is otherwise left as it is, since it is also the real path of the file.
    /// </summary>
    internal static string PortableKey(string path)
    {
        if (path.Length == 0)
        {
            return path;
        }

        return Path.TrimEndingDirectorySeparator(Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path));
    }
}
