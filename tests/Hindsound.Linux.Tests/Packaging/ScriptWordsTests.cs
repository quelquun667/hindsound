namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The scripts that build the Linux packages are public, and the scan run before a push refuses a few words in a file of
/// the repository: a comment must name another folder in other words (SDL's own documentation folder, for one).
/// </summary>
public sealed class ScriptWordsTests
{
    private static readonly string Root = RepositoryRoot();

    // Built from pieces, so that this file does not hold the words it looks for.
    private static readonly string[] Forbidden = ["docs" + "/", "Clau" + "de", "work" + "tree", "PL" + "AN"];

    public static TheoryData<string> Scripts()
    {
        var data = new TheoryData<string>();
        foreach (var folder in new[] { Path.Combine("tools", "linux"), Path.Combine("packaging", "linux") })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(Root, folder), "*.sh"))
            {
                data.Add(Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void A_script_of_the_Linux_build_holds_none_of_the_words_the_push_scan_refuses(string script)
    {
        var text = File.ReadAllText(Path.Combine(Root, script));

        foreach (var word in Forbidden)
        {
            Assert.DoesNotContain(word, text, StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("Hindsound.slnx is not above the test assembly");
    }
}
