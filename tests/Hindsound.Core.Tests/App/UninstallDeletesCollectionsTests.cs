namespace Hindsound.Tests.App;

/// <summary>
/// What the uninstaller does with the settings and the collections, read from its script: the installer is not run here, and
/// nothing else would notice a line lost from it. The names left in the roaming folder would be the collections' names.
/// </summary>
public sealed class UninstallDeletesCollectionsTests
{
    private static string[] Script()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "installer", "Hindsound.iss")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        return File.ReadAllLines(Path.Combine(folder.FullName, "installer", "Hindsound.iss")).Select(line => line.Trim()).ToArray();
    }

    /// <summary>The lines of a routine, from its header to the first <c>end;</c> that starts a line of its own.</summary>
    private static List<string> Routine(string[] script, string header)
    {
        var start = Array.FindIndex(script, line => line.StartsWith(header, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{header} is in the script");
        var lines = new List<string>();
        for (var i = start; i < script.Length && script[i] != "end;"; i++)
        {
            lines.Add(script[i]);
        }

        return lines;
    }

    [Fact]
    public void Both_the_settings_and_the_collections_go_in_the_branch_of_the_answer()
    {
        var step = Routine(Script(), "procedure CurUninstallStepChanged");
        var branch = step.FindIndex(line => line.Contains("usPostUninstall") && line.Contains("DeleteSettingsWanted"));

        Assert.True(branch >= 0, "the deletion is for the last step, and only under the answer");
        Assert.Contains(step.Skip(branch), line => line.Contains("DelTree(Folder + '\\settings.json*'"));
        Assert.Contains(step.Skip(branch), line => line.Contains("DelTree(Folder + '\\library.json*'"));
    }

    [Fact]
    public void The_collections_go_with_their_copies_and_leftovers_by_name_not_with_the_folder()
    {
        var step = Routine(Script(), "procedure CurUninstallStepChanged");

        // The star makes it .bak and .tmp too; a wider pattern could take clips if the clips folder was set to this one.
        Assert.DoesNotContain(step, line => line.Contains("DelTree(Folder, "));
        Assert.DoesNotContain(step, line => line.Contains("DelTree(Folder + '\\*"));
        Assert.Contains(step, line => line.Contains("'\\library.json*', False, True, False)"));
    }

    [Fact]
    public void A_silent_uninstallation_keeps_them_and_only_a_yes_deletes_them()
    {
        var answer = string.Join(' ', Routine(Script(), "function DeleteSettingsWanted"));
        var asked = answer[answer.IndexOf("#else", StringComparison.Ordinal)..];

        Assert.Contains("(not UninstallSilent) and", asked);
        Assert.Contains("Also delete your Hindsound settings and collections?", asked);
        Assert.Contains("= IDYES", asked);
        Assert.Contains("MB_DEFBUTTON2", asked);
    }
}
