using System.Text;
using Hindsound.Linux.DBus;
using Hindsound.Settings;

namespace Hindsound.Linux.Desktop;

/// <summary>What a launch does about the entry of the app in the menu of applications.</summary>
internal enum EntryOutcome
{
    /// <summary>Not run from an AppImage or from the folder of the archive: whoever installed the app installed its entry.</summary>
    NotPackaged,

    /// <summary>A package of the system has one: an entry of the user's would hide it.</summary>
    SystemHasOne,

    /// <summary>The user's entry already starts this program.</summary>
    AlreadyThere,

    /// <summary>The user's entry is marked Hidden or NoDisplay: the user removed the app from the menu, or hid it there.</summary>
    Hidden,

    /// <summary>Written, with the icons: there was none, or it started another program.</summary>
    Install,
}

/// <summary>
/// The entry of the app in the menu of applications, written at launch from an AppImage or from the folder of the archive:
/// $XDG_DATA_HOME/applications, ~/.local/share/applications by default, with the icons in its icons/hicolor. The portal of
/// global shortcuts knows an app only by a desktop entry named after its id, and the notifications name it too; the packages
/// carry one, but nothing would put it where the desktop looks. It is written before anything talks to the portal.
/// </summary>
internal sealed class ApplicationEntry
{
    public const string FileName = DBusNames.AppId + ".desktop";

    public const string IconName = DBusNames.AppId + ".png";

    private readonly string _dataHome;
    private readonly IReadOnlyList<string> _systemData;

    /// <param name="dataHome">$XDG_DATA_HOME: for a test, a folder of its own.</param>
    /// <param name="systemData">The folders of $XDG_DATA_DIRS, where the packages of the system put their entries.</param>
    internal ApplicationEntry(string dataHome, IReadOnlyList<string> systemData)
    {
        _dataHome = dataHome;
        _systemData = systemData;
    }

    public string FilePath => Path.Combine(_dataHome, "applications", FileName);

    /// <summary>Writes the entry if this launch needs one, and says what it did in one line of <paramref name="log"/>.</summary>
    public static void AtLaunch(string? program, Action<string> log)
    {
        var found = ForUser(
            folder => Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetEnvironmentVariable("XDG_DATA_DIRS"));
        if (found is not { } entry)
        {
            log("desktop entry: not written (no data folder)");
            return;
        }

        // Before what the launch decides: the copy of a package of the system is no AppImage, and would stop at NotPackaged.
        if (entry.RemoveLeftBehind(program) is { } removed)
        {
            log(removed);
        }

        var share = EntryRules.PackageShare(Environment.GetEnvironmentVariable, AppContext.BaseDirectory, File.Exists);
        log(entry.Apply(share, program));
    }

    /// <summary>
    /// The entry of the user, in the data folder <paramref name="resolve"/> gives; null when it gives none, or one that is
    /// no full path: a relative one would put the entry in whatever folder the program started from.
    /// </summary>
    /// <param name="resolve">Gives the path of a special folder, as <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> does.</param>
    /// <param name="dataDirs">$XDG_DATA_DIRS.</param>
    internal static ApplicationEntry? ForUser(Func<Environment.SpecialFolder, string?> resolve, string? dataDirs) =>
        UserFolders.Find(Environment.SpecialFolder.LocalApplicationData, resolve) is { } dataHome
            ? new ApplicationEntry(dataHome, EntryRules.SystemDataFolders(dataDirs))
            : null;

    /// <summary>Does what <see cref="Decide"/> says. Answers the line for the log: no path, a home folder holds a name.</summary>
    /// <param name="share">The share folder of the package the program runs from; null when it runs from none.</param>
    public string Apply(string? share, string? program)
    {
        try
        {
            var outcome = Decide(share, program);
            if (outcome == EntryOutcome.Install)
            {
                Install(share!, program!);
            }

            return EntryRules.LineFor(outcome);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"desktop entry: not written ({ex.GetType().Name})";
        }
    }

    public EntryOutcome Decide(string? share, string? program)
    {
        // A data folder that is not a full path would land in the current folder, wherever that is.
        if (share is null || program is null || !Path.IsPathFullyQualified(_dataHome) || EntryRules.SameFolder(share, _dataHome))
        {
            return EntryOutcome.NotPackaged;
        }

        if (_systemData.Any(folder => File.Exists(Path.Combine(folder, "applications", FileName))))
        {
            return EntryOutcome.SystemHasOne;
        }

        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return EntryOutcome.Install;
        }

        return EntryRules.Judge(text, program);
    }

    /// <summary>
    /// Removes the entry another copy of the app wrote, the AppImage or the archive run before a package of the system
    /// brought its own: under the same name, the user's goes before the system's, and the menu would keep starting the old
    /// copy. Only an entry this app wrote and nobody changed since (<see cref="EntryRules.LeftBehind"/>), and only the icons
    /// equal, byte for byte, to those of the system. Answers the line for the log, with no path; null when nothing was to go.
    /// </summary>
    public string? RemoveLeftBehind(string? program)
    {
        if (program is null || !Path.IsPathFullyQualified(_dataHome))
        {
            return null;
        }

        try
        {
            string? user = null;
            foreach (var folder in _systemData)
            {
                var system = Path.Combine(folder, "applications", FileName);
                if (!File.Exists(system))
                {
                    continue;
                }

                if (user is null)
                {
                    try
                    {
                        user = File.ReadAllText(FilePath);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                    {
                        return null;
                    }
                }

                if (EntryRules.LeftBehind(user, File.ReadAllText(system), program))
                {
                    // The entry first: an entry left with no icon would show with none.
                    File.Delete(FilePath);
                    return EntryRules.RemovedLine(RemoveIcons(folder));
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"desktop entry: the one another copy of the app wrote, not removed ({ex.GetType().Name})";
        }
    }

    /// <summary>The icons of the user's theme equal to those of <paramref name="systemFolder"/>, size for size, removed: how many.</summary>
    private int RemoveIcons(string systemFolder)
    {
        var theme = Path.Combine(_dataHome, "icons", "hicolor");
        if (!Directory.Exists(theme))
        {
            return 0;
        }

        var removed = 0;
        foreach (var size in Directory.EnumerateDirectories(theme))
        {
            var icon = Path.Combine(size, "apps", IconName);
            var system = Path.Combine(systemFolder, "icons", "hicolor", Path.GetFileName(size), "apps", IconName);
            if (File.Exists(icon) && File.Exists(system) && File.ReadAllBytes(icon).AsSpan().SequenceEqual(File.ReadAllBytes(system)))
            {
                File.Delete(icon);
                removed++;
            }
        }

        // As Install does: the icon caches look at the time the top-level folder of the theme changed.
        if (removed > 0)
        {
            Directory.SetLastWriteTimeUtc(theme, DateTime.UtcNow);
        }

        return removed;
    }

    private void Install(string share, string program)
    {
        // The icons first: an entry that shows before its icon shows with none.
        var icons = Path.Combine(share, "icons", "hicolor");
        var theme = Path.Combine(_dataHome, "icons", "hicolor");
        var copied = false;
        if (Directory.Exists(icons))
        {
            foreach (var size in Directory.EnumerateDirectories(icons))
            {
                var icon = Path.Combine(size, "apps", IconName);
                if (File.Exists(icon))
                {
                    var target = Path.Combine(theme, Path.GetFileName(size), "apps");
                    Directory.CreateDirectory(target);
                    File.Copy(icon, Path.Combine(target, IconName), overwrite: true);
                    copied = true;
                }
            }
        }

        // An icon cache, says the Icon Theme specification, looks at the time the top-level folder of the theme changed, and an
        // installer is to change it: a copy into a size folder already there leaves that time as it was.
        if (copied)
        {
            Directory.SetLastWriteTimeUtc(theme, DateTime.UtcNow);
        }

        var text = EntryRules.Text(File.ReadAllText(Path.Combine(share, "applications", FileName)), program);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        // Whole or not at all: the menu never reads half an entry.
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, FilePath, overwrite: true);
    }
}

/// <summary>The rules of <see cref="ApplicationEntry"/> that read no disk: where the package is, and what an entry says.</summary>
internal static class EntryRules
{
    private const string Group = "[Desktop Entry]";

    /// <summary>The default of $XDG_DATA_DIRS in the Base Directory specification.</summary>
    private const string DefaultSystemData = "/usr/local/share/:/usr/share/";

    /// <summary>The folders of <paramref name="variable"/>, or of its default when it is unset or empty.</summary>
    public static IReadOnlyList<string> SystemDataFolders(string? variable) =>
        (string.IsNullOrEmpty(variable) ? DefaultSystemData : variable)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Where(folder => folder.StartsWith('/'))
            .ToList();

    /// <summary>
    /// The share folder of the package the program runs from, or null. An AppImage: its runtime sets APPIMAGE and APPDIR, and
    /// AppRun starts APPDIR/usr/bin/hindsound, beside APPDIR/usr/share. The archive: share sits next to the program. A copy
    /// installed elsewhere has neither, and is left to whoever installed it.
    /// </summary>
    public static string? PackageShare(Func<string, string?> environment, string programFolder, Func<string, bool> fileExists)
    {
        var folder = Path.TrimEndingDirectorySeparator(programFolder);
        string share;
        if (environment("APPIMAGE") is { Length: > 0 } image && image.StartsWith('/')
            && environment("APPDIR") is { Length: > 0 } appDir)
        {
            if (!SameFolder(folder, Path.Combine(appDir, "usr", "bin")))
            {
                return null;
            }

            share = Path.Combine(Path.TrimEndingDirectorySeparator(appDir), "usr", "share");
        }
        else
        {
            share = Path.Combine(folder, "share");
        }

        return fileExists(Path.Combine(share, "applications", ApplicationEntry.FileName)) ? share : null;
    }

    public static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.Ordinal);

    /// <summary>
    /// What an entry the user already has asks for: left when it starts <paramref name="program"/>, or when the user took it
    /// out of the menu (Hidden) or hid it there (NoDisplay): written again, it would come back.
    /// </summary>
    public static EntryOutcome Judge(string text, string program)
    {
        var (hidden, exec) = Read(text);
        if (hidden)
        {
            return EntryOutcome.Hidden;
        }

        return exec is not null && AutostartFile.ProgramOf(exec) == program ? EntryOutcome.AlreadyThere : EntryOutcome.Install;
    }

    /// <summary>
    /// Whether the user's entry is one another copy of the app wrote from <paramref name="system"/>, the entry of a package
    /// of the system, and is left for <paramref name="program"/> to remove. The proof it is the app's: its text is, to the
    /// byte, what <see cref="Text"/> writes from the system's entry for the program it starts. The entries of 0.6.0 to 0.8.0
    /// carry no mark, and the packaged entry has not changed since; a mark added now would prove nothing more, as it would
    /// stay in an entry the user then changed. Never an entry the user hid, nor one that starts this program.
    /// </summary>
    public static bool LeftBehind(string user, string system, string program)
    {
        var (hidden, exec) = Read(user);
        if (hidden)
        {
            return false;
        }

        var started = exec is null ? null : AutostartFile.ProgramOf(exec);
        if (started is null || started == program)
        {
            return false;
        }

        try
        {
            return string.Equals(user, Text(system, started), StringComparison.Ordinal);
        }
        catch (IOException)
        {
            // A path the app could not have written, or a system entry with no group: not the app's.
            return false;
        }
    }

    /// <summary>The keys of the [Desktop Entry] group the rules read: Hidden or NoDisplay at true, and the last Exec.</summary>
    private static (bool Hidden, string? Exec) Read(string text)
    {
        var inGroup = false;
        string? exec = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('['))
            {
                inGroup = line == Group;
                continue;
            }

            var equals = line.IndexOf('=');
            if (!inGroup || equals < 1)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if ((key is "Hidden" or "NoDisplay") && value == "true")
            {
                return (true, exec);
            }

            if (key == "Exec")
            {
                exec = value;
            }
        }

        return (false, exec);
    }

    /// <summary>
    /// The entry of the package with its Exec set to <paramref name="program"/>, quoted as the Desktop Entry specification
    /// asks: the package says only "hindsound", which an AppImage or an unpacked folder is not on the PATH as.
    /// </summary>
    public static string Text(string packaged, string program)
    {
        var exec = "Exec=" + AutostartFile.ExecOf(program);
        var lines = packaged.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var inGroup = false;
        var groupAt = -1;
        var written = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.StartsWith('['))
            {
                inGroup = line == Group;
                if (inGroup)
                {
                    groupAt = i;
                }

                continue;
            }

            if (inGroup && line.StartsWith("Exec=", StringComparison.Ordinal))
            {
                lines[i] = exec;
                written = true;
            }
        }

        if (!written)
        {
            if (groupAt < 0)
            {
                throw new IOException("the desktop entry of the package has no [Desktop Entry] group");
            }

            lines.Insert(groupAt + 1, exec);
        }

        return string.Join('\n', lines);
    }

    /// <summary>The one line a launch writes to the log.</summary>
    public static string LineFor(EntryOutcome outcome) => outcome switch
    {
        EntryOutcome.Install => "desktop entry: written to the menu of applications, with its icons",
        EntryOutcome.AlreadyThere => "desktop entry: already in the menu of applications",
        EntryOutcome.SystemHasOne => "desktop entry: a package of the system has one, nothing written",
        EntryOutcome.Hidden => "desktop entry: hidden by the user, left alone",
        _ => "desktop entry: not run from an AppImage or the folder of the archive, nothing written",
    };

    /// <summary>The line of a launch that removed the entry another copy wrote, with <paramref name="icons"/> of its icons.</summary>
    public static string RemovedLine(int icons) =>
        $"desktop entry: removed the one another copy of the app wrote, as a package of the system has one, with {icons} icon{(icons == 1 ? "" : "s")}";
}
