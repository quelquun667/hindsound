using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The type2 runtime at the head of the AppImage is MIT, but it is one static executable with libraries built into it,
/// libfuse under the LGPL-2.1 among them. Each of them needs its row in the notices and its upstream text in licenses/linux/,
/// copied unchanged from the version the runtime of release 20251108 was built with: the git blob of each file is pinned
/// here, as the upstream repository has it at that tag.
/// </summary>
public sealed partial class AppImageRuntimeLicenceTests
{
    private static readonly string Root = RepositoryRoot();

    private const string InAppImage = "Linux (AppImage only)";

    /// <summary>The runtime and each library built into it: its row, its texts with their git blobs, the upstream ref.</summary>
    private static readonly Dictionary<string, (string[] Files, string[] Blobs, string Ref)> Components = new()
    {
        ["AppImage type2 runtime"] = (["AppImage-type2-runtime-LICENSE.txt"], ["a9fd00b042f392357a84fd81b7789976a53f5b4e"], "dd6cebedcbddde9c82f89b011e8e1d40b6e43868"),
        ["musl libc"] = (["musl-COPYRIGHT.txt"], ["c1628e9ac84f927fe791124ec172fb61c760bb9b"], "?h=v1.2.5"),
        ["libfuse"] = (["libfuse-LGPL2.txt", "libfuse-LICENSE.txt"], ["4362b49151d7b34ef83b3067a8f9c9f877d72a0e", "184a5b27c6cfec5ff4c70ab186ab94c82dae1de3"], "6d08472ea47db895748d5ca7d3daf032c3fefcf8"),
        ["squashfuse"] = (["squashfuse-LICENSE.txt"], ["2c821bbaa662c7e863424b0523f032b77ab75d87"], "775b4cc72ab47641637897f11ce0da15d5c1f115"),
        ["Zstandard"] = (["zstd-LICENSE.txt"], ["75800288cc243f164b9d130f125e2ffae28f8e39"], "794ea1b0afca0f020f4e57b6732332231fb23c70"),
        ["zlib"] = (["zlib-LICENSE.txt"], ["ab8ee6f71428c3e4646b12b64ad387cde33462e5"], "51b7f2abdade71cd9bb0e7a373ef2610ec6f9daf"),
        ["mimalloc"] = (["mimalloc-LICENSE.txt"], ["670b668a0c928e5c29439d6dbf43091ed8932f56"], "8c532c32c3c96e5ba1f2283e032f69ead8add00f"),
    };

    public static TheoryData<string> ComponentNames() => new(Components.Keys);

    [Fact]
    public void The_runtime_row_records_its_licence()
    {
        var row = Row("AppImage type2 runtime");

        Assert.StartsWith("MIT, ", row[3], StringComparison.Ordinal);
        Assert.Equal("Copyright (c) 2004-23 probonopd (its `LICENSE`); Copyright (c) 2004-24 Simon Peter, portions Copyright (c) 2007 Alexander Larsson (the header of `src/runtime/runtime.c` at the tag); MD5 code from WjCryptLib_Md5, by Alexander Peslyak, modified by WaterJuice, public domain", row[4]);
        Assert.Contains("`LICENSE` of [AppImage/type2-runtime]", row[6], StringComparison.Ordinal);
        Assert.Contains("at the tag `20251108`", row[6], StringComparison.Ordinal);
        Assert.DoesNotContain("Not recorded yet", string.Join('\n', NoticeLines()), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ComponentNames))]
    public void Each_part_of_the_runtime_has_its_row_in_the_AppImage_only(string component)
    {
        var row = Row(component);

        Assert.Equal(InAppImage, row[7]);
        Assert.False(string.IsNullOrWhiteSpace(row[2]), component + " has no version");
        Assert.False(string.IsNullOrWhiteSpace(row[4]), component + " has no copyright");
        Assert.Contains(Components[component].Ref, row[6], StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ComponentNames))]
    public void The_row_links_exactly_the_texts_of_its_component(string component)
    {
        var linked = LicenceLink().Matches(Row(component)[5]).Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(Components[component].Files.Select(file => "licenses/linux/" + file), linked);
    }

    [Theory]
    [MemberData(nameof(ComponentNames))]
    public void Each_text_is_the_upstream_file_unchanged(string component)
    {
        var (files, blobs, _) = Components[component];
        for (var i = 0; i < files.Length; i++)
        {
            var path = Path.Combine(Root, "licenses", "linux", files[i]);
            Assert.True(File.Exists(path), files[i] + " is not in licenses/linux/");
            Assert.NotEqual(0, new FileInfo(path).Length);
            Assert.Equal(blobs[i], GitBlob(path));
        }
    }

    [Fact]
    public void Every_library_the_runtime_licence_names_has_a_row()
    {
        var named = NamedLibrary().Matches(File.ReadAllText(Path.Combine(Root, "licenses", "linux", "AppImage-type2-runtime-LICENSE.txt")))
            .Select(match => match.Groups[1].Value)
            .ToList();
        var rows = NoticeLines().Where(line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains("in the AppImage runtime)", StringComparison.Ordinal)).ToList();

        Assert.Equal(5, named.Count);
        foreach (var library in named)
        {
            Assert.True(rows.Any(row => row.Split('|')[1].Contains(library, StringComparison.Ordinal)), library + " is named by the runtime's LICENSE but has no row");
        }
    }

    [Fact]
    public void The_notices_say_where_the_exact_libfuse_source_is()
    {
        var section = Section("### libfuse and the LGPL-2.1");

        Assert.Contains("<https://github.com/libfuse/libfuse/releases/download/fuse-3.15.0/fuse-3.15.0.tar.xz>", section, StringComparison.Ordinal);
        Assert.Contains("`70589cfd5e1cff7ccd6ac91c86c01be340b227285c5e200baa284e401eea2ca0`", section, StringComparison.Ordinal);
        Assert.Contains("<https://github.com/AppImage/type2-runtime/blob/20251108/patches/libfuse/mount.c.diff>", section, StringComparison.Ordinal);
        Assert.Contains("<https://github.com/AppImage/type2-runtime/tree/20251108>", section, StringComparison.Ordinal);
        Assert.Contains("section 6", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_notices_say_the_runtime_licence_leaves_mimalloc_out()
    {
        var section = Section("### What is built into it");

        Assert.Contains("-lmimalloc", section, StringComparison.Ordinal);
        Assert.Contains("does not name it", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Windows_component_says_whether_it_is_in_the_portable_zip()
    {
        var windows = NoticeLines()
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && !line.StartsWith("| Component ", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(row => row[7].Contains("Windows", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(windows);
        foreach (var row in windows)
        {
            if (row[1].StartsWith("Inno Setup ", StringComparison.Ordinal))
            {
                Assert.StartsWith("Windows (the Setup only: the portable zip has no `unins000.exe`)", row[7], StringComparison.Ordinal);
                continue;
            }

            Assert.True(WindowsBuild().IsMatch(row[7]), row[1] + " does not name the Windows build as its own label");
            Assert.False(WindowsBuildWithoutZip().IsMatch(row[7]), row[1] + " names the Windows build without the portable zip");
        }

        Assert.Contains("The Windows build comes as the Setup and as the portable zip", string.Join('\n', NoticeLines()), StringComparison.Ordinal);
    }

    /// <summary>The git blob hash of a file, with the CRLF a Windows clone may have written turned back into LF.</summary>
    private static string GitBlob(string path)
    {
        var read = File.ReadAllBytes(path);
        var bytes = read.Where((value, index) => !(value == '\r' && index + 1 < read.Length && read[index + 1] == '\n')).ToArray();
        var header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        return Convert.ToHexStringLower(SHA1.HashData([.. header, .. bytes]));
    }

    /// <summary>The text of a section of the notices, up to the next heading of the same or a higher level.</summary>
    private static string Section(string heading)
    {
        var lines = NoticeLines();
        var start = lines.IndexOf(heading);
        Assert.True(start >= 0, heading + " is not a heading of the notices");
        var level = heading.TakeWhile(c => c == '#').Count();
        var end = lines.FindIndex(start + 1, line => line.StartsWith('#') && line.TakeWhile(c => c == '#').Count() <= level);
        return string.Join('\n', lines.Skip(start).Take((end < 0 ? lines.Count : end) - start));
    }

    /// <summary>The cells of the row of a component, the empty cells before the first bar and after the last one included.</summary>
    private static string[] Row(string component)
    {
        var rows = NoticeLines().Where(line => line.StartsWith("| " + component + " ", StringComparison.Ordinal)).ToList();
        Assert.True(rows.Count == 1, $"{rows.Count} rows of the notices name {component}");
        return rows[0].Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static List<string> NoticeLines() =>
        File.ReadAllLines(Path.Combine(Root, "THIRD-PARTY-NOTICES.md")).Select(line => line.TrimEnd('\r')).ToList();

    [GeneratedRegex(@"\]\((licenses/[^)]+)\)")]
    private static partial Regex LicenceLink();

    /// <summary>"Windows" as the label of a build in an "In" cell: at its start, or after "NAudio.Wasapi: ".</summary>
    [GeneratedRegex(@"(?:^|: )Windows")]
    private static partial Regex WindowsBuild();

    [GeneratedRegex(@"(?:^|: )Windows(?! \(the Setup and the portable zip\))")]
    private static partial Regex WindowsBuildWithoutZip();

    /// <summary>A library of the runtime's LICENSE: "* musl libc, licensed under the terms of".</summary>
    [GeneratedRegex(@"^\* (.+), licensed under the terms of", RegexOptions.Multiline)]
    private static partial Regex NamedLibrary();

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
