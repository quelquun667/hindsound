namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// Every shell script of the repository runs on Linux, where a CR at the end of a line breaks it ("set -euo pipefail\r"
/// fails at the first line). A clone made on Windows with core.autocrlf keeps them with LF only if their folder says
/// eol=lf, which is what this checks in the clone the tests run from.
/// </summary>
public sealed class ShellScriptLineEndsTests
{
    public static TheoryData<string> Scripts()
    {
        var root = RepositoryRoot();
        var data = new TheoryData<string>();
        foreach (var folder in new[] { Path.Combine("tools", "release"), Path.Combine("packaging", "linux") })
        {
            foreach (var script in Directory.EnumerateFiles(Path.Combine(root, folder), "*.sh").Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetRelativePath(root, script));
            }
        }

        return data;
    }

    [Fact]
    public void The_Linux_LAME_build_is_among_the_scripts() =>
        Assert.Contains(new object[] { Path.Combine("tools", "release", "build-lame-linux.sh") }, Scripts().Cast<object[]>(), RowComparer.Instance);

    private sealed class RowComparer : IEqualityComparer<object[]>
    {
        public static readonly RowComparer Instance = new();

        public bool Equals(object[]? x, object[]? y) => x is not null && y is not null && x.SequenceEqual(y);

        public int GetHashCode(object[] obj) => obj.Length;
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void A_shell_script_has_no_carriage_return(string script)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), script));
        Assert.True(Array.IndexOf(bytes, (byte)'\r') < 0, script + " has a CR: its folder needs '*.sh text eol=lf' in .gitattributes");
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
