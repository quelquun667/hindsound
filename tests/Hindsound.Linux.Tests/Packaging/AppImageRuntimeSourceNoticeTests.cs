namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The runtime's binary is compiled from one source file of its own, src/runtime/runtime.c, and that file carries its own
/// MIT notice, with copyright holders the repository's LICENSE does not name. Its MIT terms ask for that copyright notice
/// in every copy, so the notices must reproduce it, as the header of the file reads at the tag 20251108.
/// </summary>
public sealed class AppImageRuntimeSourceNoticeTests
{
    private static readonly string Root = RepositoryRoot();

    /// <summary>The copyright lines of the header of src/runtime/runtime.c at the tag 20251108, without the leading " * ".</summary>
    private static readonly string[] HeaderCopyright =
    [
        "Copyright (c) 2004-24 Simon Peter",
        "Portions Copyright (c) 2007 Alexander Larsson",
        "Portions from WjCryptLib_Md5 originally written by Alexander Peslyak,",
        "  modified by WaterJuice retaining Public Domain license",
    ];

    [Fact]
    public void The_runtime_row_names_the_copyright_of_its_source_file()
    {
        var copyright = RuntimeRow()[4];

        Assert.Contains("Copyright (c) 2004-23 probonopd", copyright, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2004-24 Simon Peter", copyright, StringComparison.Ordinal);
        Assert.Contains("portions Copyright (c) 2007 Alexander Larsson", copyright, StringComparison.Ordinal);
        Assert.Contains("`src/runtime/runtime.c`", copyright, StringComparison.Ordinal);
        Assert.Contains("WjCryptLib_Md5", copyright, StringComparison.Ordinal);
        Assert.Contains("public domain", copyright, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runtime_section_quotes_the_header_of_its_source_file_as_it_reads()
    {
        var section = RuntimeSection();
        var quoted = string.Join('\n', HeaderCopyright);

        Assert.Contains("```text\n" + quoted + "\n```", section, StringComparison.Ordinal);
        Assert.Contains("`src/runtime/runtime.c` at the tag `20251108`", section, StringComparison.Ordinal);
        Assert.Contains("the same permission notice and disclaimer, word for word, as that `LICENSE`", section, StringComparison.Ordinal);
    }

    /// <summary>The section "## The AppImage runtime", up to the next heading of level 2.</summary>
    private static string RuntimeSection()
    {
        var lines = NoticeLines();
        var start = lines.IndexOf("## The AppImage runtime");
        Assert.True(start >= 0, "The notices have no section on the AppImage runtime");
        var end = lines.FindIndex(start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', lines.Skip(start).Take((end < 0 ? lines.Count : end) - start));
    }

    private static string[] RuntimeRow()
    {
        var rows = NoticeLines().Where(line => line.StartsWith("| AppImage type2 runtime ", StringComparison.Ordinal)).ToList();
        Assert.Single(rows);
        return rows[0].Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static List<string> NoticeLines() =>
        File.ReadAllLines(Path.Combine(Root, "THIRD-PARTY-NOTICES.md")).Select(line => line.TrimEnd('\r')).ToList();

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
