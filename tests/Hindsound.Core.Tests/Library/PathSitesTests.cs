using System.Text.RegularExpressions;

namespace Hindsound.Tests.Library;

/// <summary>
/// A path is compared through PathRules, which follows the file system of the machine. Reverting one site to
/// OrdinalIgnoreCase changes nothing where the file system ignores case, so no behaviour test can see it there: this reads the
/// sources instead, and runs on every system. Each use of OrdinalIgnoreCase that stays is counted and says what it compares.
/// </summary>
public class PathSitesTests
{
    /// <summary>Per file of the core, how many times OrdinalIgnoreCase is written, and what those compare: never a path of the library.</summary>
    private static readonly Dictionary<string, (int Count, string Compares)> Allowed = new()
    {
        ["App/FullscreenPolicy.cs"] = (1, "a setting value"),
        ["App/SettingsMapping.cs"] = (3, "setting values, and the default folder on Windows only"),
        ["Audio/AudioEngine.cs"] = (4, "executable names and device ids"),
        ["Audio/ProcessSnapshot.cs"] = (2, "executable names"),
        ["Export/FileNameTemplate.cs"] = (4, "reserved names and tags of a template"),
        ["Input/HotkeyGesture.cs"] = (3, "key names"),
        ["Library/AudioFileLength.cs"] = (2, "file extensions"),
        ["Library/ClipFolders.cs"] = (1, "the long-path prefix of a UNC path, a Windows spelling"),
        ["Library/ClipIndexMatch.cs"] = (2, "the suffix of a clip name"),
        ["Library/ClipScanner.cs"] = (3, "file extensions and the suffix of a clip name"),
        ["Library/LibraryFilter.cs"] = (2, "names of collections"),
        ["Library/LibraryStore.cs"] = (1, "the name of a collection"),
        ["Library/PathRules.cs"] = (2, "the definition of the comparer of paths"),
        ["Settings/AppSettings.cs"] = (2, "option values and the microphone id"),
        ["Settings/StartupRegistration.cs"] = (1, "a value of the Run key of Windows"),
        ["Ui/Window/SettingsEditor.cs"] = (6, "gestures, device ids and tags of a template"),
        ["Ui/Window/WaveView.cs"] = (2, "executable names"),
        ["Ui/Window/Widgets.cs"] = (1, "option values"),
        ["Updates/UpdateDownloader.cs"] = (2, "digests in hexadecimal"),
    };

    private static string CoreFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
            {
                return Path.Combine(folder.FullName, "src", "Hindsound.Core");
            }
        }

        throw new InvalidOperationException("Hindsound.slnx is not above the test assembly");
    }

    [Fact]
    public void No_comparison_ignores_case_except_those_that_say_what_they_compare()
    {
        var core = CoreFolder();
        var found = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(core, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal))
            {
                continue;
            }

            var count = Regex.Count(File.ReadAllText(file), "OrdinalIgnoreCase");
            if (count > 0)
            {
                found[relative] = count;
            }
        }

        var expected = Allowed.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
        var differences = found.Keys.Union(expected.Keys).Order(StringComparer.Ordinal)
            .Where(file => found.GetValueOrDefault(file) != expected.GetValueOrDefault(file))
            .Select(file => $"{file}: {found.GetValueOrDefault(file)} found, {expected.GetValueOrDefault(file)} expected")
            .ToList();

        Assert.True(differences.Count == 0, "A path is compared with PathRules.Comparer or PathRules.Comparison; list here only what is not a path:\n" + string.Join('\n', differences));
    }
}
