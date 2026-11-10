using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Hindsound.Linux.DBus;

namespace Hindsound.Linux.Tests.Packaging;

/// <summary>
/// The files of packaging/linux, read as text: the desktop entry, the icons, AppRun and the two scripts. The packages
/// themselves are made and started only by the linux-package job of the CI; these tests keep the files that job uses in
/// line with the app (its id, its program name) on every system.
/// </summary>
public sealed partial class LinuxPackageFilesTests
{
    private static readonly string Folder = Path.Combine(RepositoryRoot(), "packaging", "linux");

    private static string DesktopEntryPath => Path.Combine(Folder, DBusNames.AppId + ".desktop");

    [Fact]
    public void The_desktop_entry_is_named_after_the_id_of_the_app() => Assert.True(File.Exists(DesktopEntryPath), DesktopEntryPath);

    [Fact]
    public void The_desktop_entry_has_one_group_and_no_key_twice()
    {
        var groups = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in Meaningful(File.ReadAllLines(DesktopEntryPath)))
        {
            if (line.StartsWith('['))
            {
                groups.Add(line);
                continue;
            }

            Assert.True(groups.Count > 0, "a key before the first group: " + line);
            var equals = line.IndexOf('=');
            Assert.True(equals > 0, "not a key: " + line);
            var key = line[..equals];
            Assert.Equal(key.Trim(), key);
            Assert.True(keys.Add(key), "twice: " + key);
        }

        Assert.Equal(new[] { "[Desktop Entry]" }, groups);
    }

    [Theory]
    [InlineData("Type", "Application")]
    [InlineData("Name", "Hindsound")]
    [InlineData("Icon", DBusNames.AppId)]
    [InlineData("Terminal", "false")]
    [InlineData("Categories", "AudioVideo;Audio;Utility;")]
    [InlineData("X-GNOME-UsesNotifications", "true")]
    public void The_desktop_entry_says_what_the_desktops_read(string key, string value) => Assert.Equal(value, Entry()[key]);

    [Fact]
    public void The_desktop_entry_starts_the_program_of_the_Linux_app() =>
        Assert.Equal(typeof(DBusNames).Assembly.GetName().Name, Entry()["Exec"]);

    [Theory]
    [InlineData(48)]
    [InlineData(128)]
    [InlineData(256)]
    public void Each_icon_is_a_square_PNG_of_its_size_with_alpha(int size)
    {
        var path = Path.Combine(Folder, "icons", "hicolor", $"{size}x{size}", "apps", DBusNames.AppId + ".png");
        var bytes = File.ReadAllBytes(path);

        // The signature, then the IHDR chunk: width, height, bit depth and colour type (6, RGBA).
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.Equal(8, bytes[24]);
        Assert.Equal(6, bytes[25]);
    }

    [Theory]
    [InlineData("build-packages.sh")]
    [InlineData("check-packages.sh")]
    public void The_scripts_use_the_id_of_the_app(string script) =>
        Assert.Contains($"readonly APP_ID='{DBusNames.AppId}'", File.ReadAllLines(Path.Combine(Folder, script)));

    [Fact]
    public void AppRun_starts_the_program_where_the_build_puts_it()
    {
        var appRun = File.ReadAllLines(Path.Combine(Folder, "AppRun"));
        var build = File.ReadAllText(Path.Combine(Folder, "build-packages.sh"));

        Assert.Equal("#!/bin/sh", appRun[0]);
        Assert.Equal("exec \"$here/usr/bin/hindsound\" \"$@\"", Meaningful(appRun).Last());
        Assert.Contains("lay_out \"$appdir/usr/bin\" ", build, StringComparison.Ordinal);
        Assert.Contains("install -m 755 -- \"$here/AppRun\" \"$appdir/AppRun\"", build, StringComparison.Ordinal);
    }

    [Fact]
    public void The_build_tools_are_pinned_to_a_release_and_a_sha256()
    {
        var build = File.ReadAllText(Path.Combine(Folder, "build-packages.sh"));
        foreach (var tool in new[] { "APPIMAGETOOL", "RUNTIME" })
        {
            var url = Assigned(build, tool + "_URL");
            Assert.StartsWith("https://github.com/AppImage/", url, StringComparison.Ordinal);
            Assert.DoesNotContain("/continuous/", url, StringComparison.Ordinal);
            Assert.Matches(Sha256(), Assigned(build, tool + "_SHA256"));
        }

        Assert.NotEqual(Assigned(build, "APPIMAGETOOL_SHA256"), Assigned(build, "RUNTIME_SHA256"));
    }

    [Theory]
    [InlineData("AppRun")]
    [InlineData("build-packages.sh")]
    [InlineData("check-packages.sh")]
    [InlineData("README.txt")]
    [InlineData(DBusNames.AppId + ".desktop")]
    public void A_file_read_on_Linux_has_no_carriage_return_and_no_byte_order_mark(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Folder, file));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), file + " starts with a byte order mark");
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    [Fact]
    public void The_CI_makes_and_checks_both_packages()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        Assert.Contains("bash packaging/linux/build-packages.sh ", ci, StringComparison.Ordinal);
        Assert.Contains("bash packaging/linux/check-packages.sh ", ci, StringComparison.Ordinal);
        Assert.Contains("bash tools/release/build-lame-linux.sh ", ci, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Entry() =>
        Meaningful(File.ReadAllLines(DesktopEntryPath))
            .Where(line => !line.StartsWith('['))
            .Select(line => line.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

    /// <summary>The lines that say something: no blank line, no comment (Desktop Entry Specification, Basic format).</summary>
    private static IEnumerable<string> Meaningful(IEnumerable<string> lines) =>
        lines.Where(line => line.Trim().Length > 0 && !line.StartsWith('#'));

    /// <summary>The value of <c>readonly NAME='value'</c> in a script.</summary>
    private static string Assigned(string script, string name)
    {
        var match = Regex.Match(script, "^readonly " + name + "='([^']*)'$", RegexOptions.Multiline);
        Assert.True(match.Success, name + " is not set in the script");
        return match.Groups[1].Value;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();

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
