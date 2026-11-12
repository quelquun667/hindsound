using System.Security.Cryptography;
using System.Text;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The texts at the top of licenses/, which the Setup and the portable zip take, are byte for byte those of the tag v0.5.0:
/// the checks of the Setup and of the zip compare with the repository, so a text changed here would go out unnoticed.
/// </summary>
public sealed class WindowsLicenceTextsTests
{
    private static readonly string Root = RepositoryRoot();

    /// <summary>The git blob of each text in the tag v0.5.0 (git ls-tree v0.5.0 licenses/).</summary>
    public static TheoryData<string, string> Blobs => new()
    {
        { "InnoSetup-license.txt", "ac4dfe9e6543a360c5422b6f1fb88075e8b6073d" },
        { "LAME-COPYING.txt", "f5030495bf6ed4609db75324b1b6e94f97790d6a" },
        { "LAME-LICENSE.txt", "430a6a0e66bef7afd80de03966791c241d00e137" },
        { "LAME-README.txt", "77c6c47ae2b739d460e82526fab1744dfb145c44" },
        { "NAudio-LICENSE.txt", "034ae805fbf6887e4b415771dd8548e4ac4f179a" },
        { "NAudio.Lame-LICENSE.txt", "f9ad3fd4c462cb4f98c38b194d8a2f25147b7798" },
        { "System.Numerics.Tensors-LICENSE.txt", "fa3121dfaf73735faa1de75487de977993c40d0b" },
        { "System.Numerics.Tensors-THIRD-PARTY-NOTICES.txt", "6c9991ecf9cddf9a19180e91bc618c4869627f91" },
        { "dotnet-runtime-LICENSE.txt", "fa3121dfaf73735faa1de75487de977993c40d0b" },
        { "dotnet-runtime-THIRD-PARTY-NOTICES.txt", "0822dfb49f477c43b9b5bf1aac20b454a6943f27" },
    };

    [Theory]
    [MemberData(nameof(Blobs))]
    public void Each_text_is_the_one_of_0_5_0(string file, string blob)
    {
        var path = Path.Combine(Root, "licenses", file);

        Assert.True(File.Exists(path), file + " is not at the top of licenses/");
        Assert.Equal(blob, GitBlob(path));
    }

    [Fact]
    public void Every_text_at_the_top_has_its_blob_here()
    {
        var files = Directory.GetFiles(Path.Combine(Root, "licenses"), "*.txt").Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal);

        Assert.Equal(Blobs.Select(row => (string)row[0]).Order(StringComparer.Ordinal), files);
    }

    // The .gitattributes of licenses/ keeps git from changing the line endings, so the file on disk is the blob itself.
    private static string GitBlob(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        return Convert.ToHexStringLower(SHA1.HashData([.. header, .. bytes]));
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
